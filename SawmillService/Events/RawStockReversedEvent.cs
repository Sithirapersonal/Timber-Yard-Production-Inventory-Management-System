namespace SawmillService.Events;

/// <summary>
/// Event published to Kafka (topic: raw-stock-reversed) when a saw job is cancelled.
/// Lists every log that was allocated to the job so LogIntakeService can flip those
/// LogIds back to 'InStock'. Serialized to camelCase JSON by KafkaProducerService.
/// Deliberately duplicated (not shared) with LogIntakeService's copy of this event,
/// matching the codebase's per-service DTO convention.
/// </summary>
public class RawStockReversedEvent
{
    public int SawJobId { get; set; }

    public List<LogEntry> Logs { get; set; } = new();

    public string Reason { get; set; } = string.Empty;

    /// <summary>User id from the cancelling caller's JWT principal.</summary>
    public int CancelledBy { get; set; }

    /// <summary>UTC timestamp of the cancellation.</summary>
    public DateTime CancelledAt { get; set; }

    /// <summary>A single log allocated to the cancelled job, with the volume recorded
    /// at allocation time so it stays stable even if LogIntakeService changes.</summary>
    public class LogEntry
    {
        public int LogId { get; set; }
        public decimal VolumeM3 { get; set; }
    }
}