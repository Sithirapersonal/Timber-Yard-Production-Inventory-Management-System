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
                workerIds: dto.WorkerIds);
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

        createdJob.AssignedWorkerNames = workers.Select(w => w.FullName).ToList();

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
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Extracts the raw Bearer token string from the Authorization header.</summary>
    private string? ExtractBearerToken()
    {
        var authHeader = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return authHeader["Bearer ".Length..].Trim();
    }
}
