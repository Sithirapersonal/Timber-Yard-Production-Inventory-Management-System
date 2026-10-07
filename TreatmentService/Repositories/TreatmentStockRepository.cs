using MySql.Data.MySqlClient;
using TreatmentService.Events;
using TreatmentService.Models;

namespace TreatmentService.Repositories;

public class TreatmentStockRepository : ITreatmentStockRepository
{
    private readonly string _connectionString;

    public TreatmentStockRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TreatmentDb")
            ?? configuration["ConnectionStrings:TreatmentDb"]
            ?? "Server=localhost;Database=TreatmentDB;User=root;Password=;";
    }

    public async Task<bool> CreditSawnStockAsync(SawnStockCreditedEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.EventId))
        {
            throw new ArgumentException("EventId must not be empty.", nameof(evt));
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. Check if event was already processed (Idempotency check)
            const string checkEventSql = "SELECT COUNT(1) FROM ProcessedEvents WHERE EventId = @EventId;";
            await using (var checkCmd = new MySqlCommand(checkEventSql, connection, transaction))
            {
                checkCmd.Parameters.AddWithValue("@EventId", evt.EventId);
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count > 0)
                {
                    // Already processed: rollback and return false (idempotent skip)
                    await transaction.RollbackAsync();
                    return false;
                }
            }

            // 2. Insert or update sawn stock balance
            const string updateStockSql = @"
                INSERT INTO SawnStock (Species, Dimensions, VolumeM3, LastUpdated)
                VALUES (@Species, @Dimensions, @VolumeM3, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE
                    VolumeM3 = VolumeM3 + VALUES(VolumeM3),
                    LastUpdated = UTC_TIMESTAMP();";

            await using (var stockCmd = new MySqlCommand(updateStockSql, connection, transaction))
            {
                stockCmd.Parameters.AddWithValue("@Species", evt.Species.Trim());
                stockCmd.Parameters.AddWithValue("@Dimensions", evt.Dimensions.Trim());
                stockCmd.Parameters.AddWithValue("@VolumeM3", evt.VolumeM3);
                await stockCmd.ExecuteNonQueryAsync();
            }

            // 3. Record event in ProcessedEvents table
            const string recordEventSql = @"
                INSERT INTO ProcessedEvents (EventId, EventType, JobId, ProcessedAt)
                VALUES (@EventId, @EventType, @JobId, UTC_TIMESTAMP());";

            await using (var recordCmd = new MySqlCommand(recordEventSql, connection, transaction))
            {
                recordCmd.Parameters.AddWithValue("@EventId", evt.EventId);
                recordCmd.Parameters.AddWithValue("@EventType", evt.EventType);
                recordCmd.Parameters.AddWithValue("@JobId", evt.JobId);
                await recordCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<decimal> GetStockBalanceAsync(string species, string dimensions)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT VolumeM3
            FROM SawnStock
            WHERE Species = @Species AND Dimensions = @Dimensions;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Species", species.Trim());
        cmd.Parameters.AddWithValue("@Dimensions", dimensions.Trim());

        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0m;
    }

    public async Task<IEnumerable<SawnStock>> GetAllStockAsync()
    {
        var list = new List<SawnStock>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT StockId, Species, Dimensions, VolumeM3, LastUpdated
            FROM SawnStock
            ORDER BY Species ASC, Dimensions ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new SawnStock
            {
                StockId = reader.GetInt32(reader.GetOrdinal("StockId")),
                Species = reader.GetString(reader.GetOrdinal("Species")),
                Dimensions = reader.GetString(reader.GetOrdinal("Dimensions")),
                VolumeM3 = reader.GetDecimal(reader.GetOrdinal("VolumeM3")),
                LastUpdated = reader.GetDateTime(reader.GetOrdinal("LastUpdated"))
            });
        }

        return list;
    }

    public async Task<bool> HasEventBeenProcessedAsync(string eventId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT COUNT(1) FROM ProcessedEvents WHERE EventId = @EventId;";
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@EventId", eventId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    public async Task<int> CreateBatchAsync(string species, string dimensions, string chemicalType, decimal quantityM3)
    {
        if (string.IsNullOrWhiteSpace(species) || string.IsNullOrWhiteSpace(dimensions) || string.IsNullOrWhiteSpace(chemicalType))
            throw new ArgumentException("Species, dimensions and chemical type are required.");
        if (quantityM3 <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. Check available balance (inside the transaction so concurrent batches can't both pass)
            const string balanceSql = "SELECT VolumeM3 FROM SawnStock WHERE Species = @Species AND Dimensions = @Dimensions FOR UPDATE;";
            decimal available;
            await using (var balanceCmd = new MySqlCommand(balanceSql, connection, transaction))
            {
                balanceCmd.Parameters.AddWithValue("@Species", species.Trim());
                balanceCmd.Parameters.AddWithValue("@Dimensions", dimensions.Trim());
                var result = await balanceCmd.ExecuteScalarAsync();
                available = result is null || result is DBNull ? 0m : Convert.ToDecimal(result);
            }

            if (available < quantityM3)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException(
                    $"Insufficient sawn stock for {species} ({dimensions}). Available: {available} m³, requested: {quantityM3} m³.");
            }

            // 2. Deduct the stock
            const string deductSql = @"
                UPDATE SawnStock
                SET VolumeM3 = VolumeM3 - @Qty, LastUpdated = UTC_TIMESTAMP()
                WHERE Species = @Species AND Dimensions = @Dimensions;";

            await using (var deductCmd = new MySqlCommand(deductSql, connection, transaction))
            {
                deductCmd.Parameters.AddWithValue("@Qty", quantityM3);
                deductCmd.Parameters.AddWithValue("@Species", species.Trim());
                deductCmd.Parameters.AddWithValue("@Dimensions", dimensions.Trim());
                await deductCmd.ExecuteNonQueryAsync();
            }

            // 3. Generate batch code and insert the batch (created Pending)
            var batchCode = $"TB-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

            const string batchSql = @"
                INSERT INTO TreatmentBatches (BatchCode, Species, Dimensions, ChemicalType, QuantityM3, Status, CreatedAt)
                VALUES (@Code, @Species, @Dimensions, @Chemical, @Qty, 'Pending', UTC_TIMESTAMP());
                SELECT LAST_INSERT_ID();";

            int batchId;
            await using (var batchCmd = new MySqlCommand(batchSql, connection, transaction))
            {
                batchCmd.Parameters.AddWithValue("@Code", batchCode);
                batchCmd.Parameters.AddWithValue("@Species", species.Trim());
                batchCmd.Parameters.AddWithValue("@Dimensions", dimensions.Trim());
                batchCmd.Parameters.AddWithValue("@Chemical", chemicalType.Trim());
                batchCmd.Parameters.AddWithValue("@Qty", quantityM3);
                batchId = Convert.ToInt32(await batchCmd.ExecuteScalarAsync());
            }

            // 4. Log the stock movement
            const string moveSql = @"
                INSERT INTO StockMovements (Species, Dimensions, VolumeM3, MovementType, BatchCode, CreatedAt)
                VALUES (@Species, @Dimensions, @Qty, 'BATCH_DEDUCTION', @Code, UTC_TIMESTAMP());";

            await using (var moveCmd = new MySqlCommand(moveSql, connection, transaction))
            {
                moveCmd.Parameters.AddWithValue("@Species", species.Trim());
                moveCmd.Parameters.AddWithValue("@Dimensions", dimensions.Trim());
                moveCmd.Parameters.AddWithValue("@Qty", quantityM3);
                moveCmd.Parameters.AddWithValue("@Code", batchCode);
                await moveCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return batchId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<TreatmentBatch>> GetBatchesAsync(string? status = null)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT BatchId, BatchCode, Species, Dimensions, ChemicalType, QuantityM3, Tank, TankId, CancellationReason, TreatedM3, RejectedM3, Status, StartedAt, CompletedAt, CreatedAt FROM TreatmentBatches";
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " WHERE Status = @Status";
        }
        sql += " ORDER BY CreatedAt DESC;";

        await using var cmd = new MySqlCommand(sql, connection);
        if (!string.IsNullOrWhiteSpace(status))
        {
            cmd.Parameters.AddWithValue("@Status", status.Trim());
        }

        return await ReadBatchesAsync(cmd);
    }

    public async Task<TreatmentBatch?> GetBatchByIdAsync(int batchId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT BatchId, BatchCode, Species, Dimensions, ChemicalType, QuantityM3, Tank, TankId, CancellationReason, TreatedM3, RejectedM3, Status, StartedAt, CompletedAt, CreatedAt
            FROM TreatmentBatches
            WHERE BatchId = @BatchId;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@BatchId", batchId);

        var batches = await ReadBatchesAsync(cmd);
        return batches.FirstOrDefault();
    }

    public async Task<bool> StartBatchAsync(int batchId, int tankId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. Load the batch
            decimal quantity = 0m;
            string status = string.Empty;
            await using (var batchCmd = new MySqlCommand(
                "SELECT Status, QuantityM3 FROM TreatmentBatches WHERE BatchId = @BatchId FOR UPDATE;",
                connection, transaction))
            {
                batchCmd.Parameters.AddWithValue("@BatchId", batchId);
                bool found;
                await using (var reader = await batchCmd.ExecuteReaderAsync())
                {
                    found = await reader.ReadAsync();
                    if (found)
                    {
                        status = reader.GetString(0);
                        quantity = reader.GetDecimal(1);
                    }
                }
                if (!found)
                {
                    await transaction.RollbackAsync();
                    throw new KeyNotFoundException($"Batch {batchId} not found.");
                }
            }

            if (status != "Pending")
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"Batch is {status}; only Pending batches can be started.");
            }

            // 2. Load the tank and validate it
            decimal capacity = 0m;
            string tankCode = string.Empty;
            await using (var tankCmd = new MySqlCommand(
                "SELECT TankCode, CapacityM3 FROM Tanks WHERE TankId = @TankId AND IsActive = TRUE;",
                connection, transaction))
            {
                tankCmd.Parameters.AddWithValue("@TankId", tankId);
                bool tankFound;
                await using (var reader = await tankCmd.ExecuteReaderAsync())
                {
                    tankFound = await reader.ReadAsync();
                    if (tankFound)
                    {
                        tankCode = reader.GetString(0);
                        capacity = reader.GetDecimal(1);
                    }
                }
                if (!tankFound)
                {
                    await transaction.RollbackAsync();
                    throw new KeyNotFoundException($"Tank {tankId} not found or inactive.");
                }
            }

            if (quantity > capacity)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException(
                    $"Batch quantity {quantity} m³ exceeds tank {tankCode} capacity {capacity} m³.");
            }

            // 3. Capacity / double-booking guard: no other InTreatment batch on this tank
            await using (var busyCmd = new MySqlCommand(
                "SELECT COUNT(1) FROM TreatmentBatches WHERE TankId = @TankId AND Status = 'InTreatment';",
                connection, transaction))
            {
                busyCmd.Parameters.AddWithValue("@TankId", tankId);
                var busy = Convert.ToInt32(await busyCmd.ExecuteScalarAsync());
                if (busy > 0)
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException(
                        $"Tank {tankCode} is busy with another InTreatment batch.");
                }
            }

            // 4. Start the batch
            await using (var startCmd = new MySqlCommand(@"
                UPDATE TreatmentBatches
                SET Status = 'InTreatment', StartedAt = UTC_TIMESTAMP(), TankId = @TankId, Tank = @TankCode
                WHERE BatchId = @BatchId;",
                connection, transaction))
            {
                startCmd.Parameters.AddWithValue("@TankId", tankId);
                startCmd.Parameters.AddWithValue("@TankCode", tankCode);
                startCmd.Parameters.AddWithValue("@BatchId", batchId);
                await startCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
            throw;
        }
    }

    public async Task<bool> CancelBatchAsync(int batchId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            decimal quantity;
            string status, species, dimensions;
            await using (var batchCmd = new MySqlCommand(
                "SELECT Status, QuantityM3, Species, Dimensions FROM TreatmentBatches WHERE BatchId = @BatchId FOR UPDATE;",
                connection, transaction))
            {
                batchCmd.Parameters.AddWithValue("@BatchId", batchId);
                bool found;
                await using (var reader = await batchCmd.ExecuteReaderAsync())
                {
                    found = await reader.ReadAsync();
                    if (found)
                    {
                        status = reader.GetString(0);
                        quantity = reader.GetDecimal(1);
                        species = reader.GetString(2);
                        dimensions = reader.GetString(3);
                    }
                    else
                    {
                        status = string.Empty; quantity = 0; species = string.Empty; dimensions = string.Empty;
                    }
                }
                if (!found)
                {
                    await transaction.RollbackAsync();
                    throw new KeyNotFoundException($"Batch {batchId} not found.");
                }
            }

            if (status != "Pending")
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException(
                    $"Batch is {status}; only Pending batches can be cancelled.");
            }

            await using (var cancelCmd = new MySqlCommand(@"
                UPDATE TreatmentBatches
                SET Status = 'Cancelled', CancellationReason = @Reason
                WHERE BatchId = @BatchId;", connection, transaction))
            {
                cancelCmd.Parameters.AddWithValue("@Reason", reason.Trim());
                cancelCmd.Parameters.AddWithValue("@BatchId", batchId);
                await cancelCmd.ExecuteNonQueryAsync();
            }

            // Restore the deducted sawn stock
            await using (var restoreCmd = new MySqlCommand(@"
                INSERT INTO SawnStock (Species, Dimensions, VolumeM3, LastUpdated)
                VALUES (@Species, @Dimensions, @Qty, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE
                    VolumeM3 = VolumeM3 + VALUES(VolumeM3),
                    LastUpdated = UTC_TIMESTAMP();", connection, transaction))
            {
                restoreCmd.Parameters.AddWithValue("@Species", species);
                restoreCmd.Parameters.AddWithValue("@Dimensions", dimensions);
                restoreCmd.Parameters.AddWithValue("@Qty", quantity);
                await restoreCmd.ExecuteNonQueryAsync();
            }

            await using (var moveCmd = new MySqlCommand(@"
                INSERT INTO StockMovements (Species, Dimensions, VolumeM3, MovementType, BatchCode, CreatedAt)
                SELECT @Species, @Dimensions, @Qty, 'TREATMENT_CANCELLED', BatchCode, UTC_TIMESTAMP()
                FROM TreatmentBatches WHERE BatchId = @BatchId;", connection, transaction))
            {
                moveCmd.Parameters.AddWithValue("@Species", species);
                moveCmd.Parameters.AddWithValue("@Dimensions", dimensions);
                moveCmd.Parameters.AddWithValue("@Qty", quantity);
                moveCmd.Parameters.AddWithValue("@BatchId", batchId);
                await moveCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
            throw;
        }
    }

    public async Task<bool> CompleteBatchAsync(int batchId, decimal treatedM3, decimal rejectedM3)
    {
        if (treatedM3 < 0 || rejectedM3 < 0)
        {
            throw new ArgumentException("Treated and rejected quantities cannot be negative.");
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            decimal quantity;
            string status;
            string species, dimensions, chemicalType;

            await using (var batchCmd = new MySqlCommand(
                "SELECT Status, QuantityM3, Species, Dimensions, ChemicalType FROM TreatmentBatches WHERE BatchId = @BatchId FOR UPDATE;",
                connection, transaction))
            {
                batchCmd.Parameters.AddWithValue("@BatchId", batchId);
                bool found;
                await using (var reader = await batchCmd.ExecuteReaderAsync())
                {
                    found = await reader.ReadAsync();
                    if (found)
                    {
                        status = reader.GetString(0);
                        quantity = reader.GetDecimal(1);
                        species = reader.GetString(2);
                        dimensions = reader.GetString(3);
                        chemicalType = reader.GetString(4);
                    }
                    else
                    {
                        status = string.Empty; quantity = 0; species = string.Empty; dimensions = string.Empty; chemicalType = string.Empty;
                    }
                }
                if (!found)
                {
                    await transaction.RollbackAsync();
                    throw new KeyNotFoundException($"Batch {batchId} not found.");
                }
            }

            if (status != "InTreatment")
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException(
                    $"Batch is {status}; only InTreatment batches can be completed.");
            }

            if (treatedM3 + rejectedM3 > quantity)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException(
                    $"Treated ({treatedM3}) + rejected ({rejectedM3}) exceeds batch input quantity ({quantity}).");
            }

            await using (var completeCmd = new MySqlCommand(@"
                UPDATE TreatmentBatches
                SET Status = 'Completed', CompletedAt = UTC_TIMESTAMP(), TreatedM3 = @Treated, RejectedM3 = @Rejected
                WHERE BatchId = @BatchId;", connection, transaction))
            {
                completeCmd.Parameters.AddWithValue("@Treated", treatedM3);
                completeCmd.Parameters.AddWithValue("@Rejected", rejectedM3);
                completeCmd.Parameters.AddWithValue("@BatchId", batchId);
                await completeCmd.ExecuteNonQueryAsync();
            }

            if (treatedM3 > 0)
            {
                await using (var creditCmd = new MySqlCommand(@"
                    INSERT INTO TreatedStock (Species, Dimensions, ChemicalType, VolumeM3, LastUpdated)
                    VALUES (@Species, @Dimensions, @Chemical, @Treated, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE
                        VolumeM3 = VolumeM3 + VALUES(VolumeM3),
                        LastUpdated = UTC_TIMESTAMP();", connection, transaction))
                {
                    creditCmd.Parameters.AddWithValue("@Species", species);
                    creditCmd.Parameters.AddWithValue("@Dimensions", dimensions);
                    creditCmd.Parameters.AddWithValue("@Chemical", chemicalType);
                    creditCmd.Parameters.AddWithValue("@Treated", treatedM3);
                    await creditCmd.ExecuteNonQueryAsync();
                }
            }

            await using (var moveCmd = new MySqlCommand(@"
                INSERT INTO StockMovements (Species, Dimensions, VolumeM3, MovementType, BatchCode, CreatedAt)
                SELECT @Species, @Dimensions, @Treated, 'TREATED_CREDIT', BatchCode, UTC_TIMESTAMP()
                FROM TreatmentBatches WHERE BatchId = @BatchId;", connection, transaction))
            {
                moveCmd.Parameters.AddWithValue("@Species", species);
                moveCmd.Parameters.AddWithValue("@Dimensions", dimensions);
                moveCmd.Parameters.AddWithValue("@Treated", treatedM3);
                moveCmd.Parameters.AddWithValue("@BatchId", batchId);
                await moveCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
            throw;
        }
    }

    public async Task<IEnumerable<TreatedStock>> GetTreatedStockAsync()
    {
        var list = new List<TreatedStock>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT StockId, Species, Dimensions, ChemicalType, VolumeM3, LastUpdated
            FROM TreatedStock
            ORDER BY Species ASC, Dimensions ASC, ChemicalType ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TreatedStock
            {
                StockId = reader.GetInt32(reader.GetOrdinal("StockId")),
                Species = reader.GetString(reader.GetOrdinal("Species")),
                Dimensions = reader.GetString(reader.GetOrdinal("Dimensions")),
                ChemicalType = reader.GetString(reader.GetOrdinal("ChemicalType")),
                VolumeM3 = reader.GetDecimal(reader.GetOrdinal("VolumeM3")),
                LastUpdated = reader.GetDateTime(reader.GetOrdinal("LastUpdated"))
            });
        }

        return list;
    }

    public async Task<IEnumerable<TankAvailability>> GetTanksAvailabilityAsync()
    {
        var list = new List<TankAvailability>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT t.TankId, t.TankCode, t.CapacityM3,
                   b.BatchId AS CurrentBatchId, b.BatchCode AS CurrentBatchCode, b.QuantityM3 AS CurrentBatchQuantityM3
            FROM Tanks t
            LEFT JOIN TreatmentBatches b
                ON b.TankId = t.TankId AND b.Status = 'InTreatment'
            WHERE t.IsActive = TRUE
            ORDER BY t.TankCode ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var hasBatch = !reader.IsDBNull(reader.GetOrdinal("CurrentBatchId"));
            list.Add(new TankAvailability
            {
                TankId = reader.GetInt32(reader.GetOrdinal("TankId")),
                TankCode = reader.GetString(reader.GetOrdinal("TankCode")),
                CapacityM3 = reader.GetDecimal(reader.GetOrdinal("CapacityM3")),
                Status = hasBatch ? "Busy" : "Idle",
                CurrentBatchId = hasBatch ? reader.GetInt32(reader.GetOrdinal("CurrentBatchId")) : null,
                CurrentBatchCode = hasBatch ? reader.GetString(reader.GetOrdinal("CurrentBatchCode")) : null,
                CurrentBatchQuantityM3 = hasBatch ? reader.GetDecimal(reader.GetOrdinal("CurrentBatchQuantityM3")) : null
            });
        }

        return list;
    }

    private static async Task<List<TreatmentBatch>> ReadBatchesAsync(MySqlCommand cmd)
    {
        var list = new List<TreatmentBatch>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TreatmentBatch
            {
                BatchId = reader.GetInt32(reader.GetOrdinal("BatchId")),
                BatchCode = reader.GetString(reader.GetOrdinal("BatchCode")),
                Species = reader.GetString(reader.GetOrdinal("Species")),
                Dimensions = reader.GetString(reader.GetOrdinal("Dimensions")),
                ChemicalType = reader.GetString(reader.GetOrdinal("ChemicalType")),
                QuantityM3 = reader.GetDecimal(reader.GetOrdinal("QuantityM3")),
                Tank = reader.IsDBNull(reader.GetOrdinal("Tank")) ? null : reader.GetString(reader.GetOrdinal("Tank")),
                TankId = reader.IsDBNull(reader.GetOrdinal("TankId")) ? null : reader.GetInt32(reader.GetOrdinal("TankId")),
                CancellationReason = reader.IsDBNull(reader.GetOrdinal("CancellationReason")) ? null : reader.GetString(reader.GetOrdinal("CancellationReason")),
                TreatedM3 = reader.IsDBNull(reader.GetOrdinal("TreatedM3")) ? null : reader.GetDecimal(reader.GetOrdinal("TreatedM3")),
                RejectedM3 = reader.IsDBNull(reader.GetOrdinal("RejectedM3")) ? null : reader.GetDecimal(reader.GetOrdinal("RejectedM3")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                StartedAt = reader.IsDBNull(reader.GetOrdinal("StartedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("StartedAt")),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            });
        }

        return list;
    }
}
