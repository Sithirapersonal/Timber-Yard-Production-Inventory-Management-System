namespace TreatmentService.Events;

/// <summary>
/// Event consumed from Kafka (topic: stock-updates) when SawmillService completes a saw job.
/// Credited to TreatmentDB sawn stock so available treatment inventory updates automatically.
/// Contract agreed with SawmillService.
/// </summary>
public class SawnStockCreditedEvent
{
    /// <summary>Unique UUID/GUID for idempotent processing and deduplication.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Event discriminator string: "sawn-stock-credited".</summary>
    public string EventType { get; set; } = "sawn-stock-credited";

    /// <summary>Identifier of the completed saw job in SawmillService.</summary>
    public int JobId { get; set; }

    /// <summary>Timber species name, e.g. Teak, Mahogany, Pine.</summary>
    public string Species { get; set; } = string.Empty;

    /// <summary>Dimensions specification, e.g. 2x4x10.</summary>
    public string Dimensions { get; set; } = string.Empty;

    /// <summary>Volume in cubic meters credited to sawn stock balance.</summary>
    public decimal VolumeM3 { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTime OccurredAt { get; set; }
}
