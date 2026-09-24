using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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

public class SawmillControllerCancelJobTests
{
    private const string DefaultReason = "No reason provided";

    private readonly Mock<ISawmillRepository> _repo;
    private readonly Mock<KafkaProducerService> _producer;
    private readonly SawmillController _controller;

    public SawmillControllerCancelJobTests()
    {
        _repo = new Mock<ISawmillRepository>();
        _producer = new Mock<KafkaProducerService>("localhost:9092", "raw-stock-reversed");

        _controller = new SawmillController(
            _repo.Object,
            new LogIntakeClient(new HttpClient(), NullLogger<LogIntakeClient>.Instance),
            _producer.Object,
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
    }

    [Fact]
    public async Task CancelJob_MissingReason_SucceedsWithDefaultedReason()
    {
        _repo.Setup(r => r.CancelJobAsync(7)).ReturnsAsync(true);
        _repo.Setup(r => r.GetLogAllocationsForJobAsync(7))
            .ReturnsAsync(new List<SawJobLogAllocation>
            {
                new() { SawJobId = 7, LogId = 11, VolumeM3 = 0.4531m },
                new() { SawJobId = 7, LogId = 12, VolumeM3 = 1.1327m }
            });

        // Null Reason (as when the frontend sends no body) must not be rejected.
        var result = await _controller.CancelJob(7, new CancelJobDto());

        Assert.IsType<OkObjectResult>(result);
        _producer.Verify(p => p.PublishRawStockReversedAsync(It.Is<RawStockReversedEvent>(e =>
            e.SawJobId == 7 &&
            e.Reason == DefaultReason &&
            e.CancelledBy == 42 &&
            e.Logs.Count == 2 &&
            e.Logs.Any(l => l.LogId == 11 && l.VolumeM3 == 0.4531m) &&
            e.Logs.Any(l => l.LogId == 12 && l.VolumeM3 == 1.1327m))), Times.Once);
    }

    [Fact]
    public async Task CancelJob_ProvidedReason_PassesTrimmedReason()
    {
        _repo.Setup(r => r.CancelJobAsync(7)).ReturnsAsync(true);
        _repo.Setup(r => r.GetLogAllocationsForJobAsync(7)).ReturnsAsync(new List<SawJobLogAllocation>());

        await _controller.CancelJob(7, new CancelJobDto { Reason = "  Blocked log  " });

        _producer.Verify(p => p.PublishRawStockReversedAsync(It.Is<RawStockReversedEvent>(e =>
            e.Reason == "Blocked log")), Times.Once);
    }

    [Fact]
    public async Task CancelJob_ProducerThrows_StillReturnsOkAndJobStaysCancelled()
    {
        _repo.Setup(r => r.CancelJobAsync(7)).ReturnsAsync(true);
        _repo.Setup(r => r.GetLogAllocationsForJobAsync(7)).ReturnsAsync(new List<SawJobLogAllocation>());
        _producer.Setup(p => p.PublishRawStockReversedAsync(It.IsAny<RawStockReversedEvent>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await _controller.CancelJob(7, new CancelJobDto { Reason = "test" });

        Assert.IsType<OkObjectResult>(result);
        _repo.Verify(r => r.CancelJobAsync(7), Times.Once);
    }

    [Fact]
    public async Task CancelJob_RepoReturnsFalse_ReturnsNotFoundAndNeverPublishes()
    {
        _repo.Setup(r => r.CancelJobAsync(404)).ReturnsAsync(false);

        var result = await _controller.CancelJob(404, new CancelJobDto { Reason = "test" });

        Assert.IsType<NotFoundObjectResult>(result);
        _producer.Verify(p => p.PublishRawStockReversedAsync(It.IsAny<RawStockReversedEvent>()), Times.Never);
    }

    [Fact]
    public void CancelJob_AllowsAdminManagerSupervisor()
    {
        var attribute = typeof(SawmillController)
            .GetMethod(nameof(SawmillController.CancelJob))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("Admin,Manager,Supervisor", attribute.Roles);
    }
}