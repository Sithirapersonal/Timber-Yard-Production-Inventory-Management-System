namespace LogIntakeService.Events;

/// <summary>
/// Mirror of SawmillService's RawStockReversedEvent, intentionally duplicated (not
/// shared) per this codebase's per-service DTO convention. Received from the Kafka
/// topic 'raw-stock-reversed' when a saw job is cancelled; the consumer flips the
/// listed LogIds back to 'InStock'. Deserialized as camelCase JSON.
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

    /// <summary>A single log that was allocated to the cancelled job.</summary>
    public class LogEntry
    {
        public int LogId { get; set; }
        public decimal VolumeM3 { get; set; }
    }
}