using SawmillService.DTOs;
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
    Task<SawJob?> GetJobByIdAsync(int sawJobId);
    Task<IEnumerable<SawJobLogAllocation>> GetLogAllocationsForJobAsync(int sawJobId);

    // Saw-job history (Completed and Cancelled only, no limit).
    // from/to are inclusive bounds on StartedAt, both optional (null = unbounded).
    // A non-null 'to' is treated as END-OF-DAY on that calendar day, matching
    // GetWastageYieldReportAsync's existing date-bound convention.
    Task<IEnumerable<SawJob>> GetJobHistoryAsync(DateTime? from, DateTime? to);

    // Wastage & yield report (Completed jobs only, bounded by CompletedAt)
    // from/to are inclusive date bounds on CompletedAt, both optional (null = unbounded).
    // A non-null 'to' provided as a date is treated as END-OF-DAY: a job completed
    // anytime on that calendar day is included.
    Task<WastageYieldReportDto> GetWastageYieldReportAsync(DateTime? from, DateTime? to);

    // Job completion & revert
    Task<bool> CompleteSawJobAsync(int sawJobId, decimal outputVolumeM3, decimal wastageM3);
    Task<bool> RevertToInProgressAsync(int sawJobId);
    Task<bool> IsMachineInUseAsync(int machineId, int excludeSawJobId);

    // Job cancellation (Admin only)
    Task<bool> CancelJobAsync(int sawJobId);

    // Job-code generation helper
    Task<string> GenerateNextJobCodeAsync();
}
