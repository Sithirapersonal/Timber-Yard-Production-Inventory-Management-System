namespace SawmillService.Events;

/// <summary>
/// Event published to Kafka (topic: logs-consumed) when a saw job is started.
/// Lists every log allocated to the job so LogIntakeService can flip those
/// LogIds from 'InStock' to 'Consumed'. Serialized to camelCase JSON by
/// LogsConsumedProducerService. Deliberately duplicated (not shared) with
/// LogIntakeService's copy of this event, matching this codebase's per-service
/// DTO convention (see RawStockReversedEvent).
/// </summary>
public class LogsConsumedEvent
{
    public int SawJobId { get; set; }

    public List<LogEntry> Logs { get; set; } = new();

    /// <summary>User id from the starting caller's JWT principal.</summary>
    public int StartedBy { get; set; }

    /// <summary>UTC timestamp the job was started.</summary>
    public DateTime StartedAt { get; set; }

    public class LogEntry
    {
        public int LogId { get; set; }
        public decimal VolumeM3 { get; set; }
    }
}