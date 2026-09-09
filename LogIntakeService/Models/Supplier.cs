namespace LogIntakeService.Models;

public class Supplier
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}