using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TreatmentService.Repositories;

namespace TreatmentService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TreatmentBatchesController : ControllerBase
{
    private readonly ITreatmentStockRepository _repository;

    public TreatmentBatchesController(ITreatmentStockRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Creates a treatment batch from a species/dimensions of sawn stock.
    /// Deducts the requested quantity from sawn stock and logs the movement.
    /// Status starts as "Pending".
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateTreatmentBatchDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Species) || string.IsNullOrWhiteSpace(dto.Dimensions) || string.IsNullOrWhiteSpace(dto.ChemicalType))
        {
            return BadRequest(new { message = "Species, dimensions and chemical type are required." });
        }
        if (dto.QuantityM3 <= 0)
        {
            return BadRequest(new { message = "QuantityM3 must be greater than zero." });
        }

        try
        {
            var batchId = await _repository.CreateBatchAsync(dto.Species, dto.Dimensions, dto.ChemicalType, dto.QuantityM3);
            return StatusCode(StatusCodes.Status201Created, new { batchId, message = "Treatment batch created with status Pending." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Assigns a pending batch to a tank and starts treatment.
    /// </summary>
    [HttpPut("{id:int}/start")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> StartBatch(int id, [FromBody] StartBatchDto dto)
    {
        if (dto.TankId <= 0)
        {
            return BadRequest(new { message = "TankId is required." });
        }

        try
        {
            await _repository.StartBatchAsync(id, dto.TankId);
            return Ok(new { message = "Batch started. Status: InTreatment." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cancels a Pending treatment batch (Admin only). Restores the allocated sawn stock,
    /// logs a TREATMENT_CANCELLED movement, and stores the cancellation reason.
    /// </summary>
    [HttpPut("{id:int}/cancel")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CancelBatch(int id, [FromBody] CancelBatchDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "Cancellation reason is required." });
        }

        try
        {
            await _repository.CancelBatchAsync(id, dto.Reason);
            return Ok(new { message = "Batch cancelled. Sawn stock restored." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Marks an InTreatment batch complete, records treated/rejected quantities,
    /// credits treated stock, and logs a stock movement.
    /// </summary>
    [HttpPut("{id:int}/complete")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> CompleteBatch(int id, [FromBody] CompleteBatchDto dto)
    {
        if (dto.TreatedM3 < 0 || dto.RejectedM3 < 0)
        {
            return BadRequest(new { message = "Treated and rejected quantities cannot be negative." });
        }

        try
        {
            await _repository.CompleteBatchAsync(id, dto.TreatedM3, dto.RejectedM3);
            return Ok(new { message = "Batch completed. Treated stock credited." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lists treatment batches, optionally filtered by ?status=.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetBatches([FromQuery] string? status = null)
    {
        var batches = await _repository.GetBatchesAsync(status);
        return Ok(batches);
    }

    /// <summary>
    /// Full detail of a single batch — includes tank and cancellation reason where applicable.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetBatch(int id)
    {
        var batch = await _repository.GetBatchByIdAsync(id);
        if (batch == null)
        {
            return NotFound(new { message = "Batch not found." });
        }
        return Ok(batch);
    }
}

public class CreateTreatmentBatchDto
{
    public string Species { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public string ChemicalType { get; set; } = string.Empty;
    public decimal QuantityM3 { get; set; }
}

public class StartBatchDto
{
    public int TankId { get; set; }
}

public class CompleteBatchDto
{
    public decimal TreatedM3 { get; set; }
    public decimal RejectedM3 { get; set; }
}

public class CancelBatchDto
{
    public string Reason { get; set; } = string.Empty;
}
