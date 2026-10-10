namespace TreatmentService.Models;

/// <summary>
/// Treatment turnaround / duration report for a selected date range.
/// All durations are derived in C# from StartedAt/CompletedAt of completed batches.
/// </summary>
public class TreatmentDurationReport
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Completed batches whose CompletedAt falls within the range.</summary>
    public int CompletedBatches { get; set; }

    /// <summary>Batches currently in treatment (InTreatment) whose StartedAt falls within the range.</summary>
    public int ActiveCycles { get; set; }

    /// <summary>Overall average turnaround in hours across completed batches in range.</summary>
    public double? AverageTurnaroundHours { get; set; }

    /// <summary>Per-chemical-type turnaround comparison, ordered by average duration descending.</summary>
    public List<ChemicalTypeDuration> ByChemicalType { get; set; } = new();
}

/// <summary>
/// Turnaround statistics for one chemical type within the report range.
/// </summary>
public class ChemicalTypeDuration
{
    public string ChemicalType { get; set; } = string.Empty;
    public int CompletedCount { get; set; }
    public double AverageDurationHours { get; set; }
    public double MinDurationHours { get; set; }
    public double MaxDurationHours { get; set; }
}
