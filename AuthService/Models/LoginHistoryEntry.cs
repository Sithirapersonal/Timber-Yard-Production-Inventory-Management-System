namespace AuthService.Models;

public class LoginHistoryEntry
{
    public int LoginHistoryId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime AttemptedAt { get; set; }
    public string? IpAddress { get; set; }
}