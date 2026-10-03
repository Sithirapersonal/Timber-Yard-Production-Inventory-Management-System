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
