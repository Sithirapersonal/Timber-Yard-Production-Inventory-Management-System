using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class UpdateThresholdDto
{
    [Required]
    [StringLength(50)]
    public string Species { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string Grade { get; set; } = string.Empty;

    [Range(0.00, 5000.00, ErrorMessage = "Threshold must be non-negative.")]
    public decimal LowStockThreshold { get; set; }
}