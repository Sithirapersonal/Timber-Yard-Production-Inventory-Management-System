using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class CreateDeliveryDto
{
    [Required]
    public int SupplierId { get; set; }

    [StringLength(20)]
    public string? VehicleNumber { get; set; }

    [Required]
    public int ReceivedBy { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "LogCount must be a positive integer.")]
    public int? LogCount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "At least one log entry is required.")]
    public List<DeliveryLogEntryDto> Logs { get; set; } = new();
}