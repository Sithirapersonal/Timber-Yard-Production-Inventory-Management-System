using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SawmillService.Controllers;
using SawmillService.Models;
using SawmillService.Repositories;
using SawmillService.Services;

namespace SawmillService.Tests;

/// <summary>
/// Covers GET api/Sawmill/jobs/history — the Job History tab's backend contract:
/// optional from/to date bounds (parsed by the shared TryParseReportDate helper,
/// passed through to the repository), invalid dates rejected with the same
/// message format as the Wastage &amp; Yield Report, and the endpoint protected
/// only by the controller's class-level [Authorize] (no per-role gate).
/// </summary>
public class SawmillControllerJobHistoryTests
{
    private readonly Mock<ISawmillRepository> _repo;
    private readonly Mock<KafkaProducerService> _producer;
    private readonly Mock<LogsConsumedProducerService> _logsConsumedProducer;
    private readonly SawmillController _controller;

    public SawmillControllerJobHistoryTests()
    {
        _repo = new Mock<ISawmillRepository>();
        _producer = new Mock<KafkaProducerService>("localhost:9092", "raw-stock-reversed");
        _logsConsumedProducer = new Mock<LogsConsumedProducerService>("localhost:9092", "logs-consumed");

        _controller = new SawmillController(
            _repo.Object,
            new LogIntakeClient(
                new HttpClient { BaseAddress = new Uri("http://localhost:5201/api/LogIntake/") },
                NullLogger<LogIntakeClient>.Instance),
            _producer.Object,
            _logsConsumedProducer.Object,
            NullLogger<SawmillController>.Instance);

        // Mount a ClaimsPrincipal carrying a userId claim, mirroring how the JWT
        // middleware exposes the caller's id to authenticated actions.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim("userId", "42") }, "Test"))
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Date-bound parsing & pass-through to the repository
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJobHistory_NoDates_Returns200AndCallsRepositoryWithNullBounds()
    {
        _repo.Setup(r => r.GetJobHistoryAsync(null, null))
            .ReturnsAsync(new List<SawJob>
            {
                new() { SawJobId = 1, JobCode = "SAW-001", SpeciesName = "Jak", Status = "Completed" }
            });

        var result = await _controller.GetJobHistory();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);

        var jobs = Assert.IsAssignableFrom<IEnumerable<SawJob>>(ok.Value);
        Assert.Single(jobs);
        Assert.Equal("SAW-001", jobs.First().JobCode);

        _repo.Verify(r => r.GetJobHistoryAsync(null, null), Times.Once);
    }

    [Fact]
    public async Task GetJobHistory_WithFromAndTo_ParsesDatesUtcMidnightAndPassesToRepository()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        _repo.Setup(r => r.GetJobHistoryAsync(from, to))
            .ReturnsAsync(new List<SawJob>());

        var result = await _controller.GetJobHistory(from: "2026-09-01", to: "2026-09-30");

        Assert.IsType<OkObjectResult>(result);
        _repo.Verify(r => r.GetJobHistoryAsync(from, to), Times.Once);
    }

    [Fact]
    public async Task GetJobHistory_OnlyFrom_PassesFromAndNullTo()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _repo.Setup(r => r.GetJobHistoryAsync(from, null))
            .ReturnsAsync(new List<SawJob>());

        var result = await _controller.GetJobHistory(from: "2026-01-01");

        Assert.IsType<OkObjectResult>(result);
        _repo.Verify(r => r.GetJobHistoryAsync(from, null), Times.Once);
    }

    [Fact]
    public async Task GetJobHistory_OnlyTo_PassesNullFromAndTo()
    {
        var to = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        _repo.Setup(r => r.GetJobHistoryAsync(null, to))
            .ReturnsAsync(new List<SawJob>());

        var result = await _controller.GetJobHistory(to: "2026-12-31");

        Assert.IsType<OkObjectResult>(result);
        _repo.Verify(r => r.GetJobHistoryAsync(null, to), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Invalid date formats → 400, same message format as the Wastage & Yield
    // Report (the shared TryParseReportDate helper)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-date", null, "Invalid 'from' date. Use YYYY-MM-DD.")]
    [InlineData(null, "nope", "Invalid 'to' date. Use YYYY-MM-DD.")]
    [InlineData("2026-13-99", null, "Invalid 'from' date. Use YYYY-MM-DD.")]
    [InlineData("", "2026-9-1", "Invalid 'to' date. Use YYYY-MM-DD.")]
    public async Task GetJobHistory_InvalidDate_Returns400WithReportStyleMessage(
        string? from, string? to, string expectedMessage)
    {
        var result = await _controller.GetJobHistory(from, to);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);

        // Anonymous controller response types can't be reached via `dynamic`
        // across assemblies, so read the message property reflectively.
        var message = bad.Value?.GetType().GetProperty("message")?.GetValue(bad.Value) as string;
        Assert.Equal(expectedMessage, message);

        // The repository must not be consulted for a malformed request.
        _repo.Verify(r => r.GetJobHistoryAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Authorization contract — class-level [Authorize] only, no role gate
    // (MVC enforces [Authorize] at request time; this verifies the contract
    // that unauthenticated callers are rejected and no role is required.)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetJobHistory_RequiresAuthenticationViaClassLevelAuthorize()
    {
        var classAttr = typeof(SawmillController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(classAttr);

        var methodAttr = typeof(SawmillController)
            .GetMethod(nameof(SawmillController.GetJobHistory))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Null(methodAttr);
    }
}