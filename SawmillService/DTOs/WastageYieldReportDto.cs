namespace SawmillService.DTOs;

/// <summary>
/// Wastage &amp; yield report over completed saw jobs, optionally bounded by the
/// date each job was Completed (CompletedAt). All totals and percentages are
/// computed server-side (repository); the frontend only ever displays them.
/// All volumes are cubic metres (M3/m³) — the unit used throughout this service.
/// </summary>
public class WastageYieldReportDto
{
    public WastageYieldTotalsDto Totals { get; set; } = new();
    public List<SpeciesWastageYieldDto> SpeciesBreakdown { get; set; } = new();
    public List<JobWastageYieldDto> Jobs { get; set; } = new();
}

/// <summary>Overall figures across every completed job in range.</summary>
public class WastageYieldTotalsDto
{
    public int CompletedJobCount { get; set; }
    public decimal TotalInputVolumeM3 { get; set; }
    /// <summary>Sum of OutputVolumeM3 — the finished sawn yield.</summary>
    public decimal TotalYieldVolumeM3 { get; set; }
    public decimal TotalWastageVolumeM3 { get; set; }
    /// <summary>TotalYieldVolumeM3 / TotalInputVolumeM3 × 100, rounded to 1 dp.</summary>
    public decimal RecoveryPercentage { get; set; }
    /// <summary>TotalWastageVolumeM3 / TotalInputVolumeM3 × 100, rounded to 1 dp.</summary>
    public decimal WastagePercentage { get; set; }
}

/// <summary>One row per species, for the "Recovery Rate by Species" chart.</summary>
public class SpeciesWastageYieldDto
{
    public string SpeciesName { get; set; } = string.Empty;
    public int CompletedJobCount { get; set; }
    public decimal InputVolumeM3 { get; set; }
    public decimal YieldVolumeM3 { get; set; }
    public decimal WastageVolumeM3 { get; set; }
    /// <summary>YieldVolumeM3 / InputVolumeM3 × 100, rounded to 1 dp.</summary>
    public decimal RecoveryPercentage { get; set; }
}

/// <summary>One row per completed job, for the "Wastage &amp; Output by Saw Job" table.</summary>
public class JobWastageYieldDto
{
    public string JobCode { get; set; } = string.Empty;
    public string SpeciesName { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public decimal InputVolumeM3 { get; set; }
    public decimal YieldVolumeM3 { get; set; }
    public decimal WastageVolumeM3 { get; set; }
    /// <summary>WastageVolumeM3 / InputVolumeM3 × 100, rounded to 1 dp.</summary>
    public decimal WastagePercentage { get; set; }
}