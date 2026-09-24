using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SawmillService.Controllers;
using SawmillService.DTOs;
using SawmillService.Events;
using SawmillService.Models;
using SawmillService.Repositories;
using SawmillService.Services;

namespace SawmillService.Tests;

/// <summary>
/// Covers the fire-and-forget logs-consumed flow in StartSawJob: the job is
/// committed in SawmillDB, a LogsConsumedEvent is published to Kafka, and a
/// publish failure must NEVER fail the request or trigger the old
/// consume-and-rollback path. The synchronous validation reads
/// (GetStockAsync / GetLogsAsync) are exercised against a real LogIntakeClient
/// backed by a fake HTTP handler, so they are kept exactly as-is.
/// </summary>
public class SawmillControllerStartSawJobTests
{
    private readonly Mock<ISawmillRepository> _repo;
    private readonly Mock<KafkaProducerService> _producer;
    private readonly Mock<LogsConsumedProducerService> _logsConsumedProducer;
    private readonly FakeLogIntakeHandler _logIntakeHandler;
    private readonly SawmillController _controller;

    public SawmillControllerStartSawJobTests()
    {
        _repo = new Mock<ISawmillRepository>();
        _producer = new Mock<KafkaProducerService>("localhost:9092", "raw-stock-reversed");
        _logsConsumedProducer = new Mock<LogsConsumedProducerService>("localhost:9092", "logs-consumed");
        _logIntakeHandler = new FakeLogIntakeHandler();

        // Real LogIntakeClient over a canned HTTP handler, so the synchronous
        // validation reads (stock + in-stock logs) succeed with fake data.
        var client = new LogIntakeClient(
            new HttpClient(_logIntakeHandler)
            {
                BaseAddress = new Uri("http://localhost:5201/api/LogIntake/")
            },
            NullLogger<LogIntakeClient>.Instance);

        _controller = new SawmillController(
            _repo.Object,
            client,
            _producer.Object,
            _logsConsumedProducer.Object,
            NullLogger<SawmillController>.Instance);

        // Mount a ClaimsPrincipal carrying a userId claim, mirroring how the JWT
        // middleware exposes the caller's id to authenticated actions.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim("userId", "42") }, "Test"))
            }
        };

        // StartSawJob re-reads live stock/logs from LogIntakeService via this token.
        _controller.Request.Headers["Authorization"] = "Bearer test-token";
    }

    private void SetupValidSawmillDependencies()
    {
        _repo.Setup(r => r.GetWorkersByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Worker>
            {
                new() { WorkerId = 1, EmployeeCode = "EMP-011", FullName = "Kamal Perera", IsActive = true }
            });

        _repo.Setup(r => r.GetMachineByIdAsync(1))
            .ReturnsAsync(new Machine
            {
                MachineId = 1,
                MachineCode = "MCH-01",
                Name = "Circular Saw Rig A",
                Status = "Available"
            });

        _repo.Setup(r => r.CreateSawJobAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<IEnumerable<(int, decimal)>>(),
                It.IsAny<IEnumerable<int>>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(new SawJob
            {
                SawJobId = 7,
                JobCode = "SAW-007",
                StockId = 1,
                SpeciesName = "Jak",
                LengthFt = 8.00m,
                TotalVolumeM3 = 0.6721m,
                Status = "InProgress",
                StartedBy = 42,
                StartedAt = DateTime.UtcNow,
                MachineId = 1,
                MachineCode = "MCH-01",
                MachineName = "Circular Saw Rig A"
            });
    }

    private static StartSawJobDto ValidDto() => new()
    {
        StockId = 1,
        LogIds = new List<int> { 101 },
        WorkerIds = new List<int> { 1 },
        MachineId = 1,
        Notes = "Batch one"
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Publish behavior — the whole point of the fire-and-forget flow
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartSawJob_Success_Returns201AndPublishesLogsConsumedEvent()
    {
        SetupValidSawmillDependencies();

        var result = await _controller.StartSawJob(ValidDto());

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, objectResult.StatusCode);

        _logsConsumedProducer.Verify(p => p.PublishLogsConsumedAsync(It.Is<LogsConsumedEvent>(e =>
            e.SawJobId == 7 &&
            e.StartedBy == 42 &&
            e.Logs.Count == 1 &&
            e.Logs[0].LogId == 101 &&
            e.Logs[0].VolumeM3 == 0.6721m)), Times.Once);
    }

    [Fact]
    public async Task StartSawJob_PublishThrows_StillReturns201AndNeverCancels()
    {
        SetupValidSawmillDependencies();
        _logsConsumedProducer.Setup(p => p.PublishLogsConsumedAsync(It.IsAny<LogsConsumedEvent>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await _controller.StartSawJob(ValidDto());

        // Fire-and-forget: a Kafka failure must not fail the request...
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, objectResult.StatusCode);

        // ...and must not trigger the old consume-and-rollback path.
        _repo.Verify(r => r.CancelJobAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task StartSawJob_NeverCallsConsumeLogsEndpointOverHttp()
    {
        SetupValidSawmillDependencies();

        await _controller.StartSawJob(ValidDto());

        // The old flow PUT /api/LogIntake/logs/consume must no longer happen —
        // stock state now only travels via the Kafka logs-consumed event.
        Assert.DoesNotContain(_logIntakeHandler.RequestPaths,
            p => p.Contains("consume", StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fake HTTP handler — canned LogIntakeService responses for the synchronous
    // validation reads, plus a recorder to prove no consume request is made.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeLogIntakeHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            RequestPaths.Add(path);

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (path.EndsWith("/stock", StringComparison.OrdinalIgnoreCase))
            {
                response.Content = new StringContent(
                    """
                    [
                      { "stockId": 1, "speciesId": 2, "species": "Jak", "lengthId": 3,
                        "lengthFt": 8.00, "logCount": 1, "totalVolumeM3": 0.6721,
                        "lowStockThreshold": 0.5 }
                    ]
                    """,
                    Encoding.UTF8, "application/json");
            }
            else if (path.EndsWith("/logs", StringComparison.OrdinalIgnoreCase))
            {
                response.Content = new StringContent(
                    """
                    [
                      { "logId": 101, "stockId": 1, "speciesId": 2, "speciesName": "Jak",
                        "lengthId": 3, "lengthFt": 8.00, "girthFt": 3.2,
                        "volumeM3": 0.6721, "status": "InStock", "deliveryId": 9 }
                    ]
                    """,
                    Encoding.UTF8, "application/json");
            }
            else
            {
                response.StatusCode = HttpStatusCode.NotFound;
            }

            return Task.FromResult(response);
        }
    }
}