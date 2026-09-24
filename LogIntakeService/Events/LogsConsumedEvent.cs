namespace LogIntakeService.Events;

/// <summary>
/// Mirror of SawmillService's LogsConsumedEvent. Received from the Kafka topic
/// 'logs-consumed' when a saw job is started; the consumer flips the listed
/// LogIds from InStock to Consumed. Deliberately duplicated (not shared) with
/// SawmillService's copy of this event, matching this codebase's per-service
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