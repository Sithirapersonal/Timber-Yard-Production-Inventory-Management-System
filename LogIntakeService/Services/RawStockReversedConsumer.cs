using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Confluent.Kafka;
using LogIntakeService.Events;
using LogIntakeService.Repositories;

namespace LogIntakeService.Services;

/// <summary>
/// Background Kafka consumer for the 'raw-stock-reversed' topic. Each event lists logs
/// that a cancelled saw job had consumed; this service flips those LogIds (currently
/// Status = 'Consumed') back to 'InStock'. The flow is fire-and-forget: SawmillService
/// does not wait for us, and this consumer must never take the rest of the service down —
/// malformed messages are logged and skipped, and a broker unreachable at startup is
/// retried with exponential backoff instead of crashing the host.
/// </summary>
public class RawStockReversedConsumer : BackgroundService
{
    private readonly string _bootstrapServers;
    private readonly string _topic;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RawStockReversedConsumer> _logger;

    // JSON options matching the producer side's web defaults (camelCase), which are also
    // case-insensitive so hand-written payloads with slight casing differences still parse.
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public RawStockReversedConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<RawStockReversedConsumer> logger)
    {
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _topic = configuration["Kafka:Topic"] ?? "raw-stock-reversed";
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry-with-backoff: if connecting/subscribing fails (e.g. broker not up yet),
        // keep retrying instead of crashing the host. The rest of LogIntakeService's HTTP
        // API keeps serving normally while this loop is in backoff.
        var delaySeconds = 1;
        const int maxDelaySeconds = 30;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumeLoopAsync(stoppingToken);
                delaySeconds = 1; // only reached on clean shutdown of the inner loop
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kafka consumer for topic {Topic} failed; retrying in {DelaySeconds}s", _topic, delaySeconds);
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
            GroupId = "logintake-raw-stock-reversed",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            if (result is null || result.Message is null) continue;

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
                // One bad message must never kill the consumer loop.
                _logger.LogError(ex, "Failed to process raw-stock-reversed message {Key}", result.Message.Key);
            }
        }
    }

    /// <summary>
    /// Parses and handles a single payload. Exposed publicly (without the Kafka plumbing)
    /// so the malformed-payload and processing paths are unit-testable. Malformed payloads
    /// are logged and skipped — never thrown.
    /// </summary>
    public async Task ProcessPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        if (!TryParsePayload(payload, out var evt))
        {
            _logger.LogWarning("Skipping malformed raw-stock-reversed payload: {Payload}", payload);
            return;
        }

        // Repository is Scoped in DI but this BackgroundService is a singleton — resolve a
        // fresh scope per message, per standard ASP.NET Core guidance.
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILogIntakeRepository>();

        var requestedIds = evt.Logs.Select(l => l.LogId).ToList();
        var rowsAffected = await repository.ReleaseLogsAsync(requestedIds);

        _logger.LogInformation(
            "Processed raw-stock-reversed event for job {SawJobId}: flipped {Flipped}/{Requested} log(s) to InStock (reason: {Reason}, cancelledBy: {CancelledBy})",
            evt.SawJobId, rowsAffected, requestedIds.Count, evt.Reason, evt.CancelledBy);

        if (rowsAffected != requestedIds.Count)
        {
            // Useful signal, not an error: a log may already be InStock (re-delivered
            // event) or be in some other state (removed). Log, don't throw.
            _logger.LogWarning(
                "Raw stock reversal for job {SawJobId}: {RowsAffected}/{Requested} logs updated (already InStock, removed, or duplicate delivery)",
                evt.SawJobId, rowsAffected, requestedIds.Count);
        }
    }

    /// <summary>
    /// Deserializes a raw payload into a <see cref="RawStockReversedEvent"/>.
    /// Returns false (without throwing) for malformed JSON so the caller can log-and-skip.
    /// </summary>
    public static bool TryParsePayload(string payload, [NotNullWhen(true)] out RawStockReversedEvent? evt)
    {
        evt = null;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            evt = JsonSerializer.Deserialize<RawStockReversedEvent>(payload, _jsonOpts);
            return evt is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}