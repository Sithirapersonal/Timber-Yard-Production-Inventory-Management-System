using LogIntakeService.DTOs;
using LogIntakeService.Models;

namespace LogIntakeService.Repositories;

public interface ILogIntakeRepository
{
    Task<int> RecordDeliveryAsync(TimberDelivery delivery, IEnumerable<DeliveryLogEntryDto> logs);
    Task<IEnumerable<StockSummaryDto>> GetAllStockAsync();
    Task<IEnumerable<TimberDelivery>> GetDeliveriesAsync();
    Task<bool> AdjustStockAsync(StockAdjustment adjustment);
    Task<bool> UpdateThresholdAsync(int stockId, decimal threshold);
    Task<IEnumerable<Supplier>> GetActiveSuppliersAsync(bool includeInactive = false);
    Task<int> AddSupplierAsync(Supplier supplier);
    Task<bool> DeactivateSupplierAsync(int supplierId);

    Task<IEnumerable<Species>> GetSpeciesAsync();
    Task<IEnumerable<LogLength>> GetLogLengthsAsync();
    Task<IEnumerable<LogItem>> GetLogsAsync(int? speciesId = null, int? lengthId = null);
    Task<bool> RemoveLogAsync(int logId, string reason, int removedBy);
    Task<int> FindOrCreateStockAsync(int speciesId, int lengthId);

    /// <summary>
    /// Marks all supplied LogIds as Consumed in a single DB transaction.
    /// Only logs whose current Status = 'InStock' are eligible.
    /// Returns false (with rollback) if ANY requested log is not InStock.
    /// Called by SawmillService via PUT /api/LogIntake/logs/consume.
    /// </summary>
    Task<bool> ConsumeLogsAsync(IEnumerable<int> logIds);

    /// <summary>
    /// Flips all supplied LogIds from 'Consumed' back to 'InStock' in a single
    /// DB transaction. Deliberately tolerant of partial state (NOT all-or-nothing):
    /// logs that are already InStock, Removed, or absent are simply not matched, so a
    /// re-delivered Kafka event harmlessly updates 0 rows. Returns the number of rows
    /// actually updated — callers log (don't throw) when it differs from the request.
    /// Called by the raw-stock-reversed consumer after a saw job is cancelled.
    /// </summary>
    Task<int> ReleaseLogsAsync(IEnumerable<int> logIds);

    /// <summary>
    /// Marks all supplied LogIds as 'Consumed' where they are currently 'InStock' in a
    /// single DB transaction. Deliberately tolerant of partial state (NOT all-or-nothing,
    /// unlike ConsumeLogsAsync): Kafka can redeliver the same event, and by the time it
    /// is processed some logs may already be Consumed (prior delivery) or in some other
    /// state (Removed/absent). Those are simply not matched — a second delivery updates
    /// 0 rows harmlessly. Returns the number of rows actually updated — callers log
    /// (don't throw) when it differs from the request.
    /// Called by the logs-consumed consumer after a saw job is started.
    /// </summary>
    Task<int> MarkLogsConsumedAsync(IEnumerable<int> logIds);
}