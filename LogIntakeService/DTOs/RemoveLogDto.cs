using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class RemoveLogDto
{
    [Required(ErrorMessage = "Removal reason is required.")]
    [StringLength(255, ErrorMessage = "Removal reason cannot exceed 255 characters.")]
    public string Reason { get; set; } = string.Empty;
}
