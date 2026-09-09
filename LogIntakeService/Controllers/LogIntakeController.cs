using Microsoft.AspNetCore.Mvc;
using LogIntakeService.DTOs;
using LogIntakeService.Models;
using LogIntakeService.Repositories;

namespace LogIntakeService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogIntakeController : ControllerBase
{
    private readonly ILogIntakeRepository _repository;

    public LogIntakeController(ILogIntakeRepository repository)
    {
        _repository = repository;
    }

    [HttpPost("deliveries")]
    public async Task<IActionResult> RecordDelivery([FromBody] CreateDeliveryDto dto)
    {
        var delivery = new TimberDelivery
        {
            SupplierId = dto.SupplierId,
            Species = dto.Species.Trim(),
            Grade = dto.Grade.Trim().ToUpperInvariant(),
            VolumeM3 = dto.VolumeM3,
            VehicleNumber = dto.VehicleNumber?.Trim(),
            ReceivedBy = dto.ReceivedBy
        };

        var deliveryId = await _repository.RecordDeliveryAsync(delivery);
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

    [HttpPost("stock/adjust")]
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
    public async Task<IActionResult> UpdateThreshold([FromBody] UpdateThresholdDto dto)
    {
        var updated = await _repository.UpdateThresholdAsync(
            dto.Species.Trim(),
            dto.Grade.Trim().ToUpperInvariant(),
            dto.LowStockThreshold
        );

        if (!updated)
        {
            return NotFound(new { message = "Target stock record not found." });
        }

        return Ok(new { message = "Low stock alert threshold updated." });
    }

    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers()
    {
        var suppliers = await _repository.GetActiveSuppliersAsync();
        return Ok(suppliers);
    }

    [HttpDelete("suppliers/{id:int}")]
    public async Task<IActionResult> DeactivateSupplier(int id)
    {
        var deactivated = await _repository.DeactivateSupplierAsync(id);
        if (!deactivated)
        {
            return NotFound(new { message = "Active supplier not found." });
        }

        return Ok(new { message = "Supplier marked inactive." });
    }
}