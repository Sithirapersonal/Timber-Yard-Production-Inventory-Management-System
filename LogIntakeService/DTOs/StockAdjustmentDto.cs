using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class StockAdjustmentDto
{
    [Required]
    [StringLength(50)]
    public string Species { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string Grade { get; set; } = string.Empty;

    [Required]
    public decimal AdjustedVolumeM3 { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 3, ErrorMessage = "Reason is required.")]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public int AdjustedBy { get; set; }
}