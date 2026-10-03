namespace TreatmentService.Models;

/// <summary>
/// Sawn timber stock balance maintained independently inside TreatmentDB.
/// Credited via Kafka sawn-stock-credited events produced by SawmillService.
/// </summary>
public class SawnStock
{
    public int StockId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public decimal VolumeM3 { get; set; }
    public DateTime LastUpdated { get; set; }
}
