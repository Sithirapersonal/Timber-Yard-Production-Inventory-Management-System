using LogIntakeService.Events;
using LogIntakeService.Repositories;
using LogIntakeService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LogIntakeService.Tests;

public class RawStockReversedConsumerTests
{
    /// <summary>
    /// Builds a consumer backed by the given mocked repository, exactly as DI would
    /// wire it at runtime (repository resolved scoped from an IServiceScopeFactory).
    /// </summary>
    private static RawStockReversedConsumer CreateConsumer(
        Mock<ILogIntakeRepository> repo,
        out IServiceScopeFactory scopeFactory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repo.Object);
        var provider = services.BuildServiceProvider();
        scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        // No Kafka config keys → the consumer falls back to its defaults. The parsing
        // and processing paths under test never touch a real broker.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        return new RawStockReversedConsumer(config, scopeFactory, NullLogger<RawStockReversedConsumer>.Instance);
    }

    private const string ValidPayload = """
        {
          "sawJobId": 7,
          "logs": [
            { "logId": 11, "volumeM3": 0.4531 },
            { "logId": 12, "volumeM3": 1.1327 }
          ],
          "reason": "No reason provided",
          "cancelledBy": 42,
          "cancelledAt": "2026-09-23T11:22:05Z"
        }
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // TryParsePayload — including the malformed-payload log-and-skip path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParsePayload_ValidJson_ReturnsEventWithAllFields()
    {
        var ok = RawStockReversedConsumer.TryParsePayload(ValidPayload, out var evt);

        Assert.True(ok);
        Assert.NotNull(evt);
        Assert.Equal(7, evt!.SawJobId);
        Assert.Equal("No reason provided", evt.Reason);
        Assert.Equal(42, evt.CancelledBy);
        Assert.Equal(2, evt.Logs.Count);
        Assert.Equal(11, evt.Logs[0].LogId);
        Assert.Equal(0.4531m, evt.Logs[0].VolumeM3);
        Assert.Equal(12, evt.Logs[1].LogId);
        Assert.Equal(1.1327m, evt.Logs[1].VolumeM3);
    }

    [Theory]
    [InlineData("not json at all {{{")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParsePayload_MalformedPayload_ReturnsFalseWithoutThrowing(string raw)
    {
        var ok = RawStockReversedConsumer.TryParsePayload(raw, out var evt);

        Assert.False(ok);
        Assert.Null(evt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ProcessPayloadAsync — happy path, partial-state tolerance
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessPayload_ValidPayload_CallsReleaseLogsAsyncWithAllIds()
    {
        var repo = new Mock<ILogIntakeRepository>();
        repo.Setup(r => r.ReleaseLogsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(2);
        var consumer = CreateConsumer(repo, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        repo.Verify(r => r.ReleaseLogsAsync(It.Is<IEnumerable<int>>(ids =>
            ids.SequenceEqual(new[] { 11, 12 }))), Times.Once);
    }

    [Fact]
    public async Task ProcessPayload_RowsAffectedMismatch_DoesNotThrow()
    {
        // Represents redelivery where logs are already InStock: repository affects 0
        // rows. The consumer must log (not throw) when affected != requested.
        var repo = new Mock<ILogIntakeRepository>();
        repo.Setup(r => r.ReleaseLogsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(0);
        var consumer = CreateConsumer(repo, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        // No exception was thrown, and the call did go through.
        repo.Verify(r => r.ReleaseLogsAsync(It.IsAny<IEnumerable<int>>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotency at the consumer level — redelivered event is harmless
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessPayload_SamePayloadTwice_IsHarmlessSecondTime()
    {
        var repo = new Mock<ILogIntakeRepository>();
        repo.SetupSequence(r => r.ReleaseLogsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(2)  // first delivery: 2 logs flipped
            .ReturnsAsync(0); // redelivery: already InStock → 0 rows, no error
        var consumer = CreateConsumer(repo, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);
        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        repo.Verify(r => r.ReleaseLogsAsync(It.IsAny<IEnumerable<int>>()), Times.Exactly(2));
    }
}