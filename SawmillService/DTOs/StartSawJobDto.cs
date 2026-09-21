namespace SawmillService.DTOs;

/// <summary>
/// Client-submitted payload to start a new saw job.
/// TotalVolumeM3 is never supplied by the client — it is computed server-side.
/// StartedBy is never supplied by the client — it comes from the JWT userId claim.
/// JobCode is never supplied by the client — it is generated server-side (SAW-001 format).
/// </summary>
public class StartSawJobDto
{
    /// <summary>StockId from LogIntakeService — identifies the species/length batch being processed.</summary>
    public int StockId { get; set; }

    /// <summary>LogIds from LogIntakeService to allocate to this job. Must be non-empty and all InStock.</summary>
    public List<int> LogIds { get; set; } = new();

    /// <summary>WorkerIds from the local SawmillDB Workers table. Must be non-empty and all active.</summary>
    public List<int> WorkerIds { get; set; } = new();

    /// <summary>MachineId from the local SawmillDB Machines table. Exactly one machine per job; must be 'Available'.</summary>
    public int MachineId { get; set; }

    /// <summary>Optional operator notes (max 500 chars).</summary>
    public string? Notes { get; set; }
}
