namespace LogIntakeService.Models;

public class LogItem
{
    public int LogId { get; set; }
    public int DeliveryId { get; set; }
    public int SpeciesId { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public int LengthId { get; set; }
    public decimal LengthFt { get; set; }
    public string Grade { get; set; } = string.Empty;
    public decimal GirthFt { get; set; }
    public decimal VolumeM3 { get; set; }
    public string Status { get; set; } = "InStock";
    public DateTime CreatedAt { get; set; }
    public string? RemovalReason { get; set; }
    public DateTime? RemovedAt { get; set; }
    public int? RemovedBy { get; set; }
}
