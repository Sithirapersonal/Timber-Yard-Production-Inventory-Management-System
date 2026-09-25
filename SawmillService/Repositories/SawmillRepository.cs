using MySql.Data.MySqlClient;
using SawmillService.DTOs;
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

        // JobCode is derived from MAX(SawJobId)+1 with no locking, so two concurrent
        // starts can compute the same code; the UNIQUE(JobCode) constraint then rejects
        // the loser with MySQL error 1062 (ER_DUP_ENTRY). Retry with a freshly computed
        // code, bounded, instead of surfacing a 500 (the failed attempt is fully rolled
        // back, so no partial rows survive).
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
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
            catch (MySqlException ex) when (ex.Number == 1062 && attempt < maxAttempts)
            {
                // Duplicate JobCode under concurrency: the failed transaction was fully
                // rolled back (no partial rows). Discard this attempt and retry with a
                // freshly computed JobCode. Any other exception falls through to the
                // catch below and is rethrown immediately.
                await transaction.RollbackAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique JobCode after retries.");
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
                   Notes, Status, StartedBy, StartedAt, MachineId, MachineCode, MachineName,
                   OutputVolumeM3, WastageM3, CompletedAt
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
                    MachineName = reader.IsDBNull(reader.GetOrdinal("MachineName")) ? "" : reader.GetString(reader.GetOrdinal("MachineName")),
                    OutputVolumeM3 = reader.IsDBNull(reader.GetOrdinal("OutputVolumeM3")) ? null : reader.GetDecimal(reader.GetOrdinal("OutputVolumeM3")),
                    WastageM3 = reader.IsDBNull(reader.GetOrdinal("WastageM3")) ? null : reader.GetDecimal(reader.GetOrdinal("WastageM3")),
                    CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt"))
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

    public async Task<SawJob?> GetJobByIdAsync(int sawJobId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT SawJobId, JobCode, StockId, SpeciesName, LengthFt, TotalVolumeM3,
                   Notes, Status, StartedBy, StartedAt, MachineId, MachineCode, MachineName,
                   OutputVolumeM3, WastageM3, CompletedAt
            FROM SawJobs
            WHERE SawJobId = @SawJobId;";

        SawJob? job = null;
        await using (var cmd = new MySqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@SawJobId", sawJobId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                job = new SawJob
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
                    MachineName = reader.IsDBNull(reader.GetOrdinal("MachineName")) ? "" : reader.GetString(reader.GetOrdinal("MachineName")),
                    OutputVolumeM3 = reader.IsDBNull(reader.GetOrdinal("OutputVolumeM3")) ? null : reader.GetDecimal(reader.GetOrdinal("OutputVolumeM3")),
                    WastageM3 = reader.IsDBNull(reader.GetOrdinal("WastageM3")) ? null : reader.GetDecimal(reader.GetOrdinal("WastageM3")),
                    CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt"))
                };
            }
        }

        if (job == null) return null;

        const string workerSql = @"
            SELECT w.FullName, w.EmployeeCode
            FROM SawJobWorkers sjw
            JOIN Workers w ON sjw.WorkerId = w.WorkerId
            WHERE sjw.SawJobId = @SawJobId
            ORDER BY w.FullName ASC;";

        await using (var workerCmd = new MySqlCommand(workerSql, connection))
        {
            workerCmd.Parameters.AddWithValue("@SawJobId", sawJobId);
            await using var workerReader = await workerCmd.ExecuteReaderAsync();
            while (await workerReader.ReadAsync())
            {
                var name = workerReader.GetString(workerReader.GetOrdinal("FullName"));
                var code = workerReader.GetString(workerReader.GetOrdinal("EmployeeCode"));
                job.AssignedWorkerNames.Add($"{name} ({code})");
            }
        }

        return job;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read Job History (Completed + Cancelled only, no limit)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<SawJob>> GetJobHistoryAsync(DateTime? from, DateTime? to)
    {
        var jobs = new List<SawJob>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // "to" is an inclusive calendar day: expand midnight → following midnight
        // so the comparison uses an exclusive upper bound (`<`) and therefore
        // catches every StartedAt timestamp anywhere on that day — the same
        // convention as GetWastageYieldReportAsync, but on StartedAt rather than
        // CompletedAt (Job History is filtered by when a job STARTED).
        var toExclusive = to.HasValue ? to.Value.Date.AddDays(1) : (DateTime?)null;

        // Fetch job rows. InProgress jobs are excluded unconditionally — history
        // only ever shows finished jobs; active ones stay on the Recently Started
        // Jobs list until they resolve. No LIMIT: every matching row is returned.
        var sql = @"
            SELECT SawJobId, JobCode, StockId, SpeciesName, LengthFt, TotalVolumeM3,
                   Notes, Status, StartedBy, StartedAt, MachineId, MachineCode, MachineName,
                   OutputVolumeM3, WastageM3, CompletedAt
            FROM SawJobs
            WHERE Status IN ('Completed', 'Cancelled')";

        // Both bounds optional — omit either for an unbounded side, omit both
        // for every finished job.
        if (from.HasValue) sql += "\n            AND StartedAt >= @From";
        if (toExclusive.HasValue) sql += "\n            AND StartedAt < @ToExclusive";

        sql += "\n            ORDER BY StartedAt DESC;";

        await using (var cmd = new MySqlCommand(sql, connection))
        {
            if (from.HasValue) cmd.Parameters.AddWithValue("@From", from.Value.Date);
            if (toExclusive.HasValue) cmd.Parameters.AddWithValue("@ToExclusive", toExclusive.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
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
                    MachineName = reader.IsDBNull(reader.GetOrdinal("MachineName")) ? "" : reader.GetString(reader.GetOrdinal("MachineName")),
                    OutputVolumeM3 = reader.IsDBNull(reader.GetOrdinal("OutputVolumeM3")) ? null : reader.GetDecimal(reader.GetOrdinal("OutputVolumeM3")),
                    WastageM3 = reader.IsDBNull(reader.GetOrdinal("WastageM3")) ? null : reader.GetDecimal(reader.GetOrdinal("WastageM3")),
                    CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt"))
                });
            }
        }
        // The block above ensures `reader` and `cmd` are fully disposed (and the
        // reader closed) before the second query below runs on this same
        // connection — same discipline and rationale as GetRecentJobsAsync.

        if (jobs.Count == 0) return jobs;

        // Fetch worker names (with employee codes) for each job in a single query
        // — same batched pattern as GetRecentJobsAsync.
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

    public async Task<bool> CompleteSawJobAsync(int sawJobId, decimal outputVolumeM3, decimal wastageM3)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE SawJobs
            SET Status = 'Completed', OutputVolumeM3 = @OutputVolumeM3, WastageM3 = @WastageM3,
                CompletedAt = UTC_TIMESTAMP()
            WHERE SawJobId = @SawJobId AND Status = 'InProgress';";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SawJobId", sawJobId);
        cmd.Parameters.AddWithValue("@OutputVolumeM3", outputVolumeM3);
        cmd.Parameters.AddWithValue("@WastageM3", wastageM3);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RevertToInProgressAsync(int sawJobId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE SawJobs
            SET Status = 'InProgress', OutputVolumeM3 = NULL, WastageM3 = NULL, CompletedAt = NULL
            WHERE SawJobId = @SawJobId AND Status IN ('Completed', 'Cancelled');";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SawJobId", sawJobId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Wastage & Yield Report (Admin/Manager analytics — completed jobs only)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WastageYieldReportDto> GetWastageYieldReportAsync(DateTime? from, DateTime? to)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // "to" is an inclusive calendar day: expand midnight → following midnight
        // so the comparison uses an exclusive upper bound (`<`) and therefore
        // catches every timestamp completed anywhere on that day.
        var toExclusive = to.HasValue ? to.Value.Date.AddDays(1) : (DateTime?)null;

        var sql = @"
            SELECT JobCode, SpeciesName, CompletedAt, TotalVolumeM3,
                   COALESCE(OutputVolumeM3, 0) AS YieldVolumeM3,
                   COALESCE(WastageM3, 0)      AS WastageVolumeM3
            FROM SawJobs
            WHERE Status = 'Completed' AND CompletedAt IS NOT NULL";

        // Both bounds optional — omit either for an unbounded side, omit both
        // for every completed job.
        if (from.HasValue) sql += "\n            AND CompletedAt >= @From";
        if (toExclusive.HasValue) sql += "\n            AND CompletedAt < @ToExclusive";

        sql += "\n            ORDER BY CompletedAt DESC;";

        var jobs = new List<JobWastageYieldDto>();
        await using (var cmd = new MySqlCommand(sql, connection))
        {
            if (from.HasValue) cmd.Parameters.AddWithValue("@From", from.Value.Date);
            if (toExclusive.HasValue) cmd.Parameters.AddWithValue("@ToExclusive", toExclusive.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var inputVolumeM3 = reader.GetDecimal(reader.GetOrdinal("TotalVolumeM3"));
                var yieldVolumeM3 = reader.GetDecimal(reader.GetOrdinal("YieldVolumeM3"));
                var wastageVolumeM3 = reader.GetDecimal(reader.GetOrdinal("WastageVolumeM3"));

                jobs.Add(new JobWastageYieldDto
                {
                    JobCode = reader.GetString(reader.GetOrdinal("JobCode")),
                    SpeciesName = reader.GetString(reader.GetOrdinal("SpeciesName")),
                    CompletedAt = reader.GetDateTime(reader.GetOrdinal("CompletedAt")),
                    InputVolumeM3 = inputVolumeM3,
                    YieldVolumeM3 = yieldVolumeM3,
                    WastageVolumeM3 = wastageVolumeM3,
                    WastagePercentage = RatioPercentage(wastageVolumeM3, inputVolumeM3)
                });
            }
        }

        // ── Overall totals (all percentages computed server-side) ─────────────
        var totals = new WastageYieldTotalsDto
        {
            CompletedJobCount = jobs.Count,
            TotalInputVolumeM3 = jobs.Sum(j => j.InputVolumeM3),
            TotalYieldVolumeM3 = jobs.Sum(j => j.YieldVolumeM3),
            TotalWastageVolumeM3 = jobs.Sum(j => j.WastageVolumeM3)
        };
        totals.RecoveryPercentage = RatioPercentage(totals.TotalYieldVolumeM3, totals.TotalInputVolumeM3);
        totals.WastagePercentage = RatioPercentage(totals.TotalWastageVolumeM3, totals.TotalInputVolumeM3);

        // ── Per-species breakdown, largest contributor first ──────────────────
        var speciesBreakdown = jobs
            .GroupBy(j => j.SpeciesName)
            .Select(g => new SpeciesWastageYieldDto
            {
                SpeciesName = g.Key,
                CompletedJobCount = g.Count(),
                InputVolumeM3 = g.Sum(j => j.InputVolumeM3),
                YieldVolumeM3 = g.Sum(j => j.YieldVolumeM3),
                WastageVolumeM3 = g.Sum(j => j.WastageVolumeM3),
                RecoveryPercentage = RatioPercentage(g.Sum(j => j.YieldVolumeM3), g.Sum(j => j.InputVolumeM3))
            })
            .OrderByDescending(s => s.InputVolumeM3)
            .ThenBy(s => s.SpeciesName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WastageYieldReportDto
        {
            Totals = totals,
            SpeciesBreakdown = speciesBreakdown,
            Jobs = jobs
        };
    }

    public async Task<bool> IsMachineInUseAsync(int machineId, int excludeSawJobId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT COUNT(*)
            FROM SawJobs
            WHERE MachineId = @MachineId AND Status = 'InProgress' AND SawJobId != @ExcludeSawJobId;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MachineId", machineId);
        cmd.Parameters.AddWithValue("@ExcludeSawJobId", excludeSawJobId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Log allocations
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the logs allocated to a job (SawJobLogs rows). Used after cancelling
    /// a job so a raw-stock-reversed event can be published listing every LogId that
    /// LogIntakeService should flip back to InStock. LogId is a value-only reference
    /// to LogIntakeService's Logs table — no cross-DB FK.
    /// </summary>
    public async Task<IEnumerable<SawJobLogAllocation>> GetLogAllocationsForJobAsync(int sawJobId)
    {
        var allocations = new List<SawJobLogAllocation>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT SawJobLogId, SawJobId, LogId, VolumeM3
            FROM SawJobLogs
            WHERE SawJobId = @SawJobId
            ORDER BY SawJobLogId;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SawJobId", sawJobId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            allocations.Add(MapSawJobLogAllocation(reader));
        }
        return allocations;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cancel Job (Admin, Manager, Supervisor — enforced by the controller).
    // Marks an InProgress job Cancelled. After the DB cancel the controller
    // publishes a raw-stock-reversed event (fire-and-forget) so LogIntakeService
    // can flip the job's allocated logs back to InStock. Because the reversal is
    // asynchronous there is a short window where the logs may still show Consumed,
    // and a Kafka publish failure means they stay Consumed — an accepted tradeoff.
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

    /// <summary>
    /// part/whole expressed as a percentage rounded to 1 decimal place
    /// (AwayFromZero, matching the service's other rounding). Returns 0 when
    /// whole is &lt;= 0 so an empty report yields clean zeros instead of
    /// NaN/Infinity — also guards per-row division by zero.
    /// </summary>
    private static decimal RatioPercentage(decimal part, decimal whole)
        => whole > 0m ? Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

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

    private static SawJobLogAllocation MapSawJobLogAllocation(System.Data.Common.DbDataReader reader) => new()
    {
        SawJobLogId = reader.GetInt32(reader.GetOrdinal("SawJobLogId")),
        SawJobId = reader.GetInt32(reader.GetOrdinal("SawJobId")),
        LogId = reader.GetInt32(reader.GetOrdinal("LogId")),
        VolumeM3 = reader.GetDecimal(reader.GetOrdinal("VolumeM3"))
    };
}
