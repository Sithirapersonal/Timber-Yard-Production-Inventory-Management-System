using LogIntakeService.DTOs;
using LogIntakeService.Models;

namespace LogIntakeService.Repositories;

public interface ILogIntakeRepository
{
    Task<int> RecordDeliveryAsync(TimberDelivery delivery, IEnumerable<DeliveryLogEntryDto> logs);
    Task<IEnumerable<StockSummaryDto>> GetAllStockAsync();
    Task<IEnumerable<TimberDelivery>> GetDeliveriesAsync();
    Task<bool> AdjustStockAsync(StockAdjustment adjustment);
    Task<bool> UpdateThresholdAsync(string species, string grade, decimal threshold);
    Task<IEnumerable<Supplier>> GetActiveSuppliersAsync(bool includeInactive = false);
    Task<int> AddSupplierAsync(Supplier supplier);
    Task<bool> DeactivateSupplierAsync(int supplierId);

    Task<IEnumerable<Species>> GetSpeciesAsync();
    Task<IEnumerable<LogLength>> GetLogLengthsAsync();
    Task<IEnumerable<LogItem>> GetLogsAsync(int? speciesId = null, string? species = null, int? lengthId = null, string? grade = null);
    Task<bool> RemoveLogAsync(int logId, string reason, int removedBy);
}