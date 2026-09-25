namespace SawmillService.Models;

/// <summary>
/// A physical sawmill machine (saw rig) that a saw job runs on.
/// Exactly one machine is allocated per saw job (unlike Workers, which are many-to-many).
/// </summary>
public class Machine
{
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = string.Empty; // e.g. MCH-01
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Available"; // 'Available' | 'InUse' | 'UnderMaintenance'
    public DateTime CreatedAt { get; set; }
}
