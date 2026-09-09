namespace LogIntakeService.Models;

public class StockAdjustment
{
    public int AdjustmentId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public decimal AdjustedVolumeM3 { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int AdjustedBy { get; set; }
    public DateTime AdjustedAt { get; set; }
}