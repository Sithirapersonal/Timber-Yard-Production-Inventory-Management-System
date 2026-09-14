namespace LogIntakeService.Models;

public class TimberDelivery
{
    public int DeliveryId { get; set; }
    public int SupplierId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public decimal VolumeM3 { get; set; }
    public string? VehicleNumber { get; set; }
    public DateTime ReceivedAt { get; set; }
    public int ReceivedBy { get; set; }
}