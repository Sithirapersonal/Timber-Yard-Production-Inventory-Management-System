namespace LogIntakeService.Models;

public class RawStock
{
    public int StockId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public decimal CurrentVolumeM3 { get; set; }
    public decimal LowStockThreshold { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsLowStock => CurrentVolumeM3 <= LowStockThreshold;
}