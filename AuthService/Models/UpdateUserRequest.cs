namespace AuthService.Models;

public class UpdateUserRequest
{
    public string? Role { get; set; }
    public string? Password { get; set; }
}