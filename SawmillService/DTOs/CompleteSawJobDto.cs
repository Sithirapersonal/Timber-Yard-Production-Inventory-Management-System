namespace SawmillService.DTOs;

public class BoardRowDto
{
    public decimal LengthFt { get; set; }
    public decimal WidthIn { get; set; }
    public decimal ThicknessIn { get; set; }
    public int Quantity { get; set; }
}

public class CompleteSawJobDto
{
    public List<BoardRowDto> Boards { get; set; } = new();
    public bool AcknowledgedDeviationWarning { get; set; } = false;
}
