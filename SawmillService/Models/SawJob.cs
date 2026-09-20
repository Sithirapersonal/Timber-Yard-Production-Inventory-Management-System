namespace SawmillService.Models;

/// <summary>
/// A saw job that processes one or more raw logs from a LogIntakeService stock batch.
/// SpeciesName and LengthFt are denormalized snapshots captured at creation time.
/// TotalVolumeM3 is computed server-side from the sum of allocated log volumes.
/// AssignedWorkerNames is populated on read for list/detail views.
/// AllocatedLogs is populated for detail views.
/// </summary>
public class SawJob
{
    public int SawJobId { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public int StockId { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public decimal LengthFt { get; set; }
    public decimal TotalVolumeM3 { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "InProgress";
    public int StartedBy { get; set; }
    public DateTime StartedAt { get; set; }

    // Populated for list/detail views — not stored in DB
    public List<string> AssignedWorkerNames { get; set; } = new();
    public List<SawJobLogAllocation> AllocatedLogs { get; set; } = new();
}
