namespace LogIntakeService.DTOs;

public class StockSummaryDto
{
    public int StockId { get; set; }
    public int SpeciesId { get; set; }
    public string Species { get; set; } = string.Empty;
    public int LengthId { get; set; }
    public decimal LengthFt { get; set; }
    public int LogCount { get; set; }
    public decimal TotalVolumeM3 { get; set; }
    public decimal LowStockThreshold { get; set; } = 10.00m;
    public bool IsLowStock => TotalVolumeM3 <= LowStockThreshold;
}
