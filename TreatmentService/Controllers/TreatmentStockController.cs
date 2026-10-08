using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TreatmentService.Repositories;

namespace TreatmentService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TreatmentStockController : ControllerBase
{
    private readonly ITreatmentStockRepository _repository;
    private readonly ILogger<TreatmentStockController> _logger;

    public TreatmentStockController(
        ITreatmentStockRepository repository,
        ILogger<TreatmentStockController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Gets all sawn stock currently available for treatment.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetAllStock()
    {
        var stock = await _repository.GetAllStockAsync();
        return Ok(stock);
    }

    /// <summary>
    /// Gets available volume balance for a given species and dimensions.
    /// </summary>
    [HttpGet("{species}/{dimensions}")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetStockBalance(string species, string dimensions)
    {
        if (string.IsNullOrWhiteSpace(species) || string.IsNullOrWhiteSpace(dimensions))
        {
            return BadRequest(new { message = "Species and dimensions are required." });
        }

        var balance = await _repository.GetStockBalanceAsync(species, dimensions);
        return Ok(new
        {
            species,
            dimensions,
            volumeM3 = balance
        });
    }

    /// <summary>
    /// Upsert a low-stock threshold for a treated timber grade (Manager only).
    /// </summary>
    [HttpPut("treated/threshold")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> SetTreatedThreshold([FromBody] SetThresholdDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Species) || string.IsNullOrWhiteSpace(dto.Dimensions) || string.IsNullOrWhiteSpace(dto.ChemicalType))
        {
            return BadRequest(new { message = "Species, dimensions and chemical type are required." });
        }
        if (dto.ThresholdM3 < 0)
        {
            return BadRequest(new { message = "ThresholdM3 cannot be negative." });
        }

        await _repository.SetTreatedStockThresholdAsync(dto.Species, dto.Dimensions, dto.ChemicalType, dto.ThresholdM3);
        return Ok(new { message = "Threshold saved." });
    }

    /// <summary>
    /// Evaluates treated stock against thresholds and returns all active low-stock alerts.
    /// GET /api/TreatmentStock/treated/alerts
    /// </summary>
    [HttpGet("treated/alerts")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetTreatedAlerts()
    {
        var alerts = await _repository.GetTreatedStockAlertsAsync();
        return Ok(alerts);
    }

    /// <summary>
    /// Treated stock listing per species/dimension/chemical type.
    /// GET /api/TreatmentStock/treated
    /// </summary>
    [HttpGet("treated")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetTreatedStock()
    {
        var stock = await _repository.GetTreatedStockAsync();
        return Ok(stock);
    }

    /// <summary>
    /// Same as above but using the card’s available-sawn-stock naming per Treat.2 DoD
    /// (GET /api/TreatmentStock/sawn/availability).
    /// </summary>
    [HttpGet("sawn/availability")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetSawnAvailability()
    {
        var stock = await _repository.GetAllStockAsync();
        return Ok(stock.Select(s => new { s.Species, s.Dimensions, AvailableVolumeM3 = s.VolumeM3 }));
    }

    /// <summary>
    /// Service health endpoint.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new { status = "Healthy", service = "TreatmentService", timestamp = DateTime.UtcNow });
    }
}

public class SetThresholdDto
{
    public string Species { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public string ChemicalType { get; set; } = string.Empty;
    public decimal ThresholdM3 { get; set; }
}
