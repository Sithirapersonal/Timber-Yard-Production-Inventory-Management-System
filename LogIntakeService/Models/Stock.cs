namespace LogIntakeService.Models;

public class Stock
{
    public int StockId { get; set; }
    public int SpeciesId { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public int LengthId { get; set; }
    public decimal LengthFt { get; set; }
}
