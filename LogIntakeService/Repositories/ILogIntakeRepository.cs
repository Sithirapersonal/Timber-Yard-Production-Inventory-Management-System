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
}