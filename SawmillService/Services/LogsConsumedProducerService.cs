using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using SawmillService.Events;

namespace SawmillService.Services;

/// <summary>
/// Thin wrapper around Confluent.Kafka's IProducer for the logs-consumed topic.
/// Registered as a singleton. Publish failures throw to the caller; the
/// controller decides whether a failed publish should fail the request (for
/// starting a saw job it must not — see StartSawJob). Flushes in-flight
/// messages on Dispose so a start event isn't silently dropped on shutdown.
/// </summary>
public class LogsConsumedProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public LogsConsumedProducerService(string bootstrapServers, string topic)
        : this(bootstrapServers, topic, null)
    {
    }

    public LogsConsumedProducerService(string bootstrapServers, string topic, ILogger? logger)
    {
        _topic = topic;
        var config = KafkaProducerGuard.BuildConfig(bootstrapServers);
        _producer = new ProducerBuilder<string, string>(config)
            .AttachGuardedHandlers(logger)
            .Build();
    }

    /// <summary>
    /// Serializes the event to camelCase JSON and produces it with the SawJobId
    /// as the message key, so all events for one job land in the same partition.
    /// </summary>
    public virtual async Task PublishLogsConsumedAsync(LogsConsumedEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOpts);
        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = evt.SawJobId.ToString(),
            Value = json
        });
    }

    public void Dispose()
    {
        KafkaProducerGuard.SafeDispose(_producer);
    }
}