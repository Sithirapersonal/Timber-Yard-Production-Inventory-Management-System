namespace LogIntakeService.Models;

public class StockThreshold
{
    public int StockId { get; set; }
    public decimal LowStockThreshold { get; set; } = 10.00m;
}
