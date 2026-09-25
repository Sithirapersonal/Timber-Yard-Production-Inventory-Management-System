namespace SawmillService.DTOs;

/// <summary>
/// Body for PUT api/Sawmill/jobs/{id}/cancel.
/// Reason is optional — when omitted/empty the server defaults it to
/// "No reason provided" before publishing the raw-stock-reversed event,
/// so the existing frontend (which sends no body today) keeps working.
/// </summary>
public class CancelJobDto
{
    public string? Reason { get; set; }
}