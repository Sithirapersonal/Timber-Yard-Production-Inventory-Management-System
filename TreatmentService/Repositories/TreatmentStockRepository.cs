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
            var batchCode = $"TB-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";

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

        var sql = "SELECT BatchId, BatchCode, Species, Dimensions, ChemicalType, QuantityM3, Tank, CancellationReason, Status, StartedAt, CompletedAt, CreatedAt FROM TreatmentBatches";
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
            SELECT BatchId, BatchCode, Species, Dimensions, ChemicalType, QuantityM3, Tank, CancellationReason, Status, StartedAt, CompletedAt, CreatedAt
            FROM TreatmentBatches
            WHERE BatchId = @BatchId;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@BatchId", batchId);

        var batches = await ReadBatchesAsync(cmd);
        return batches.FirstOrDefault();
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
                CancellationReason = reader.IsDBNull(reader.GetOrdinal("CancellationReason")) ? null : reader.GetString(reader.GetOrdinal("CancellationReason")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                StartedAt = reader.IsDBNull(reader.GetOrdinal("StartedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("StartedAt")),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            });
        }

        return list;
    }
}
