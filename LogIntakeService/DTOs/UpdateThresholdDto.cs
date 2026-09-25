using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class UpdateThresholdDto
{
    [Required]
    public int StockId { get; set; }

    [Range(0.00, 5000.00, ErrorMessage = "Threshold must be non-negative.")]
    public decimal LowStockThreshold { get; set; }
}