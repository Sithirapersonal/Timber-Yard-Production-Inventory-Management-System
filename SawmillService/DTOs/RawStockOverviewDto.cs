namespace SawmillService.DTOs;

/// <summary>
/// Projection of a LogIntakeService stock batch as seen by SawmillService.
/// Used for the stock overview table and for stock-select dropdowns.
/// LowStockThreshold comes from LogIntakeService's real StockThresholds table.
/// </summary>
public class RawStockOverviewDto
{
    public int StockId { get; set; }
    public int SpeciesId { get; set; }
    public string Species { get; set; } = string.Empty;
    public int LengthId { get; set; }
    public decimal LengthFt { get; set; }
    public int LogCount { get; set; }
    public decimal TotalVolumeM3 { get; set; }
    public decimal LowStockThreshold { get; set; }
    public bool IsLowStock => TotalVolumeM3 <= LowStockThreshold;
}
