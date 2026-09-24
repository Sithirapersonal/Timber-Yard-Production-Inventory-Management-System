using LogIntakeService.Events;
using LogIntakeService.Repositories;
using LogIntakeService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LogIntakeService.Tests;

/// <summary>
/// Unit tests for the logs-consumed consumer: payload parsing (including the
/// malformed-payload log-and-skip path) and the MarkLogsConsumedAsync processing
/// path, including the tolerant partial-state behavior on redelivery.
/// </summary>
public class LogsConsumedConsumerTests
{
    /// <summary>
    /// Builds a consumer backed by the given mocked repository, exactly as DI would
    /// wire it at runtime (repository resolved scoped from an IServiceScopeFactory).
    /// </summary>
    private static LogsConsumedConsumer CreateConsumer(
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
        return new LogsConsumedConsumer(config, scopeFactory, NullLogger<LogsConsumedConsumer>.Instance);
    }

    private const string ValidPayload = """
        {
          "sawJobId": 9,
          "logs": [
            { "logId": 21, "volumeM3": 0.6721 },
            { "logId": 22, "volumeM3": 1.1327 }
          ],
          "startedBy": 42,
          "startedAt": "2026-09-24T07:30:00Z"
        }
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // TryParsePayload — including the malformed-payload log-and-skip path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParsePayload_ValidJson_ReturnsEventWithAllFields()
    {
        var ok = LogsConsumedConsumer.TryParsePayload(ValidPayload, out var evt);

        Assert.True(ok);
        Assert.NotNull(evt);
        Assert.Equal(9, evt!.SawJobId);
        Assert.Equal(42, evt.StartedBy);
        Assert.Equal(2, evt.Logs.Count);
        Assert.Equal(21, evt.Logs[0].LogId);
        Assert.Equal(0.6721m, evt.Logs[0].VolumeM3);
        Assert.Equal(22, evt.Logs[1].LogId);
        Assert.Equal(1.1327m, evt.Logs[1].VolumeM3);
    }

    [Theory]
    [InlineData("not json at all {{{")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParsePayload_MalformedPayload_ReturnsFalseWithoutThrowing(string raw)
    {
        var ok = LogsConsumedConsumer.TryParsePayload(raw, out var evt);

        Assert.False(ok);
        Assert.Null(evt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ProcessPayloadAsync — happy path, partial-state tolerance
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessPayload_ValidPayload_CallsMarkLogsConsumedAsyncWithAllIds()
    {
        var repo = new Mock<ILogIntakeRepository>();
        repo.Setup(r => r.MarkLogsConsumedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(2);
        var consumer = CreateConsumer(repo, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        repo.Verify(r => r.MarkLogsConsumedAsync(It.Is<IEnumerable<int>>(ids =>
            ids.SequenceEqual(new[] { 21, 22 }))), Times.Once);
    }

    [Fact]
    public async Task ProcessPayload_RowsAffectedMismatch_DoesNotThrow()
    {
        // Represents a redelivery where logs are already Consumed: repository affects
        // 0 rows. The consumer must log (not throw) when affected != requested.
        var repo = new Mock<ILogIntakeRepository>();
        repo.Setup(r => r.MarkLogsConsumedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(0);
        var consumer = CreateConsumer(repo, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        // No exception was thrown, and the call did go through.
        repo.Verify(r => r.MarkLogsConsumedAsync(It.IsAny<IEnumerable<int>>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotency at the consumer level — redelivered event is harmless
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessPayload_SamePayloadTwice_IsHarmlessSecondTime()
    {
        var repo = new Mock<ILogIntakeRepository>();
        repo.SetupSequence(r => r.MarkLogsConsumedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(2)  // first delivery: 2 logs flipped
            .ReturnsAsync(0); // redelivery: already Consumed → 0 rows, no error
        var consumer = CreateConsumer(repo, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);
        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        repo.Verify(r => r.MarkLogsConsumedAsync(It.IsAny<IEnumerable<int>>()), Times.Exactly(2));
    }
}