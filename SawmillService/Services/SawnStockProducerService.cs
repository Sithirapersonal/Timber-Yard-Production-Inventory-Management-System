using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using SawmillService.Events;

namespace SawmillService.Services;

/// <summary>
/// Producer for the stock-updates topic. Publishes sawn-stock-credited events
/// when a saw job completes, allowing TreatmentService to autonomously track available
/// sawn stock. Registered as a singleton.
/// </summary>
public class SawnStockProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public SawnStockProducerService(string bootstrapServers, string topic)
        : this(bootstrapServers, topic, null)
    {
    }

    public SawnStockProducerService(string bootstrapServers, string topic, ILogger? logger)
    {
        _topic = topic;
        var config = KafkaProducerGuard.BuildConfig(bootstrapServers);
        _producer = new ProducerBuilder<string, string>(config)
            .AttachGuardedHandlers(logger)
            .Build();
    }

    /// <summary>
    /// Serializes the event to camelCase JSON and produces it with the JobId
    /// as message key.
    /// </summary>
    public virtual async Task PublishSawnStockCreditedAsync(SawnStockCreditedEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOpts);
        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = evt.JobId.ToString(),
            Value = json
        });
    }

    public void Dispose()
    {
        KafkaProducerGuard.SafeDispose(_producer);
    }
}
