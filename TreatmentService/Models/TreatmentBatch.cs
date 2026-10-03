namespace TreatmentService.Models;

/// <summary>
/// A chemical treatment batch allocated from sawn stock.
/// </summary>
public class TreatmentBatch
{
    public int BatchId { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public string ChemicalType { get; set; } = string.Empty;
    public decimal QuantityM3 { get; set; }
    public string? Tank { get; set; }
    public string? CancellationReason { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Audit row for a sawn-stock in/out movement (credit or treatment-batch deduction).
/// </summary>
public class StockMovement
{
    public int MovementId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public decimal VolumeM3 { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string? BatchCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
