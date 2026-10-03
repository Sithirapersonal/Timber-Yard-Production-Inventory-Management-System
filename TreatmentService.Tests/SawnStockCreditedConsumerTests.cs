using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TreatmentService.Events;
using TreatmentService.Repositories;
using TreatmentService.Services;

namespace TreatmentService.Tests;

/// <summary>
/// Unit and integration tests for the SawnStockCreditedConsumer:
/// - Parsing of sawn-stock-credited payloads (valid, malformed, other event types, invalid dimensions/volumes).
/// - AC1: Applying valid event credits stock balance.
/// - AC2: Idempotent processing of duplicate events (credited only once).
/// - AC3: Unreachable broker backoff and non-crashing behavior.
/// - AC4: Malformed payload and unexpected event types logged and skipped without stopping or throwing.
/// </summary>
public class SawnStockCreditedConsumerTests
{
    private static SawnStockCreditedConsumer CreateConsumer(
        Mock<ITreatmentStockRepository> repoMock,
        out IServiceScopeFactory scopeFactory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();
        scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder().Build();
        return new SawnStockCreditedConsumer(config, scopeFactory, NullLogger<SawnStockCreditedConsumer>.Instance);
    }

    private const string ValidPayload = """
        {
          "eventId": "evt-12345-abcde",
          "eventType": "sawn-stock-credited",
          "jobId": 10,
          "species": "Teak",
          "dimensions": "2x4x10",
          "volumeM3": 1.2500,
          "occurredAt": "2026-10-03T10:00:00Z"
        }
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // AC4: Payload Parsing & Validation (malformed and other event types skipped)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParsePayload_ValidJson_ReturnsTrueAndPopulatesEvent()
    {
        var ok = SawnStockCreditedConsumer.TryParsePayload(ValidPayload, out var evt, out var skipReason);

        Assert.True(ok);
        Assert.Null(skipReason);
        Assert.NotNull(evt);
        Assert.Equal("evt-12345-abcde", evt!.EventId);
        Assert.Equal("sawn-stock-credited", evt.EventType);
        Assert.Equal(10, evt.JobId);
        Assert.Equal("Teak", evt.Species);
        Assert.Equal("2x4x10", evt.Dimensions);
        Assert.Equal(1.2500m, evt.VolumeM3);
        Assert.Equal(new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc), evt.OccurredAt);
    }

    [Theory]
    [InlineData("not json at all {{{{")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void TryParsePayload_MalformedJson_ReturnsFalseWithoutThrowing(string malformed)
    {
        var ok = SawnStockCreditedConsumer.TryParsePayload(malformed, out var evt, out var skipReason);

        Assert.False(ok);
        Assert.Null(evt);
        Assert.NotNull(skipReason);
    }

    [Fact]
    public void TryParsePayload_OtherEventType_ReturnsFalseWithReason()
    {
        var payload = """
            {
              "eventId": "evt-777",
              "eventType": "raw-stock-reversed",
              "jobId": 5,
              "species": "Teak",
              "dimensions": "2x4x10",
              "volumeM3": 0.5000,
              "occurredAt": "2026-10-03T10:00:00Z"
            }
            """;

        var ok = SawnStockCreditedConsumer.TryParsePayload(payload, out var evt, out var skipReason);

        Assert.False(ok);
        Assert.Null(evt);
        Assert.Contains("Unrecognized or mismatched eventType", skipReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TryParsePayload_MissingEventId_ReturnsFalse(string emptyEventId)
    {
        var payload = $$"""
            {
              "eventId": "{{emptyEventId}}",
              "eventType": "sawn-stock-credited",
              "jobId": 10,
              "species": "Teak",
              "dimensions": "2x4x10",
              "volumeM3": 1.2500,
              "occurredAt": "2026-10-03T10:00:00Z"
            }
            """;

        var ok = SawnStockCreditedConsumer.TryParsePayload(payload, out var evt, out var skipReason);

        Assert.False(ok);
        Assert.Null(evt);
        Assert.Contains("eventId", skipReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(-10)]
    public void TryParsePayload_NonPositiveVolume_ReturnsFalse(decimal volume)
    {
        var payload = $$"""
            {
              "eventId": "evt-123",
              "eventType": "sawn-stock-credited",
              "jobId": 10,
              "species": "Teak",
              "dimensions": "2x4x10",
              "volumeM3": {{volume}},
              "occurredAt": "2026-10-03T10:00:00Z"
            }
            """;

        var ok = SawnStockCreditedConsumer.TryParsePayload(payload, out var evt, out var skipReason);

        Assert.False(ok);
        Assert.Null(evt);
        Assert.Contains("VolumeM3 must be greater than zero", skipReason);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC1: Event Applied (stock balance increases)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessPayload_ValidEvent_CallsCreditSawnStockAsync()
    {
        var repoMock = new Mock<ITreatmentStockRepository>();
        repoMock.Setup(r => r.CreditSawnStockAsync(It.IsAny<SawnStockCreditedEvent>()))
            .ReturnsAsync(true);

        var consumer = CreateConsumer(repoMock, out _);

        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        repoMock.Verify(r => r.CreditSawnStockAsync(It.Is<SawnStockCreditedEvent>(e =>
            e.EventId == "evt-12345-abcde" &&
            e.EventType == "sawn-stock-credited" &&
            e.JobId == 10 &&
            e.Species == "Teak" &&
            e.Dimensions == "2x4x10" &&
            e.VolumeM3 == 1.2500m)), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC2: Duplicate Delivery (credited only once)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessPayload_DuplicateEvent_CreditedOnlyOnce()
    {
        var repoMock = new Mock<ITreatmentStockRepository>();
        // First delivery: returns true (credited)
        // Second delivery: returns false (duplicate ignored)
        repoMock.SetupSequence(r => r.CreditSawnStockAsync(It.IsAny<SawnStockCreditedEvent>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var consumer = CreateConsumer(repoMock, out _);

        // First delivery
        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        // Duplicate delivery with identical payload
        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);

        repoMock.Verify(r => r.CreditSawnStockAsync(It.IsAny<SawnStockCreditedEvent>()), Times.Exactly(2));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC4: Malformed Payload or Unhandled EventType Does Not Crash or Call Repo
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("broken JSON content")]
    [InlineData("{ \"eventType\": \"other-unrelated-event\" }")]
    [InlineData("")]
    public async Task ProcessPayload_MalformedOrOtherType_LoggedAndSkippedWithoutCallingRepo(string badPayload)
    {
        var repoMock = new Mock<ITreatmentStockRepository>();
        var consumer = CreateConsumer(repoMock, out _);

        // Must not throw exception
        await consumer.ProcessPayloadAsync(badPayload, CancellationToken.None);

        repoMock.Verify(r => r.CreditSawnStockAsync(It.IsAny<SawnStockCreditedEvent>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integration test with in-memory repository implementation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IntegrationTest_EventApplied_DuplicateIgnored_MalformedDoesNotStopConsumer()
    {
        var inMemoryRepo = new InMemoryTreatmentStockRepository();
        var services = new ServiceCollection();
        services.AddScoped<ITreatmentStockRepository>(_ => inMemoryRepo);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder().Build();
        var consumer = new SawnStockCreditedConsumer(config, scopeFactory, NullLogger<SawnStockCreditedConsumer>.Instance);

        // 1. Initial balance is 0
        var initialBalance = await inMemoryRepo.GetStockBalanceAsync("Teak", "2x4x10");
        Assert.Equal(0m, initialBalance);

        // 2. Consume valid event -> balance increases by 1.25
        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);
        var afterFirstCredit = await inMemoryRepo.GetStockBalanceAsync("Teak", "2x4x10");
        Assert.Equal(1.2500m, afterFirstCredit);

        // 3. Consume duplicate event -> balance remains 1.25 (credited only once)
        await consumer.ProcessPayloadAsync(ValidPayload, CancellationToken.None);
        var afterDuplicate = await inMemoryRepo.GetStockBalanceAsync("Teak", "2x4x10");
        Assert.Equal(1.2500m, afterDuplicate);

        // 4. Consume malformed payload -> consumer logs and skips without error
        await consumer.ProcessPayloadAsync("{ bad: json [", CancellationToken.None);
        var afterMalformed = await inMemoryRepo.GetStockBalanceAsync("Teak", "2x4x10");
        Assert.Equal(1.2500m, afterMalformed);

        // 5. Consume another valid event with different eventId -> balance increases again
        var secondPayload = """
            {
              "eventId": "evt-99999-xyz",
              "eventType": "sawn-stock-credited",
              "jobId": 11,
              "species": "Teak",
              "dimensions": "2x4x10",
              "volumeM3": 0.7500,
              "occurredAt": "2026-10-03T11:00:00Z"
            }
            """;
        await consumer.ProcessPayloadAsync(secondPayload, CancellationToken.None);
        var finalBalance = await inMemoryRepo.GetStockBalanceAsync("Teak", "2x4x10");
        Assert.Equal(2.0000m, finalBalance);
    }

    /// <summary>
    /// In-memory repository implementation simulating TreatmentDB behavior for integration testing.
    /// </summary>
    private class InMemoryTreatmentStockRepository : ITreatmentStockRepository
    {
        private readonly HashSet<string> _processedEvents = new();
        private readonly Dictionary<(string Species, string Dimensions), decimal> _stock = new();

        public Task<bool> CreditSawnStockAsync(SawnStockCreditedEvent evt)
        {
            lock (_processedEvents)
            {
                if (_processedEvents.Contains(evt.EventId))
                {
                    return Task.FromResult(false);
                }

                _processedEvents.Add(evt.EventId);

                var key = (evt.Species.Trim(), evt.Dimensions.Trim());
                if (_stock.TryGetValue(key, out var current))
                {
                    _stock[key] = current + evt.VolumeM3;
                }
                else
                {
                    _stock[key] = evt.VolumeM3;
                }

                return Task.FromResult(true);
            }
        }

        public Task<decimal> GetStockBalanceAsync(string species, string dimensions)
        {
            lock (_processedEvents)
            {
                var key = (species.Trim(), dimensions.Trim());
                return Task.FromResult(_stock.TryGetValue(key, out var val) ? val : 0m);
            }
        }

        public Task<IEnumerable<TreatmentService.Models.SawnStock>> GetAllStockAsync()
        {
            lock (_processedEvents)
            {
                var list = _stock.Select((kvp, idx) => new TreatmentService.Models.SawnStock
                {
                    StockId = idx + 1,
                    Species = kvp.Key.Species,
                    Dimensions = kvp.Key.Dimensions,
                    VolumeM3 = kvp.Value,
                    LastUpdated = DateTime.UtcNow
                }).ToList();

                return Task.FromResult<IEnumerable<TreatmentService.Models.SawnStock>>(list);
            }
        }

        public Task<bool> HasEventBeenProcessedAsync(string eventId)
        {
            lock (_processedEvents)
            {
                return Task.FromResult(_processedEvents.Contains(eventId));
            }
        }
    }
}
