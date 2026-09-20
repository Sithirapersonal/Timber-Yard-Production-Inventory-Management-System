namespace LogIntakeService.Models;

public class LogItem
{
    public int LogId { get; set; }
    public int DeliveryId { get; set; }
    public int StockId { get; set; }
    public int SpeciesId { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public int LengthId { get; set; }
    public decimal LengthFt { get; set; }
    public decimal GirthFt { get; set; }
    public decimal VolumeM3 { get; set; }
    public string Status { get; set; } = "InStock";
    public string? SupplierName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RemovalReason { get; set; }
    public DateTime? RemovedAt { get; set; }
    public int? RemovedBy { get; set; }
}
