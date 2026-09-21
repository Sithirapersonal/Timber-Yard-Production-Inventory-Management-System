using MySql.Data.MySqlClient;
using SawmillService.Models;

namespace SawmillService.Repositories;

public class SawmillRepository : ISawmillRepository
{
    private readonly string _connectionString;

    public SawmillRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SawmillDb")
            ?? throw new InvalidOperationException("SawmillDb connection string is not configured.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Workers
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<Worker>> GetActiveWorkersAsync(string? searchQuery = null)
    {
        var workers = new List<Worker>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT WorkerId, EmployeeCode, FullName, JobRole, IsActive, CreatedAt
            FROM Workers
            WHERE IsActive = TRUE";

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            sql += " AND (FullName LIKE @q OR EmployeeCode LIKE @q)";
        }

        sql += " ORDER BY FullName ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            cmd.Parameters.AddWithValue("@q", $"%{searchQuery.Trim()}%");
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            workers.Add(MapWorker(reader));
        }
        return workers;
    }

    public async Task<IEnumerable<Worker>> GetWorkersByIdsAsync(IEnumerable<int> workerIds)
    {
        var idList = workerIds.ToList();
        if (idList.Count == 0) return Enumerable.Empty<Worker>();

        var workers = new List<Worker>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Build parameterized IN clause
        var paramNames = idList.Select((_, i) => $"@wid{i}").ToArray();
        var sql = $@"
            SELECT WorkerId, EmployeeCode, FullName, JobRole, IsActive, CreatedAt
            FROM Workers
            WHERE WorkerId IN ({string.Join(",", paramNames)})
            ORDER BY FullName ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        for (int i = 0; i < idList.Count; i++)
        {
            cmd.Parameters.AddWithValue(paramNames[i], idList[i]);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            workers.Add(MapWorker(reader));
        }
        return workers;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Machines
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<Machine>> GetMachinesAsync(string? searchQuery = null)
    {
        var machines = new List<Machine>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT MachineId, MachineCode, Name, Status, CreatedAt FROM Machines";
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            sql += " WHERE (Name LIKE @q OR MachineCode LIKE @q)";
        }
        sql += " ORDER BY MachineCode ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            cmd.Parameters.AddWithValue("@q", $"%{searchQuery.Trim()}%");
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            machines.Add(MapMachine(reader));
        }
        return machines;
    }

    public async Task<Machine?> GetMachineByIdAsync(int machineId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT MachineId, MachineCode, Name, Status, CreatedAt FROM Machines WHERE MachineId = @MachineId;";
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MachineId", machineId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapMachine(reader);
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Job Code Generation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the next sequential job code in SAW-001 format.
    /// Reads MAX(SawJobId) inside an open transaction to avoid races.
    /// Called within CreateSawJobAsync's transaction.
    /// </summary>
    private static async Task<string> GenerateNextJobCodeInternalAsync(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        const string sql = "SELECT COALESCE(MAX(SawJobId), 0) FROM SawJobs;";
        await using var cmd = new MySqlCommand(sql, connection, transaction);
        var maxId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        // The next inserted row will get maxId+1 (AUTO_INCREMENT), so pad that
        return $"SAW-{(maxId + 1):D3}";
    }

    /// <summary>Exposed for unit-testing the padding logic (public test hook).</summary>
    public async Task<string> GenerateNextJobCodeAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var code = await GenerateNextJobCodeInternalAsync(connection, (MySqlTransaction)tx);
        await tx.RollbackAsync(); // Read-only — no commit needed
        return code;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create Saw Job (multi-table transaction: SawJobs + SawJobLogs + SawJobWorkers)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<SawJob> CreateSawJobAsync(
        int stockId,
        string speciesName,
        decimal lengthFt,
        decimal totalVolumeM3,
        string? notes,
        int startedBy,
        IEnumerable<(int LogId, decimal VolumeM3)> logs,
        IEnumerable<int> workerIds,
        int machineId,
        string machineCode,
        string machineName)
    {
        var logList = logs.ToList();
        var workerList = workerIds.ToList();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var jobCode = await GenerateNextJobCodeInternalAsync(connection, (MySqlTransaction)transaction);

            // 1. Insert SawJob
            const string jobSql = @"
                INSERT INTO SawJobs (JobCode, StockId, SpeciesName, LengthFt, TotalVolumeM3, Notes, Status, StartedBy, StartedAt, MachineId, MachineCode, MachineName)
                VALUES (@JobCode, @StockId, @SpeciesName, @LengthFt, @TotalVolumeM3, @Notes, 'InProgress', @StartedBy, UTC_TIMESTAMP(), @MachineId, @MachineCode, @MachineName);
                SELECT LAST_INSERT_ID();";

            int sawJobId;
            await using (var jobCmd = new MySqlCommand(jobSql, connection, (MySqlTransaction)transaction))
            {
                jobCmd.Parameters.AddWithValue("@JobCode", jobCode);
                jobCmd.Parameters.AddWithValue("@StockId", stockId);
                jobCmd.Parameters.AddWithValue("@SpeciesName", speciesName);
                jobCmd.Parameters.AddWithValue("@LengthFt", lengthFt);
                jobCmd.Parameters.AddWithValue("@TotalVolumeM3", totalVolumeM3);
                jobCmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                jobCmd.Parameters.AddWithValue("@StartedBy", startedBy);
                jobCmd.Parameters.AddWithValue("@MachineId", machineId);
                jobCmd.Parameters.AddWithValue("@MachineCode", machineCode);
                jobCmd.Parameters.AddWithValue("@MachineName", machineName);
                sawJobId = Convert.ToInt32(await jobCmd.ExecuteScalarAsync());
            }

            // 2. Insert SawJobLogs rows (one per allocated log)
            const string logSql = @"
                INSERT INTO SawJobLogs (SawJobId, LogId, VolumeM3)
                VALUES (@SawJobId, @LogId, @VolumeM3);";

            foreach (var (logId, volumeM3) in logList)
            {
                await using var logCmd = new MySqlCommand(logSql, connection, (MySqlTransaction)transaction);
                logCmd.Parameters.AddWithValue("@SawJobId", sawJobId);
                logCmd.Parameters.AddWithValue("@LogId", logId);
                logCmd.Parameters.AddWithValue("@VolumeM3", volumeM3);
                await logCmd.ExecuteNonQueryAsync();
            }

            // 3. Insert SawJobWorkers rows (many-to-many)
            const string workerSql = @"
                INSERT INTO SawJobWorkers (SawJobId, WorkerId)
                VALUES (@SawJobId, @WorkerId);";

            foreach (var workerId in workerList)
            {
                await using var workerCmd = new MySqlCommand(workerSql, connection, (MySqlTransaction)transaction);
                workerCmd.Parameters.AddWithValue("@SawJobId", sawJobId);
                workerCmd.Parameters.AddWithValue("@WorkerId", workerId);
                await workerCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            // Return a populated SawJob object to the controller
            return new SawJob
            {
                SawJobId = sawJobId,
                JobCode = jobCode,
                StockId = stockId,
                SpeciesName = speciesName,
                LengthFt = lengthFt,
                TotalVolumeM3 = totalVolumeM3,
                Notes = notes,
                Status = "InProgress",
                StartedBy = startedBy,
                StartedAt = DateTime.UtcNow,
                MachineId = machineId,
                MachineCode = machineCode,
                MachineName = machineName,
                AllocatedLogs = logList.Select(l => new SawJobLogAllocation
                {
                    SawJobId = sawJobId,
                    LogId = l.LogId,
                    VolumeM3 = l.VolumeM3
                }).ToList()
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read Recent Jobs
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<SawJob>> GetRecentJobsAsync(int limit = 20)
    {
        var jobs = new List<SawJob>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Fetch job rows
        var sql = $@"
            SELECT SawJobId, JobCode, StockId, SpeciesName, LengthFt, TotalVolumeM3,
                   Notes, Status, StartedBy, StartedAt, MachineId, MachineCode, MachineName
            FROM SawJobs
            ORDER BY StartedAt DESC
            LIMIT {limit};";

        await using (var cmd = new MySqlCommand(sql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                jobs.Add(new SawJob
                {
                    SawJobId = reader.GetInt32(reader.GetOrdinal("SawJobId")),
                    JobCode = reader.GetString(reader.GetOrdinal("JobCode")),
                    StockId = reader.GetInt32(reader.GetOrdinal("StockId")),
                    SpeciesName = reader.GetString(reader.GetOrdinal("SpeciesName")),
                    LengthFt = reader.GetDecimal(reader.GetOrdinal("LengthFt")),
                    TotalVolumeM3 = reader.GetDecimal(reader.GetOrdinal("TotalVolumeM3")),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    StartedBy = reader.GetInt32(reader.GetOrdinal("StartedBy")),
                    StartedAt = reader.GetDateTime(reader.GetOrdinal("StartedAt")),
                    MachineId = reader.IsDBNull(reader.GetOrdinal("MachineId")) ? 0 : reader.GetInt32(reader.GetOrdinal("MachineId")),
                    MachineCode = reader.IsDBNull(reader.GetOrdinal("MachineCode")) ? "" : reader.GetString(reader.GetOrdinal("MachineCode")),
                    MachineName = reader.IsDBNull(reader.GetOrdinal("MachineName")) ? "" : reader.GetString(reader.GetOrdinal("MachineName"))
                });
            }
        }
        // The block above ensures `reader` and `cmd` are fully disposed (and the
        // reader closed) before we open a second reader on this same connection
        // below. MySqlConnector/MySql.Data only permits one open DataReader per
        // connection at a time by default (MultipleActiveResultSets is not
        // enabled), so the previous version — which relied on method-scoped
        // `await using` — kept this reader open until the method returned,
        // causing "There is already an open DataReader..." as soon as a second
        // query ran on the same connection.

        if (jobs.Count == 0) return jobs;

        // Fetch worker names (with employee codes) for each job in a single query
        var jobIds = jobs.Select(j => j.SawJobId).ToList();
        var workerParamNames = jobIds.Select((_, i) => $"@jid{i}").ToArray();
        var workerSql = $@"
            SELECT sjw.SawJobId, w.FullName, w.EmployeeCode
            FROM SawJobWorkers sjw
            JOIN Workers w ON sjw.WorkerId = w.WorkerId
            WHERE sjw.SawJobId IN ({string.Join(",", workerParamNames)})
            ORDER BY w.FullName ASC;";

        var workerMap = new Dictionary<int, List<string>>();
        await using (var workerCmd = new MySqlCommand(workerSql, connection))
        {
            for (int i = 0; i < jobIds.Count; i++)
            {
                workerCmd.Parameters.AddWithValue(workerParamNames[i], jobIds[i]);
            }

            await using var workerReader = await workerCmd.ExecuteReaderAsync();
            while (await workerReader.ReadAsync())
            {
                var jId = workerReader.GetInt32(workerReader.GetOrdinal("SawJobId"));
                var name = workerReader.GetString(workerReader.GetOrdinal("FullName"));
                var code = workerReader.GetString(workerReader.GetOrdinal("EmployeeCode"));
                if (!workerMap.ContainsKey(jId)) workerMap[jId] = new List<string>();
                workerMap[jId].Add($"{name} ({code})");
            }
        }

        foreach (var job in jobs)
        {
            if (workerMap.TryGetValue(job.SawJobId, out var names))
                job.AssignedWorkerNames = names;
        }

        return jobs;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cancel Job (Admin only)
    // Decision: logs are NOT reverted to InStock on cancellation.
    // Rationale: once logs enter the sawmill, they have been physically moved
    // on the shop floor; reverting them to InStock could create a phantom count
    // for stock that is no longer in the yard or is in an indeterminate state.
    // An Admin who genuinely wants to restore a log should use LogIntakeService
    // directly (future RestoreLog endpoint). This matches the principle of
    // keeping SawmillService's cancel as a status flag only.
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> CancelJobAsync(int sawJobId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE SawJobs
            SET Status = 'Cancelled'
            WHERE SawJobId = @SawJobId AND Status = 'InProgress';";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SawJobId", sawJobId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Worker MapWorker(System.Data.Common.DbDataReader reader) => new()
    {
        WorkerId = reader.GetInt32(reader.GetOrdinal("WorkerId")),
        EmployeeCode = reader.GetString(reader.GetOrdinal("EmployeeCode")),
        FullName = reader.GetString(reader.GetOrdinal("FullName")),
        JobRole = reader.GetString(reader.GetOrdinal("JobRole")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
    };

    private static Machine MapMachine(System.Data.Common.DbDataReader reader) => new()
    {
        MachineId = reader.GetInt32(reader.GetOrdinal("MachineId")),
        MachineCode = reader.GetString(reader.GetOrdinal("MachineCode")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Status = reader.GetString(reader.GetOrdinal("Status")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
    };
}
