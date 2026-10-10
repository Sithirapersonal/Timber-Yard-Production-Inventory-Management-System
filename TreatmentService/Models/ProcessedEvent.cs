namespace TreatmentService.Models;

/// <summary>
/// Record of a successfully processed Kafka event for idempotency and deduplication.
/// </summary>
public class ProcessedEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int? JobId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
