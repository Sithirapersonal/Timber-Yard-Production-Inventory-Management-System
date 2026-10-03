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
}
