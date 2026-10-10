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
    /// Cancels a Pending batch: flips status to Cancelled, restores the allocated
    /// sawn stock, and logs an audit stock movement (TREATMENT_CANCELLED).
    /// Rejects with InvalidOperationException for batches that are InTreatment or Completed.
    /// </summary>
    Task<bool> CancelBatchAsync(int batchId, string reason);

    /// <summary>
    /// Completes an InTreatment batch with treated and rejected quantities.
    /// Rejects when the batch isn't InTreatment or when treated + rejected exceeds the batch input quantity.
    /// Credits treated stock and logs a StockMovements row. Returns true on success.
    /// </summary>
    Task<bool> CompleteBatchAsync(int batchId, decimal treatedM3, decimal rejectedM3);

    /// <summary>
    /// Lists treated stock grouped by species/dimension/chemical type.
    /// </summary>
    Task<IEnumerable<TreatedStock>> GetTreatedStockAsync();

    /// <summary>
    /// Upserts the low-stock threshold for a treated-stock grade.
    /// </summary>
    Task<bool> SetTreatedStockThresholdAsync(string species, string dimensions, string chemicalType, decimal thresholdM3);

    /// <summary>
    /// Evaluates all thresholds in C# and returns the treated stock rows whose
    /// volume is below their threshold (each an active low-stock alert).
    /// </summary>
    Task<IEnumerable<TreatedStockAlert>> GetTreatedStockAlertsAsync();

    /// <summary>
    /// Assigns a pending batch to a tank and starts treatment.
    /// Rejects with InvalidOperationException when: the batch is not Pending,
    /// the batch quantity exceeds the tank capacity, or the tank is already
    /// busy with another InTreatment batch. Returns true on success.
    /// </summary>
    Task<bool> StartBatchAsync(int batchId, int tankId);

    /// <summary>
    /// Lists all active tanks with live status (Idle/Busy) and the current batch when busy.
    /// </summary>
    Task<IEnumerable<TankAvailability>> GetTanksAvailabilityAsync();

    /// <summary>
    /// Lists treatment batches, optionally filtered by Status (e.g. Pending, InTreatment, Completed, Cancelled).
    /// </summary>
    Task<IEnumerable<TreatmentBatch>> GetBatchesAsync(string? status = null);

    /// <summary>
    /// Gets a single batch with full detail (tank, cancellation reason, timestamps). Returns null when missing.
    /// </summary>
    Task<TreatmentBatch?> GetBatchByIdAsync(int batchId);

    /// <summary>
    /// Builds the treatment turnaround report: completed batches, active cycles and
    /// average duration per chemical type, filtered by the optional UTC date range.
    /// Duration calculations are performed in C# from StartedAt/CompletedAt.
    /// </summary>
    Task<TreatmentDurationReport> GetDurationReportAsync(DateTime? fromUtc, DateTime? toUtc);
}
