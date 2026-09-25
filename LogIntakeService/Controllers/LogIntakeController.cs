using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LogIntakeService.DTOs;
using LogIntakeService.Models;
using LogIntakeService.Repositories;

namespace LogIntakeService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LogIntakeController : ControllerBase
{
    private readonly ILogIntakeRepository _repository;

    public LogIntakeController(ILogIntakeRepository repository)
    {
        _repository = repository;
    }

    [HttpPost("deliveries")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> RecordDelivery([FromBody] CreateDeliveryDto dto)
    {
        var delivery = new TimberDelivery
        {
            SupplierId = dto.SupplierId,
            VehicleNumber = dto.VehicleNumber?.Trim(),
            LogCount = dto.LogCount ?? dto.Logs.Count,
            Notes = dto.Notes?.Trim(),
            ReceivedBy = dto.ReceivedBy
        };

        var deliveryId = await _repository.RecordDeliveryAsync(delivery, dto.Logs);
        return StatusCode(StatusCodes.Status201Created, new { deliveryId, message = "Delivery intake logged successfully." });
    }

    [HttpGet("stock")]
    public async Task<IActionResult> GetStock()
    {
        var stock = await _repository.GetAllStockAsync();
        return Ok(stock);
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> GetDeliveries()
    {
        var deliveries = await _repository.GetDeliveriesAsync();
        return Ok(deliveries);
    }

    [HttpGet("species")]
    public async Task<IActionResult> GetSpecies()
    {
        var species = await _repository.GetSpeciesAsync();
        return Ok(species);
    }

    [HttpGet("log-lengths")]
    public async Task<IActionResult> GetLogLengths()
    {
        var lengths = await _repository.GetLogLengthsAsync();
        return Ok(lengths);
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int? speciesId,
        [FromQuery] int? lengthId)
    {
        var logs = await _repository.GetLogsAsync(speciesId, lengthId);
        return Ok(logs);
    }

    [HttpPut("logs/{logId:int}/remove")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveLog(int logId, [FromBody] RemoveLogDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "Removal reason is required." });
        }

        var userIdClaim = User.FindFirst("userId")?.Value;
        var userId = int.TryParse(userIdClaim, out var id) ? id : 1;

        var removed = await _repository.RemoveLogAsync(logId, dto.Reason.Trim(), userId);
        if (!removed)
        {
            return NotFound(new { message = "Log not found or cannot be removed (already consumed or removed)." });
        }

        return Ok(new { message = "Log removed from inventory successfully." });
    }

    /// <summary>
    /// Marks a batch of logs as Consumed in a single all-or-nothing transaction.
    /// Called by SawmillService immediately after recording a saw job.
    /// Returns 400 if any of the requested logs is not currently InStock.
    /// </summary>
    [HttpPut("logs/consume")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> ConsumeLogs([FromBody] ConsumeLogsDto dto)
    {
        if (dto.LogIds is null || dto.LogIds.Count == 0)
        {
            return BadRequest(new { message = "LogIds must be a non-empty list." });
        }

        var consumed = await _repository.ConsumeLogsAsync(dto.LogIds);
        if (!consumed)
        {
            return BadRequest(new
            {
                message = "One or more of the requested logs could not be marked Consumed. " +
                          "They may already be Consumed or Removed. No changes were made."
            });
        }

        return Ok(new { message = $"{dto.LogIds.Count} log(s) marked as Consumed successfully." });
    }

    [HttpPost("stock/adjust")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentDto dto)
    {
        var adjustment = new StockAdjustment
        {
            Species = dto.Species.Trim(),
            Grade = dto.Grade.Trim().ToUpperInvariant(),
            AdjustedVolumeM3 = dto.AdjustedVolumeM3,
            Reason = dto.Reason.Trim(),
            AdjustedBy = dto.AdjustedBy
        };

        var updated = await _repository.AdjustStockAsync(adjustment);
        if (!updated)
        {
            return NotFound(new { message = "Species and grade combination not found in current raw stock." });
        }

        return Ok(new { message = "Stock level adjusted successfully." });
    }

    [HttpPut("stock/threshold")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpdateThreshold([FromBody] UpdateThresholdDto dto)
    {
        var updated = await _repository.UpdateThresholdAsync(
            dto.StockId,
            dto.LowStockThreshold
        );

        if (!updated)
        {
            return NotFound(new { message = "Target stock record not found." });
        }

        return Ok(new { message = "Low stock alert threshold updated." });
    }

    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers([FromQuery] bool includeInactive = false)
    {
        var suppliers = await _repository.GetActiveSuppliersAsync(includeInactive);
        return Ok(suppliers);
    }

    [HttpPost("suppliers")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierDto dto)
    {
        var supplier = new Supplier
        {
            SupplierName = dto.Name.Trim(),
            ContactNumber = dto.PhoneNumber.Trim(),
            Address = dto.Address?.Trim(),
            IsActive = true
        };

        var supplierId = await _repository.AddSupplierAsync(supplier);
        return StatusCode(StatusCodes.Status201Created, new { supplierId, message = "Supplier created successfully." });
    }

    [HttpPut("suppliers/{supplierId:int}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeactivateSupplier(int supplierId)
    {
        var deactivated = await _repository.DeactivateSupplierAsync(supplierId);
        if (!deactivated)
        {
            return NotFound(new { message = "Active supplier not found." });
        }

        return Ok(new { message = "Supplier deactivated successfully." });
    }

    [HttpDelete("suppliers/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteSupplier(int id)
    {
        var deactivated = await _repository.DeactivateSupplierAsync(id);
        if (!deactivated)
        {
            return NotFound(new { message = "Active supplier not found." });
        }

        return Ok(new { message = "Supplier marked inactive." });
    }
}