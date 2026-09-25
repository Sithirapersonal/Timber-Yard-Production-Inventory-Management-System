namespace SawmillService.Models;

/// <summary>
/// Snapshot of a single log allocated to a saw job.
/// LogId is a value-only reference to LogIntakeService's Logs table — no cross-DB FK.
/// VolumeM3 is recorded at allocation time so it remains stable even if LogIntakeService changes.
/// </summary>
public class SawJobLogAllocation
{
    public int SawJobLogId { get; set; }
    public int SawJobId { get; set; }
    public int LogId { get; set; }
    public decimal VolumeM3 { get; set; }
}
