using TreatmentService.Events;
using TreatmentService.Models;

namespace TreatmentService.Repositories;

public interface ITreatmentStockRepository
{
    /// <summary>
    /// Credits sawn stock balance for the given species and dimensions in an atomic transaction,
    /// recording the eventId in ProcessedEvents.
    /// Returns true if newly credited; returns false if the eventId was already processed (duplicate delivery).
    /// </summary>
    Task<bool> CreditSawnStockAsync(SawnStockCreditedEvent evt);

    /// <summary>
    /// Retrieves current sawn stock volume in m3 for a given species and dimensions.
    /// </summary>
    Task<decimal> GetStockBalanceAsync(string species, string dimensions);

    /// <summary>
    /// Retrieves all sawn stock inventory records.
    /// </summary>
    Task<IEnumerable<SawnStock>> GetAllStockAsync();

    /// <summary>
    /// Checks whether an event with the given eventId has already been processed.
    /// </summary>
    Task<bool> HasEventBeenProcessedAsync(string eventId);

    /// <summary>
    /// Creates a treatment batch, deducting the requested volume from the species/dimensions
    /// sawn-stock balance, in a single transaction. Rejects with InvalidOperationException
    /// when the requested quantity exceeds available stock (no stock changes are made).
    /// A StockMovements audit row is written alongside. Returns the new BatchId.
    /// </summary>
    Task<int> CreateBatchAsync(string species, string dimensions, string chemicalType, decimal quantityM3);

    /// <summary>
    /// Lists treatment batches, optionally filtered by Status (e.g. Pending, InTreatment, Completed, Cancelled).
    /// </summary>
    Task<IEnumerable<TreatmentBatch>> GetBatchesAsync(string? status = null);

    /// <summary>
    /// Gets a single batch with full detail (tank, cancellation reason, timestamps). Returns null when missing.
    /// </summary>
    Task<TreatmentBatch?> GetBatchByIdAsync(int batchId);
}
