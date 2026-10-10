using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Confluent.Kafka;
using TreatmentService.Events;
using TreatmentService.Repositories;

namespace TreatmentService.Services;

/// <summary>
/// Background Kafka consumer for the 'stock-updates' topic. Consumes 'sawn-stock-credited'
/// events published by SawmillService when a saw job completes, and credits the finished
/// sawn stock to TreatmentDB so stock is available for treatment batch operations.
///
/// Acceptance criteria satisfied:
/// - Subscribed to 'stock-updates' with its own consumer group 'treatment-stock-updates'.
/// - Idempotent: If the same event (same eventId) is delivered twice, stock is credited only once.
/// - Resilient startup: Retries with exponential backoff if Kafka is unreachable, without crashing.
/// - Fault tolerant: Malformed payloads or other event types are logged and skipped without stopping the consumer.
/// </summary>
public class SawnStockCreditedConsumer : BackgroundService
{
    private readonly string _bootstrapServers;
    private readonly string _topic;
    private readonly string _groupId;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SawnStockCreditedConsumer> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public const string ExpectedEventType = "sawn-stock-credited";

    public SawnStockCreditedConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<SawnStockCreditedConsumer> logger)
    {
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _topic = configuration["Kafka:StockUpdatesTopic"] ?? configuration["Kafka:Topic"] ?? "stock-updates";
        _groupId = configuration["Kafka:GroupId"] ?? "treatment-stock-updates";
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry-with-backoff: if connecting/subscribing fails (e.g. broker not up yet at startup),
        // keep retrying instead of crashing the host. The rest of TreatmentService HTTP API
        // continues serving normally while this loop is in backoff.
        var delaySeconds = 1;
        const int maxDelaySeconds = 30;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumeLoopAsync(stoppingToken);
                delaySeconds = 1; // reset on clean loop return
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful host shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kafka consumer for topic {Topic} encountered an error; retrying in {DelaySeconds}s", _topic, delaySeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                delaySeconds = Math.Min(delaySeconds * 2, maxDelaySeconds);
            }
        }
    }

    private async Task RunConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = _groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_topic);
        _logger.LogInformation("Subscribed to Kafka topic {Topic} with group {GroupId}", _topic, _groupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (ConsumeException cex)
            {
                _logger.LogWarning(cex, "Kafka ConsumeException encountered on topic {Topic}", _topic);
                continue;
            }

            if (result?.Message?.Value == null) continue;

            try
            {
                await ProcessPayloadAsync(result.Message.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad message or database glitch must never kill the consumer loop
                _logger.LogError(ex, "Failed to process message on topic {Topic} (Key: {Key})", _topic, result.Message.Key);
            }
        }
    }

    /// <summary>
    /// Parses and handles a single Kafka payload. Exposed publicly so it can be tested
    /// directly without needing a live Kafka broker.
    /// </summary>
    public async Task ProcessPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        if (!TryParsePayload(payload, out var evt, out var skipReason))
        {
            _logger.LogWarning("Skipping payload on topic {Topic}. Reason: {Reason}. Raw payload: {Payload}", _topic, skipReason, payload);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITreatmentStockRepository>();

        var credited = await repository.CreditSawnStockAsync(evt);

        if (credited)
        {
            _logger.LogInformation(
                "Successfully credited sawn stock: Species={Species}, Dimensions={Dimensions}, VolumeM3={VolumeM3} (JobId: {JobId}, EventId: {EventId})",
                evt.Species, evt.Dimensions, evt.VolumeM3, evt.JobId, evt.EventId);
        }
        else
        {
            // Already processed: duplicate message ignored
            _logger.LogInformation(
                "Duplicate event ignored: EventId={EventId} for JobId={JobId} has already been applied",
                evt.EventId, evt.JobId);
        }
    }

    /// <summary>
    /// Deserializes and validates a raw payload into a <see cref="SawnStockCreditedEvent"/>.
    /// Returns false without throwing for malformed JSON, other event types, or invalid data.
    /// </summary>
    public static bool TryParsePayload(
        string payload,
        [NotNullWhen(true)] out SawnStockCreditedEvent? evt,
        out string? skipReason)
    {
        evt = null;
        skipReason = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            skipReason = "Payload is empty or whitespace";
            return false;
        }

        try
        {
            evt = JsonSerializer.Deserialize<SawnStockCreditedEvent>(payload, _jsonOpts);
        }
        catch (JsonException ex)
        {
            skipReason = $"Malformed JSON: {ex.Message}";
            return false;
        }

        if (evt == null)
        {
            skipReason = "Deserialized event is null";
            return false;
        }

        // Validate eventType
        if (!string.Equals(evt.EventType, ExpectedEventType, StringComparison.OrdinalIgnoreCase))
        {
            skipReason = $"Unrecognized or mismatched eventType '{evt.EventType}' (expected '{ExpectedEventType}')";
            evt = null;
            return false;
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(evt.EventId))
        {
            skipReason = "Missing or empty eventId";
            evt = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(evt.Species))
        {
            skipReason = "Missing or empty species";
            evt = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(evt.Dimensions))
        {
            skipReason = "Missing or empty dimensions";
            evt = null;
            return false;
        }

        if (evt.VolumeM3 <= 0)
        {
            skipReason = $"VolumeM3 must be greater than zero, received {evt.VolumeM3}";
            evt = null;
            return false;
        }

        return true;
    }
}
