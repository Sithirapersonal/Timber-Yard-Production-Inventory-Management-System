using System.Text.Json;
using Confluent.Kafka;
using SawmillService.Events;

namespace SawmillService.Services;

/// <summary>
/// Thin wrapper around Confluent.Kafka's <see cref="IProducer{TKey,TValue}"/> for the
/// raw-stock-reversed topic. Registered as a singleton — producers are safe and intended
/// to be reused across the app lifetime (do not create one per request). Publish failures
/// throw to the caller; the controller decides whether a failed publish should fail the
/// request (for job cancellation it must not). Flushes in-flight messages on Dispose so a
/// cancellation event isn't silently dropped when the app shuts down.
/// </summary>
public class KafkaProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    // JSON options matching ASP.NET Core's web defaults (camelCase property names),
    // consistent with the JSON conventions used across this codebase.
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public KafkaProducerService(string bootstrapServers, string topic)
    {
        _topic = topic;
        var config = new ProducerConfig { BootstrapServers = bootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// Serializes the event to camelCase JSON and produces it with the SawJobId as the
    /// message key (so all events for one job land in the same partition, which is the
    /// correct behavior against a multi-partition topic). No custom retry logic — this
    /// wrapper stays thin and relies on the producer library's own defaults.
    /// </summary>
    public virtual async Task PublishRawStockReversedAsync(RawStockReversedEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOpts);
        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = evt.SawJobId.ToString(),
            Value = json
        });
    }

    /// <summary>
    /// Flushes in-flight messages on shutdown so a cancelled job's reversal event isn't
    /// silently dropped when the app stops.
    /// </summary>
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}