namespace SawmillService.Models;

/// <summary>
/// A saw job that processes one or more raw logs from a LogIntakeService stock batch.
/// SpeciesName and LengthFt are denormalized snapshots captured at creation time.
/// TotalVolumeM3 is computed server-side from the sum of allocated log volumes.
/// AssignedWorkerNames holds "FullName (EmployeeCode)" strings, populated on read.
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

    /// <summary>The machine this job runs on. MachineName/MachineCode are denormalized
    /// snapshots taken at creation time (so a later machine rename/removal doesn't
    /// change historical job records).</summary>
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;

    public decimal? OutputVolumeM3 { get; set; }
    public decimal? WastageM3 { get; set; }

    /// <summary>When the job was marked Completed (UTC). Written by the Complete
    /// update (UTC_TIMESTAMP), cleared back to NULL on revert — matching how
    /// OutputVolumeM3/WastageM3 are cleared. Null while the job is
    /// InProgress/Cancelled, or for Completed rows that predate this column.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    // Populated for list/detail views — not stored in DB
    public List<string> AssignedWorkerNames { get; set; } = new();
    public List<SawJobLogAllocation> AllocatedLogs { get; set; } = new();
}
