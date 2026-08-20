using MySqlConnector;
using AuthService.Models;

namespace AuthService.Repositories;

public class LoginHistoryRepository
{
    private readonly string _connectionString;

    public LoginHistoryRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task LogAttemptAsync(string username, bool success, string? ipAddress)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"INSERT INTO LoginHistory (Username, Success, IpAddress)
                              VALUES (@Username, @Success, @IpAddress)";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@Success", success);
        command.Parameters.AddWithValue("@IpAddress", (object?)ipAddress ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<LoginHistoryEntry>> GetAllAsync()
    {
        var results = new List<LoginHistoryEntry>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"SELECT LoginHistoryId, Username, Success, AttemptedAt, IpAddress
                              FROM LoginHistory
                              ORDER BY AttemptedAt DESC";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new LoginHistoryEntry
            {
                LoginHistoryId = reader.GetInt32("LoginHistoryId"),
                Username = reader.GetString("Username"),
                Success = reader.GetBoolean("Success"),
                AttemptedAt = reader.GetDateTime("AttemptedAt"),
                IpAddress = reader.IsDBNull(reader.GetOrdinal("IpAddress")) ? null : reader.GetString("IpAddress")
            });
        }

        return results;
    }
}