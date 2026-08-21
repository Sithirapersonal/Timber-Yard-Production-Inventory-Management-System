using Microsoft.AspNetCore.Mvc;
using AuthService.Models;
using AuthService.Repositories;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;

namespace AuthService.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly UserRepository _userRepository;
    private readonly JwtService _jwtService;
    private readonly LoginHistoryRepository _loginHistoryRepository;

    public AuthController(UserRepository userRepository, JwtService jwtService, LoginHistoryRepository loginHistoryRepository)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
        _loginHistoryRepository = loginHistoryRepository;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required." });
        }

        var user = await _userRepository.GetByUsernameAsync(request.Username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await _loginHistoryRepository.LogAttemptAsync(request.Username, false, ip);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        await _loginHistoryRepository.LogAttemptAsync(request.Username, true, ip);

        var token = _jwtService.GenerateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            Username = user.Username,
            Role = user.Role
        });
    }

    [HttpGet("admin-only")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnlyTest()
    {
        var username = User.Identity?.Name;
        return Ok(new { message = $"Hello Admin (user id: {username}). You are authorized." });
    }

    [HttpPost("users")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(new { message = "Username, password, and role are required." });
        }

        var validRoles = new[] { "Supervisor", "Manager", "Admin" };
        if (!validRoles.Contains(request.Role))
        {
            return BadRequest(new { message = "Role must be one of: Supervisor, Manager, Admin." });
        }

        if (await _userRepository.UsernameExistsAsync(request.Username))
        {
            return Conflict(new { message = "Username already exists." });
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var newUserId = await _userRepository.CreateUserAsync(request.Username, passwordHash, request.Role);

        return Ok(new { userId = newUserId, username = request.Username, role = request.Role });
    }
    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _userRepository.GetAllAsync();

        var response = users.Select(u => new
        {
            userId = u.UserId,
            username = u.Username,
            role = u.Role
        });

        return Ok(response);
    }

    [HttpGet("login-history")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetLoginHistory()
    {
        var history = await _loginHistoryRepository.GetAllAsync();
        return Ok(history);
    }
    [HttpPut("users/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        if (!await _userRepository.UserIdExistsAsync(id))
        {
            return NotFound(new { message = "User not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Role) && string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Provide a role and/or a password to update." });
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var validRoles = new[] { "Supervisor", "Manager", "Admin" };
            if (!validRoles.Contains(request.Role))
            {
                return BadRequest(new { message = "Role must be one of: Supervisor, Manager, Admin." });
            }
            await _userRepository.UpdateRoleAsync(id, request.Role);
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var newHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            await _userRepository.UpdatePasswordAsync(id, newHash);
        }

        return Ok(new { message = "User updated successfully." });
    }
    [HttpDelete("users/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!await _userRepository.UserIdExistsAsync(id))
        {
            return NotFound(new { message = "User not found." });
        }

        var currentUserIdClaim = User.FindFirst("userId")?.Value;
        if (currentUserIdClaim == id.ToString())
        {
            return BadRequest(new { message = "You cannot delete your own account." });
        }

        await _userRepository.DeleteUserAsync(id);
        return Ok(new { message = "User deleted successfully." });
    }
}