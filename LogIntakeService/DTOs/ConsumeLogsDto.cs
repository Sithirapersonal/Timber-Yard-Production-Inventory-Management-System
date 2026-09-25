namespace LogIntakeService.DTOs;

/// <summary>
/// Payload for PUT api/LogIntake/logs/consume.
/// Called by SawmillService to mark a batch of InStock logs as Consumed
/// after a saw job has been successfully recorded.
/// </summary>
public class ConsumeLogsDto
{
    public List<int> LogIds { get; set; } = new();
}
