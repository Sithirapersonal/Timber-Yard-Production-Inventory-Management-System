using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class CreateDeliveryDto
{
    [Required]
    public int SupplierId { get; set; }

    [Required]
    [StringLength(50)]
    public string Species { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string Grade { get; set; } = string.Empty;

    [Range(0.01, 10000.00, ErrorMessage = "Volume must be greater than zero.")]
    public decimal VolumeM3 { get; set; }

    [StringLength(20)]
    public string? VehicleNumber { get; set; }

    [Required]
    public int ReceivedBy { get; set; }
}