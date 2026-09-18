using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class DeliveryLogEntryDto
{
    [Required]
    public int SpeciesId { get; set; }

    [Required]
    public int LengthId { get; set; }

    [Required]
    [StringLength(10)]
    public string Grade { get; set; } = string.Empty;

    [Range(0.01, 1000.00, ErrorMessage = "Girth must be greater than zero.")]
    public decimal GirthFt { get; set; }
}
