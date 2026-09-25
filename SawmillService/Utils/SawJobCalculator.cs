namespace SawmillService.Utils;

/// <summary>
/// Pure, stateless helpers for saw-job business logic.
/// Separated into this utility class so they can be unit-tested without
/// touching the database or any external service.
/// </summary>
public static class SawJobCalculator
{
    /// <summary>
    /// Computes the server-side TotalVolumeM3 for a saw job from the
    /// volumes of the individual allocated logs.
    /// </summary>
    /// <param name="logVolumes">Sequence of per-log VolumeM3 values (must be non-negative).</param>
    /// <returns>Sum of all log volumes, or 0 when the sequence is empty.</returns>
    public static decimal ComputeTotalVolumeM3(IEnumerable<decimal> logVolumes)
        => logVolumes.Sum();

    /// <summary>
    /// Validates that all supplied LogIds belong to the requested StockId.
    /// Returns the list of invalid IDs (IDs not found in the stock's InStock logs).
    /// An empty list means validation passed.
    /// </summary>
    /// <param name="requestedLogIds">LogIds submitted by the client.</param>
    /// <param name="inStockLogIds">LogIds that are InStock for the stock batch, per LogIntakeService.</param>
    public static IReadOnlyList<int> FindInvalidLogIds(
        IEnumerable<int> requestedLogIds,
        IEnumerable<int> inStockLogIds)
    {
        var validSet = inStockLogIds.ToHashSet();
        return requestedLogIds.Where(id => !validSet.Contains(id)).ToList().AsReadOnly();
    }

    /// <summary>
    /// Formats a sequential 1-based integer into the SAW-NNN job code format.
    /// The number is zero-padded to at least 3 digits (SAW-001, SAW-042, SAW-1000, …).
    /// </summary>
    /// <param name="nextSequenceNumber">The next sequential job number (must be >= 1).</param>
    public static string FormatJobCode(int nextSequenceNumber)
    {
        if (nextSequenceNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(nextSequenceNumber), "Sequence number must be >= 1.");
        return $"SAW-{nextSequenceNumber:D3}";
    }
}
