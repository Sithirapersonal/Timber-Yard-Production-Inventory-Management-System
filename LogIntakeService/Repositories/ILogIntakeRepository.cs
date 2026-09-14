using LogIntakeService.Models;

namespace LogIntakeService.Repositories;

public interface ILogIntakeRepository
{
    Task<int> RecordDeliveryAsync(TimberDelivery delivery);
    Task<IEnumerable<RawStock>> GetAllStockAsync();
    Task<IEnumerable<TimberDelivery>> GetDeliveriesAsync();
    Task<bool> AdjustStockAsync(StockAdjustment adjustment);
    Task<bool> UpdateThresholdAsync(string species, string grade, decimal threshold);
    Task<IEnumerable<Supplier>> GetActiveSuppliersAsync();
    Task<bool> DeactivateSupplierAsync(int supplierId);
}