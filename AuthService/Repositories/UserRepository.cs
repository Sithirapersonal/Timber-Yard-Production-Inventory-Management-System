using MySqlConnector;
using AuthService.Models;

namespace AuthService.Repositories;

public class UserRepository
{
    private readonly string _connectionString;

    public UserRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT UserId, Username, PasswordHash, Role FROM Users WHERE Username = @Username LIMIT 1";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username);

        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new User
            {
                UserId = reader.GetInt32("UserId"),
                Username = reader.GetString("Username"),
                PasswordHash = reader.GetString("PasswordHash"),
                Role = reader.GetString("Role")
            };
        }

        return null;
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM Users WHERE Username = @Username";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        return count > 0;
    }

    public async Task<int> CreateUserAsync(string username, string passwordHash, string role)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"INSERT INTO Users (Username, PasswordHash, Role)
                              VALUES (@Username, @PasswordHash, @Role);
                              SELECT LAST_INSERT_ID();";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@Role", role);

        var newId = await command.ExecuteScalarAsync();
        return Convert.ToInt32(newId);
    }
    public async Task<List<User>> GetAllAsync()
    {
        var results = new List<User>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT UserId, Username, PasswordHash, Role FROM Users ORDER BY UserId";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new User
            {
                UserId = reader.GetInt32("UserId"),
                Username = reader.GetString("Username"),
                PasswordHash = reader.GetString("PasswordHash"),
                Role = reader.GetString("Role")
            });
        }

        return results;
    }
    public async Task<bool> UpdateRoleAsync(int userId, string role)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "UPDATE Users SET Role = @Role WHERE UserId = @UserId";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Role", role);
        command.Parameters.AddWithValue("@UserId", userId);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> UpdatePasswordAsync(int userId, string passwordHash)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE UserId = @UserId";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@UserId", userId);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> UserIdExistsAsync(int userId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM Users WHERE UserId = @UserId";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        return count > 0;
    }
    public async Task<bool> DeleteUserAsync(int userId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "DELETE FROM Users WHERE UserId = @UserId";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
}