using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SawmillService.DTOs;
using SawmillService.Repositories;
using SawmillService.Services;

namespace SawmillService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SawmillController : ControllerBase
{
    private readonly ISawmillRepository _repository;
    private readonly LogIntakeClient _logIntakeClient;
    private readonly ILogger<SawmillController> _logger;

    public SawmillController(
        ISawmillRepository repository,
        LogIntakeClient logIntakeClient,
        ILogger<SawmillController> logger)
    {
        _repository = repository;
        _logIntakeClient = logIntakeClient;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/Sawmill/stock
    // Proxy: returns the raw stock overview from LogIntakeService.
    // The real per-stock LowStockThreshold is returned by LogIntakeService itself.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("stock")]
    public async Task<IActionResult> GetStock()
    {
        var token = ExtractBearerToken();
        if (token is null) return Unauthorized();

        try
        {
            var stock = await _logIntakeClient.GetStockAsync(token);
            return Ok(stock);
        }
        catch (LogIntakeServiceException ex)
        {
            _logger.LogError(ex, "Upstream error fetching stock");
            return StatusCode(502, new { message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/Sawmill/stock/{stockId}/logs
    // Returns in-stock individual logs for one stock batch.
    // Resolves StockId → SpeciesId/LengthId via the stock list, then proxies
    // to LogIntakeService GET /logs?speciesId=&lengthId=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("stock/{stockId:int}/logs")]
    public async Task<IActionResult> GetLogsForStock(int stockId)
    {
        var token = ExtractBearerToken();
        if (token is null) return Unauthorized();

        try
        {
            // Resolve StockId → SpeciesId and LengthId via the stock overview
            var allStock = await _logIntakeClient.GetStockAsync(token);
            var stockBatch = allStock.FirstOrDefault(s => s.StockId == stockId);
            if (stockBatch is null)
            {
                return NotFound(new { message = $"Stock batch {stockId} not found." });
            }

            var logs = await _logIntakeClient.GetLogsAsync(token, stockBatch.SpeciesId, stockBatch.LengthId);
            return Ok(logs);
        }
        catch (LogIntakeServiceException ex)
        {
            _logger.LogError(ex, "Upstream error fetching logs for stock {StockId}", stockId);
            return StatusCode(502, new { message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/Sawmill/workers
    // Active workers with optional ?q= search (FullName or EmployeeCode).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("workers")]
    public async Task<IActionResult> GetWorkers([FromQuery] string? q = null)
    {
        var workers = await _repository.GetActiveWorkersAsync(q);
        return Ok(workers);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/Sawmill/machines
    // All machines with optional ?q= search (Name or MachineCode). Includes
    // non-Available machines so the frontend can show/disable them by status.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("machines")]
    public async Task<IActionResult> GetMachines([FromQuery] string? q = null)
    {
        var machines = await _repository.GetMachinesAsync(q);
        return Ok(machines);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST api/Sawmill/jobs
    // Starts a saw job: validates, computes volume, writes DB, marks logs Consumed.
    // If the LogIntakeService consume call fails, the local insert is rolled back.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("jobs")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> StartSawJob([FromBody] StartSawJobDto dto)
    {
        var token = ExtractBearerToken();
        if (token is null) return Unauthorized();

        // ── Server-side validation ─────────────────────────────────────────
        if (dto.LogIds is null || dto.LogIds.Count == 0)
            return BadRequest(new { message = "At least one log must be selected." });

        if (dto.WorkerIds is null || dto.WorkerIds.Count == 0)
            return BadRequest(new { message = "At least one worker must be assigned." });

        if (dto.MachineId <= 0)
            return BadRequest(new { message = "A machine must be allocated to this saw job." });

        var userIdClaim = User.FindFirst("userId")?.Value;
        var startedBy = int.TryParse(userIdClaim, out var uid) ? uid : 0;

        // ── Re-verify logs from live LogIntakeService data ─────────────────
        List<RawStockOverviewDto> allStock;
        RawStockOverviewDto? stockBatch;
        List<LogItemDto> inStockLogs;

        try
        {
            allStock = await _logIntakeClient.GetStockAsync(token);
            stockBatch = allStock.FirstOrDefault(s => s.StockId == dto.StockId);
            if (stockBatch is null)
                return BadRequest(new { message = $"Stock batch {dto.StockId} not found in LogIntakeService." });

            inStockLogs = await _logIntakeClient.GetLogsAsync(token, stockBatch.SpeciesId, stockBatch.LengthId);
        }
        catch (LogIntakeServiceException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }

        // All requested LogIds must be InStock and belong to this StockId
        var inStockLogMap = inStockLogs
            .Where(l => l.Status == "InStock" && l.StockId == dto.StockId)
            .ToDictionary(l => l.LogId);

        var invalidIds = dto.LogIds.Where(id => !inStockLogMap.ContainsKey(id)).ToList();
        if (invalidIds.Count > 0)
        {
            return BadRequest(new
            {
                message = $"The following LogIds are not InStock or do not belong to stock {dto.StockId}: " +
                          string.Join(", ", invalidIds)
            });
        }

        // ── Validate workers in SawmillDB ──────────────────────────────────
        var workers = (await _repository.GetWorkersByIdsAsync(dto.WorkerIds)).ToList();
        var foundActiveWorkerIds = workers.Where(w => w.IsActive).Select(w => w.WorkerId).ToHashSet();
        var invalidWorkerIds = dto.WorkerIds.Where(id => !foundActiveWorkerIds.Contains(id)).ToList();
        if (invalidWorkerIds.Count > 0)
        {
            return BadRequest(new
            {
                message = $"The following WorkerIds are not active or do not exist: " +
                          string.Join(", ", invalidWorkerIds)
            });
        }

        // ── Validate machine in SawmillDB ──────────────────────────────────
        var machine = await _repository.GetMachineByIdAsync(dto.MachineId);
        if (machine is null)
            return BadRequest(new { message = $"Machine {dto.MachineId} does not exist." });
        if (machine.Status != "Available")
            return BadRequest(new { message = $"Machine {machine.MachineCode} ({machine.Name}) is not Available (current status: {machine.Status})." });

        // ── Compute TotalVolumeM3 server-side ─────────────────────────────
        var logAllocations = dto.LogIds
            .Select(id => (LogId: id, VolumeM3: inStockLogMap[id].VolumeM3))
            .ToList();

        var totalVolumeM3 = logAllocations.Sum(l => l.VolumeM3);

        // ── Write to SawmillDB ────────────────────────────────────────────
        SawmillService.Models.SawJob createdJob;
        try
        {
            createdJob = await _repository.CreateSawJobAsync(
                stockId: dto.StockId,
                speciesName: stockBatch.Species,
                lengthFt: stockBatch.LengthFt,
                totalVolumeM3: totalVolumeM3,
                notes: dto.Notes?.Trim(),
                startedBy: startedBy,
                logs: logAllocations,
                workerIds: dto.WorkerIds,
                machineId: machine.MachineId,
                machineCode: machine.MachineCode,
                machineName: machine.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write saw job to SawmillDB");
            return StatusCode(500, new { message = "Failed to create saw job. Please retry." });
        }

        // ── Mark logs Consumed in LogIntakeService ─────────────────────────
        // If this fails, we roll back the local rows by cancelling the job immediately.
        (bool success, string? errorMsg) consumeResult;
        try
        {
            consumeResult = await _logIntakeClient.ConsumeLogsAsync(token, dto.LogIds);
        }
        catch (LogIntakeServiceException ex)
        {
            // Roll back by cancelling the local job
            await _repository.CancelJobAsync(createdJob.SawJobId);
            _logger.LogError(ex, "LogIntakeService unreachable during consume; local job {JobId} cancelled", createdJob.SawJobId);
            return StatusCode(502, new
            {
                message = "Saw job could not be completed: LogIntakeService is unreachable. " +
                          "The job has been cancelled. Please retry."
            });
        }

        if (!consumeResult.success)
        {
            // Roll back by cancelling the local job
            await _repository.CancelJobAsync(createdJob.SawJobId);
            _logger.LogWarning("LogIntakeService refused consume for job {JobId}; local job cancelled", createdJob.SawJobId);
            return StatusCode(409, new
            {
                message = "LogIntakeService refused to mark the selected logs as Consumed " +
                          "(they may have already been consumed or removed). " +
                          "The job has been cancelled. Please refresh and retry."
            });
        }

        createdJob.AssignedWorkerNames = workers.Select(w => $"{w.FullName} ({w.EmployeeCode})").ToList();

        return StatusCode(201, createdJob);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/Sawmill/jobs
    // Returns the most recently started saw jobs (latest 20).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var jobs = await _repository.GetRecentJobsAsync(20);
        return Ok(jobs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT api/Sawmill/jobs/{id}/complete
    // Admin, Manager, Supervisor — marks an InProgress saw job Completed with sawn board output.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("jobs/{id:int}/complete")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> CompleteSawJob(int id, [FromBody] CompleteSawJobDto dto)
    {
        if (dto.Boards == null || dto.Boards.Count == 0 ||
            dto.Boards.Any(b => b.LengthFt <= 0 || b.WidthIn <= 0 || b.ThicknessIn <= 0 || b.Quantity <= 0))
        {
            return BadRequest(new { message = "Enter a valid length, width, thickness and quantity for every board row." });
        }

        var job = await _repository.GetJobByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Job not found." });
        }

        if (job.Status != "InProgress")
        {
            return Conflict(new { message = "This job is no longer In Progress and cannot be completed." });
        }

        const decimal cubicFeetToCubicMeters = 0.028316846592m;
        decimal totalBoardVolumeFt3 = dto.Boards.Sum(b => b.LengthFt * (b.WidthIn / 12m) * (b.ThicknessIn / 12m) * b.Quantity);
        decimal totalBoardVolumeM3 = Math.Round(totalBoardVolumeFt3 * cubicFeetToCubicMeters, 4, MidpointRounding.AwayFromZero);

        decimal wastageM3 = job.TotalVolumeM3 - totalBoardVolumeM3;
        decimal deviation = job.TotalVolumeM3 > 0 ? Math.Abs(wastageM3) / job.TotalVolumeM3 : 0m;
        bool looksOff = (wastageM3 < 0) || (deviation > 0.35m);

        if (looksOff && !dto.AcknowledgedDeviationWarning)
        {
            return Conflict(new
            {
                requiresAcknowledgment = true,
                message = "This looks off — the boards entered deviate significantly from the raw input volume logged for this job."
            });
        }

        var completed = await _repository.CompleteSawJobAsync(id, totalBoardVolumeM3, wastageM3);
        if (!completed)
        {
            return Conflict(new { message = "This job is no longer In Progress and cannot be completed." });
        }

        var updatedJob = await _repository.GetJobByIdAsync(id);
        return Ok(updatedJob);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT api/Sawmill/jobs/{id}/cancel
    // Admin only — marks a job Cancelled.
    // Logs are NOT reverted to InStock (see SawmillRepository.CancelJobAsync for rationale).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("jobs/{id:int}/cancel")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CancelJob(int id)
    {
        var cancelled = await _repository.CancelJobAsync(id);
        if (!cancelled)
        {
            return NotFound(new { message = "Job not found or is not in an InProgress state." });
        }
        return Ok(new { message = "Saw job cancelled successfully." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT api/Sawmill/jobs/{id}/revert
    // Admin, Manager, Supervisor — reverts a Completed or Cancelled job back to InProgress.
    // Clears OutputVolumeM3 and WastageM3. Verifies machine is not in use on another job.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("jobs/{id:int}/revert")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> RevertJob(int id)
    {
        var job = await _repository.GetJobByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Job not found." });
        }

        if (job.Status == "InProgress")
        {
            return Conflict(new { message = "Job is already In Progress." });
        }

        if (job.MachineId > 0 && await _repository.IsMachineInUseAsync(job.MachineId, id))
        {
            return Conflict(new
            {
                message = $"Cannot revert job {job.JobCode} to In Progress because machine {job.MachineName} ({job.MachineCode}) is currently in use on another active job."
            });
        }

        var reverted = await _repository.RevertToInProgressAsync(id);
        if (!reverted)
        {
            return Conflict(new { message = "Could not revert job to In Progress." });
        }

        var updatedJob = await _repository.GetJobByIdAsync(id);
        return Ok(updatedJob);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/Sawmill/jobs/wastage-yield-report?from=YYYY-MM-DD&to=YYYY-MM-DD
    // Wastage & Yield Report over completed saw jobs, filtered by the date each
    // job was Completed (CompletedAt). Management-facing analytics — Admin and
    // Manager only, deliberately distinct from the operational roles used for
    // starting/completing/reverting jobs.
    // Both bounds are optional: omit either for an unbounded side, omit both for
    // every completed job. A 'to' date is treated as end-of-day inclusive.
    // All totals and percentages are computed server-side in the repository.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("jobs/wastage-yield-report")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetWastageYieldReport(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!TryParseReportDate(from, out var fromDate))
            return BadRequest(new { message = "Invalid 'from' date. Use YYYY-MM-DD." });

        if (!TryParseReportDate(to, out var toDate))
            return BadRequest(new { message = "Invalid 'to' date. Use YYYY-MM-DD." });

        var report = await _repository.GetWastageYieldReportAsync(fromDate, toDate);
        return Ok(report);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an optional YYYY-MM-DD query value into a UTC-midnight DateTime.
    /// Empty/null is valid and means "unbounded" (value = null). The repository
    /// treats a non-null 'to' as end-of-day inclusive on that calendar day.
    /// </summary>
    private static bool TryParseReportDate(string? raw, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    /// <summary>Extracts the raw Bearer token string from the Authorization header.</summary>
    private string? ExtractBearerToken()
    {
        var authHeader = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return authHeader["Bearer ".Length..].Trim();
    }
}
