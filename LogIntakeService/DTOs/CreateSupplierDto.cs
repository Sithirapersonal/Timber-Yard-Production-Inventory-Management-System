using System.ComponentModel.DataAnnotations;

namespace LogIntakeService.DTOs;

public class CreateSupplierDto
{
    [Required(ErrorMessage = "Supplier name is required.")]
    [StringLength(100, ErrorMessage = "Supplier name cannot exceed 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(255, ErrorMessage = "Address cannot exceed 255 characters.")]
    public string? Address { get; set; }
}
