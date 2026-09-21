using SawmillService.Models;

namespace SawmillService.Repositories;

public interface ISawmillRepository
{
    // Worker queries
    Task<IEnumerable<Worker>> GetActiveWorkersAsync(string? searchQuery = null);
    Task<IEnumerable<Worker>> GetWorkersByIdsAsync(IEnumerable<int> workerIds);

    // Machine queries
    Task<IEnumerable<Machine>> GetMachinesAsync(string? searchQuery = null);
    Task<Machine?> GetMachineByIdAsync(int machineId);

    // Saw-job writes
    Task<SawJob> CreateSawJobAsync(
        int stockId,
        string speciesName,
        decimal lengthFt,
        decimal totalVolumeM3,
        string? notes,
        int startedBy,
        IEnumerable<(int LogId, decimal VolumeM3)> logs,
        IEnumerable<int> workerIds,
        int machineId,
        string machineCode,
        string machineName);

    // Saw-job reads
    Task<IEnumerable<SawJob>> GetRecentJobsAsync(int limit = 20);

    // Job cancellation (Admin only)
    Task<bool> CancelJobAsync(int sawJobId);

    // Job-code generation helper
    Task<string> GenerateNextJobCodeAsync();
}
