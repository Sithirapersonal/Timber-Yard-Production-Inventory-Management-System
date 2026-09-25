namespace SawmillService.DTOs;

/// <summary>DTO for the consume-logs request sent to LogIntakeService.</summary>
public class ConsumeLogsRequestDto
{
    public List<int> LogIds { get; set; } = new();
}
