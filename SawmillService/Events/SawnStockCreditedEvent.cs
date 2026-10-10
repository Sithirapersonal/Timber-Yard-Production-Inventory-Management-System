namespace SawmillService.Events;

/// <summary>
/// Event published to Kafka (topic: stock-updates) when a saw job is completed.
/// Notifies TreatmentService of newly finished sawn timber ready for chemical treatment.
/// Serialized to camelCase JSON. Deliberately per-service DTO matching this codebase's
/// microservice isolation conventions.
/// </summary>
public class SawnStockCreditedEvent
{
    /// <summary>Unique UUID/GUID for idempotent processing by TreatmentService.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Discriminator string: "sawn-stock-credited".</summary>
    public string EventType { get; set; } = "sawn-stock-credited";

    /// <summary>Id of the completed saw job.</summary>
    public int JobId { get; set; }

    /// <summary>Timber species name, e.g. Teak, Mahogany.</summary>
    public string Species { get; set; } = string.Empty;

    /// <summary>Board dimensions specification, e.g. 2x4x10.</summary>
    public string Dimensions { get; set; } = string.Empty;

    /// <summary>Sawn timber volume in cubic meters credited to inventory.</summary>
    public decimal VolumeM3 { get; set; }

    /// <summary>UTC timestamp when the job completion occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
