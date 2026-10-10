using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TreatmentService.Repositories;

namespace TreatmentService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TanksController : ControllerBase
{
    private readonly ITreatmentStockRepository _repository;

    public TanksController(ITreatmentStockRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Tank schedule: each tank's live status (Idle/Busy) and its current batch.
    /// </summary>
    [HttpGet("availability")]
    [Authorize(Roles = "Admin,Manager,Supervisor")]
    public async Task<IActionResult> GetAvailability()
    {
        var tanks = await _repository.GetTanksAvailabilityAsync();
        return Ok(tanks);
    }
}
