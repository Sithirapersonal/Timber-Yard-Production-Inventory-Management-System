namespace LogIntakeService.Models;

public class TimberDelivery
{
    public int DeliveryId { get; set; }
    public int SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? Species { get; set; }
    public string? Grade { get; set; }
    public decimal? VolumeM3 { get; set; }
    public string? VehicleNumber { get; set; }
    public int? LogCount { get; set; }
    public string? Notes { get; set; }
    public DateTime ReceivedAt { get; set; }
    public int ReceivedBy { get; set; }
}