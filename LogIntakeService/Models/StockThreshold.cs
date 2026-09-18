namespace LogIntakeService.Models;

public class StockThreshold
{
    public int SpeciesId { get; set; }
    public int LengthId { get; set; }
    public string Grade { get; set; } = string.Empty;
    public decimal LowStockThreshold { get; set; } = 10.00m;
}
