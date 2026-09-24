using System.Data;
using MySql.Data.MySqlClient;
using LogIntakeService.DTOs;
using LogIntakeService.Models;
using LogIntakeService.Utils;

namespace LogIntakeService.Repositories;

public class LogIntakeRepository : ILogIntakeRepository
{
    private readonly string _connectionString;

    public LogIntakeRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("LogIntakeDb")
            ?? throw new InvalidOperationException("LogIntakeDb connection string is not configured.");
    }

    public async Task<int> FindOrCreateStockAsync(int speciesId, int lengthId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return await FindOrCreateStockInternalAsync(speciesId, lengthId, connection, null);
    }

    private static async Task<int> FindOrCreateStockInternalAsync(
        int speciesId, 
        int lengthId, 
        MySqlConnection connection, 
        MySqlTransaction? transaction)
    {
        const string selectSql = "SELECT StockId FROM Stock WHERE SpeciesId = @SpeciesId AND LengthId = @LengthId;";
        await using (var selectCmd = new MySqlCommand(selectSql, connection, transaction))
        {
            selectCmd.Parameters.AddWithValue("@SpeciesId", speciesId);
            selectCmd.Parameters.AddWithValue("@LengthId", lengthId);
            var existingId = await selectCmd.ExecuteScalarAsync();
            if (existingId != null && existingId != DBNull.Value)
            {
                return Convert.ToInt32(existingId);
            }
        }

        const string insertSql = @"
            INSERT INTO Stock (SpeciesId, LengthId) VALUES (@SpeciesId, @LengthId)
            ON DUPLICATE KEY UPDATE StockId = LAST_INSERT_ID(StockId);
            SELECT LAST_INSERT_ID();";

        await using (var insertCmd = new MySqlCommand(insertSql, connection, transaction))
        {
            insertCmd.Parameters.AddWithValue("@SpeciesId", speciesId);
            insertCmd.Parameters.AddWithValue("@LengthId", lengthId);
            return Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        }
    }

    public async Task<int> RecordDeliveryAsync(TimberDelivery delivery, IEnumerable<DeliveryLogEntryDto> logs)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string deliverySql = @"
                INSERT INTO Deliveries (SupplierId, VehicleNumber, LogCount, Notes, ReceivedBy, ReceivedAt)
                VALUES (@SupplierId, @VehicleNumber, @LogCount, @Notes, @ReceivedBy, UTC_TIMESTAMP());
                SELECT LAST_INSERT_ID();";

            await using var deliveryCmd = new MySqlCommand(deliverySql, connection, (MySqlTransaction)transaction);
            deliveryCmd.Parameters.AddWithValue("@SupplierId", delivery.SupplierId);
            deliveryCmd.Parameters.AddWithValue("@VehicleNumber", (object?)delivery.VehicleNumber ?? DBNull.Value);
            deliveryCmd.Parameters.AddWithValue("@LogCount", (object?)delivery.LogCount ?? DBNull.Value);
            deliveryCmd.Parameters.AddWithValue("@Notes", (object?)delivery.Notes ?? DBNull.Value);
            deliveryCmd.Parameters.AddWithValue("@ReceivedBy", delivery.ReceivedBy);

            var deliveryId = Convert.ToInt32(await deliveryCmd.ExecuteScalarAsync());

            // Load lengths map for volume calculation
            var lengthsMap = new Dictionary<int, decimal>();
            const string lengthsSql = "SELECT LengthId, LengthFt FROM LogLengths;";
            await using (var lenCmd = new MySqlCommand(lengthsSql, connection, (MySqlTransaction)transaction))
            await using (var lenReader = await lenCmd.ExecuteReaderAsync())
            {
                while (await lenReader.ReadAsync())
                {
                    lengthsMap[lenReader.GetInt32("LengthId")] = lenReader.GetDecimal("LengthFt");
                }
            }

            // Insert each individual log in the transaction
            const string logSql = @"
                INSERT INTO Logs (DeliveryId, StockId, GirthFt, VolumeM3, Status, CreatedAt)
                VALUES (@DeliveryId, @StockId, @GirthFt, @VolumeM3, 'InStock', UTC_TIMESTAMP());";

            foreach (var log in logs)
            {
                if (!lengthsMap.TryGetValue(log.LengthId, out var lengthFt))
                {
                    throw new InvalidOperationException($"Invalid LengthId: {log.LengthId}");
                }

                var stockId = await FindOrCreateStockInternalAsync(log.SpeciesId, log.LengthId, connection, (MySqlTransaction)transaction);
                var volumeM3 = LogVolumeCalculator.CalculateVolumeM3(lengthFt, log.GirthFt);

                await using var logCmd = new MySqlCommand(logSql, connection, (MySqlTransaction)transaction);
                logCmd.Parameters.AddWithValue("@DeliveryId", deliveryId);
                logCmd.Parameters.AddWithValue("@StockId", stockId);
                logCmd.Parameters.AddWithValue("@GirthFt", log.GirthFt);
                logCmd.Parameters.AddWithValue("@VolumeM3", volumeM3);
                await logCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return deliveryId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<StockSummaryDto>> GetAllStockAsync()
    {
        var items = new List<StockSummaryDto>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT 
                st.StockId,
                st.SpeciesId,
                sp.Name AS SpeciesName,
                st.LengthId,
                ll.LengthFt,
                COUNT(lg.LogId) AS LogCount,
                COALESCE(SUM(lg.VolumeM3), 0.0000) AS TotalVolumeM3,
                COALESCE(sth.LowStockThreshold, 10.00) AS LowStockThreshold
            FROM Stock st
            JOIN Species sp ON st.SpeciesId = sp.SpeciesId
            JOIN LogLengths ll ON st.LengthId = ll.LengthId
            LEFT JOIN StockThresholds sth ON st.StockId = sth.StockId
            LEFT JOIN Logs lg ON st.StockId = lg.StockId AND lg.Status = 'InStock'
            GROUP BY st.StockId, st.SpeciesId, sp.Name, st.LengthId, ll.LengthFt, sth.LowStockThreshold
            ORDER BY sp.Name, ll.LengthFt;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new StockSummaryDto
            {
                StockId = reader.GetInt32("StockId"),
                SpeciesId = reader.GetInt32("SpeciesId"),
                Species = reader.GetString("SpeciesName"),
                LengthId = reader.GetInt32("LengthId"),
                LengthFt = reader.GetDecimal("LengthFt"),
                LogCount = reader.GetInt32("LogCount"),
                TotalVolumeM3 = reader.GetDecimal("TotalVolumeM3"),
                LowStockThreshold = reader.GetDecimal("LowStockThreshold")
            });
        }
        return items;
    }

    public async Task<IEnumerable<TimberDelivery>> GetDeliveriesAsync()
    {
        var deliveries = new List<TimberDelivery>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT 
                d.DeliveryId, 
                d.SupplierId, 
                s.SupplierName,
                d.Species, 
                d.VolumeM3, 
                d.VehicleNumber, 
                d.LogCount, 
                d.Notes, 
                d.ReceivedAt, 
                d.ReceivedBy 
            FROM Deliveries d
            LEFT JOIN Suppliers s ON d.SupplierId = s.SupplierId
            ORDER BY d.ReceivedAt DESC;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            deliveries.Add(new TimberDelivery
            {
                DeliveryId = reader.GetInt32("DeliveryId"),
                SupplierId = reader.GetInt32("SupplierId"),
                SupplierName = reader.IsDBNull(reader.GetOrdinal("SupplierName")) ? null : reader.GetString("SupplierName"),
                Species = reader.IsDBNull(reader.GetOrdinal("Species")) ? null : reader.GetString("Species"),
                VolumeM3 = reader.IsDBNull(reader.GetOrdinal("VolumeM3")) ? null : reader.GetDecimal("VolumeM3"),
                VehicleNumber = reader.IsDBNull(reader.GetOrdinal("VehicleNumber")) ? null : reader.GetString("VehicleNumber"),
                LogCount = reader.IsDBNull(reader.GetOrdinal("LogCount")) ? null : reader.GetInt32("LogCount"),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString("Notes"),
                ReceivedAt = reader.GetDateTime("ReceivedAt"),
                ReceivedBy = reader.GetInt32("ReceivedBy")
            });
        }
        return deliveries;
    }

    public async Task<bool> AdjustStockAsync(StockAdjustment adjustment)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string auditSql = @"
                INSERT INTO StockAdjustments (Species, Grade, AdjustedVolumeM3, Reason, AdjustedBy, AdjustedAt)
                VALUES (@Species, @Grade, @AdjustedVolumeM3, @Reason, @AdjustedBy, UTC_TIMESTAMP());";

            await using var auditCmd = new MySqlCommand(auditSql, connection, (MySqlTransaction)transaction);
            auditCmd.Parameters.AddWithValue("@Species", adjustment.Species);
            auditCmd.Parameters.AddWithValue("@Grade", adjustment.Grade);
            auditCmd.Parameters.AddWithValue("@AdjustedVolumeM3", adjustment.AdjustedVolumeM3);
            auditCmd.Parameters.AddWithValue("@Reason", adjustment.Reason);
            auditCmd.Parameters.AddWithValue("@AdjustedBy", adjustment.AdjustedBy);
            await auditCmd.ExecuteNonQueryAsync();

            const string stockSql = @"
                UPDATE RawStock 
                SET CurrentVolumeM3 = CurrentVolumeM3 + @AdjustedVolumeM3,
                    LastUpdated = UTC_TIMESTAMP()
                WHERE Species = @Species AND Grade = @Grade;";

            await using var stockCmd = new MySqlCommand(stockSql, connection, (MySqlTransaction)transaction);
            stockCmd.Parameters.AddWithValue("@Species", adjustment.Species);
            stockCmd.Parameters.AddWithValue("@Grade", adjustment.Grade);
            stockCmd.Parameters.AddWithValue("@AdjustedVolumeM3", adjustment.AdjustedVolumeM3);

            var rowsAffected = await stockCmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return rowsAffected > 0;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UpdateThresholdAsync(int stockId, decimal threshold)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string checkSql = "SELECT COUNT(*) FROM Stock WHERE StockId = @StockId;";
        await using (var checkCmd = new MySqlCommand(checkSql, connection))
        {
            checkCmd.Parameters.AddWithValue("@StockId", stockId);
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            if (count == 0)
            {
                return false;
            }
        }

        const string sql = @"
            INSERT INTO StockThresholds (StockId, LowStockThreshold)
            VALUES (@StockId, @Threshold)
            ON DUPLICATE KEY UPDATE LowStockThreshold = @Threshold;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@StockId", stockId);
        cmd.Parameters.AddWithValue("@Threshold", threshold);

        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<IEnumerable<Supplier>> GetActiveSuppliersAsync(bool includeInactive = false)
    {
        var suppliers = new List<Supplier>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT SupplierId, SupplierName, ContactNumber, Email, Address, IsActive, CreatedAt FROM Suppliers";
        if (!includeInactive)
        {
            sql += " WHERE IsActive = TRUE";
        }
        sql += " ORDER BY SupplierName ASC;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            suppliers.Add(new Supplier
            {
                SupplierId = reader.GetInt32("SupplierId"),
                SupplierName = reader.GetString("SupplierName"),
                ContactNumber = reader.IsDBNull(reader.GetOrdinal("ContactNumber")) ? null : reader.GetString("ContactNumber"),
                Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString("Email"),
                Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString("Address"),
                IsActive = reader.GetBoolean("IsActive"),
                CreatedAt = reader.GetDateTime("CreatedAt")
            });
        }
        return suppliers;
    }

    public async Task<int> AddSupplierAsync(Supplier supplier)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO Suppliers (SupplierName, ContactNumber, Address, IsActive, CreatedAt)
            VALUES (@SupplierName, @ContactNumber, @Address, TRUE, UTC_TIMESTAMP());
            SELECT LAST_INSERT_ID();";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SupplierName", supplier.SupplierName);
        cmd.Parameters.AddWithValue("@ContactNumber", (object?)supplier.ContactNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Address", (object?)supplier.Address ?? DBNull.Value);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<bool> DeactivateSupplierAsync(int supplierId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "UPDATE Suppliers SET IsActive = FALSE WHERE SupplierId = @SupplierId;";
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SupplierId", supplierId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<IEnumerable<Species>> GetSpeciesAsync()
    {
        var speciesList = new List<Species>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT SpeciesId, Name FROM Species ORDER BY Name ASC;";
        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            speciesList.Add(new Species
            {
                SpeciesId = reader.GetInt32("SpeciesId"),
                Name = reader.GetString("Name")
            });
        }
        return speciesList;
    }

    public async Task<IEnumerable<LogLength>> GetLogLengthsAsync()
    {
        var lengths = new List<LogLength>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT LengthId, LengthFt FROM LogLengths ORDER BY LengthFt ASC;";
        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            lengths.Add(new LogLength
            {
                LengthId = reader.GetInt32("LengthId"),
                LengthFt = reader.GetDecimal("LengthFt")
            });
        }
        return lengths;
    }

    public async Task<IEnumerable<LogItem>> GetLogsAsync(int? speciesId = null, int? lengthId = null)
    {
        var logs = new List<LogItem>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT 
                lg.LogId,
                lg.DeliveryId,
                lg.StockId,
                st.SpeciesId,
                s.Name AS SpeciesName,
                st.LengthId,
                l.LengthFt,
                lg.GirthFt,
                lg.VolumeM3,
                lg.Status,
                sup.SupplierName,
                lg.CreatedAt,
                lg.RemovalReason,
                lg.RemovedAt,
                lg.RemovedBy
            FROM Logs lg
            JOIN Stock st ON lg.StockId = st.StockId
            JOIN Species s ON st.SpeciesId = s.SpeciesId
            JOIN LogLengths l ON st.LengthId = l.LengthId
            JOIN Deliveries d ON lg.DeliveryId = d.DeliveryId
            LEFT JOIN Suppliers sup ON d.SupplierId = sup.SupplierId
            WHERE lg.Status = 'InStock'";

        if (speciesId.HasValue)
        {
            sql += " AND st.SpeciesId = @SpeciesId";
        }

        if (lengthId.HasValue)
        {
            sql += " AND st.LengthId = @LengthId";
        }

        sql += " ORDER BY lg.LogId DESC;";

        await using var cmd = new MySqlCommand(sql, connection);
        if (speciesId.HasValue)
            cmd.Parameters.AddWithValue("@SpeciesId", speciesId.Value);

        if (lengthId.HasValue)
            cmd.Parameters.AddWithValue("@LengthId", lengthId.Value);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            logs.Add(new LogItem
            {
                LogId = reader.GetInt32("LogId"),
                DeliveryId = reader.GetInt32("DeliveryId"),
                StockId = reader.GetInt32("StockId"),
                SpeciesId = reader.GetInt32("SpeciesId"),
                SpeciesName = reader.GetString("SpeciesName"),
                LengthId = reader.GetInt32("LengthId"),
                LengthFt = reader.GetDecimal("LengthFt"),
                GirthFt = reader.GetDecimal("GirthFt"),
                VolumeM3 = reader.GetDecimal("VolumeM3"),
                Status = reader.GetString("Status"),
                SupplierName = reader.IsDBNull(reader.GetOrdinal("SupplierName")) ? null : reader.GetString("SupplierName"),
                CreatedAt = reader.GetDateTime("CreatedAt"),
                RemovalReason = reader.IsDBNull(reader.GetOrdinal("RemovalReason")) ? null : reader.GetString("RemovalReason"),
                RemovedAt = reader.IsDBNull(reader.GetOrdinal("RemovedAt")) ? null : reader.GetDateTime("RemovedAt"),
                RemovedBy = reader.IsDBNull(reader.GetOrdinal("RemovedBy")) ? null : reader.GetInt32("RemovedBy")
            });
        }
        return logs;
    }

    public async Task<bool> RemoveLogAsync(int logId, string reason, int removedBy)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE Logs 
            SET Status = 'Removed',
                RemovalReason = @Reason,
                RemovedAt = UTC_TIMESTAMP(),
                RemovedBy = @RemovedBy
            WHERE LogId = @LogId AND Status = 'InStock';";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@LogId", logId);
        cmd.Parameters.AddWithValue("@Reason", reason);
        cmd.Parameters.AddWithValue("@RemovedBy", removedBy);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Marks all supplied LogIds as 'Consumed' in a single transaction.
    /// Only logs with Status = 'InStock' are eligible.
    /// Returns false (with rollback) if ANY requested log is not currently InStock,
    /// ensuring an all-or-nothing guarantee — the caller (SawmillService) can then
    /// roll back its own saw-job row and report a 409 to the end user.
    /// </summary>
    public async Task<bool> ConsumeLogsAsync(IEnumerable<int> logIds)
    {
        var idList = logIds.ToList();
        if (idList.Count == 0) return false;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. Count how many of the requested LogIds are currently InStock.
            //    If the count doesn't match, at least one is ineligible → rollback.
            var paramNames = idList.Select((_, i) => $"@lid{i}").ToArray();
            var countSql = $@"
                SELECT COUNT(*) FROM Logs
                WHERE LogId IN ({string.Join(",", paramNames)})
                  AND Status = 'InStock';";

            int eligibleCount;
            await using (var countCmd = new MySqlCommand(countSql, connection, (MySqlTransaction)transaction))
            {
                for (int i = 0; i < idList.Count; i++)
                    countCmd.Parameters.AddWithValue(paramNames[i], idList[i]);

                eligibleCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            if (eligibleCount != idList.Count)
            {
                // At least one log is not InStock — roll back and signal failure.
                await transaction.RollbackAsync();
                return false;
            }

            // 2. All logs are InStock — update them to Consumed.
            var updateSql = $@"
                UPDATE Logs
                SET Status = 'Consumed'
                WHERE LogId IN ({string.Join(",", paramNames)})
                  AND Status = 'InStock';";

            await using (var updateCmd = new MySqlCommand(updateSql, connection, (MySqlTransaction)transaction))
            {
                for (int i = 0; i < idList.Count; i++)
                    updateCmd.Parameters.AddWithValue(paramNames[i], idList[i]);

                await updateCmd.ExecuteNonQueryAsync();
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

    /// <summary>
    /// Marks all supplied LogIds as 'InStock' where they are currently 'Consumed'.
    /// Deliberately NOT all-or-nothing: Kafka can redeliver the same event, and by the
    /// time it is processed some logs may already be InStock (prior delivery) or in some
    /// other state (Removed/absent). Those are simply not matched — a second delivery
    /// updates 0 rows harmlessly. Returns rows affected so the caller can log the
    /// difference as useful signal rather than an error.
    /// </summary>
    public async Task<int> ReleaseLogsAsync(IEnumerable<int> logIds)
    {
        var idList = logIds.ToList();
        if (idList.Count == 0) return 0;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var paramNames = idList.Select((_, i) => $"@lid{i}").ToArray();
            var updateSql = $@"
                UPDATE Logs
                SET Status = 'InStock'
                WHERE LogId IN ({string.Join(",", paramNames)})
                  AND Status = 'Consumed';";

            int rowsAffected;
            await using (var updateCmd = new MySqlCommand(updateSql, connection, (MySqlTransaction)transaction))
            {
                for (int i = 0; i < idList.Count; i++)
                    updateCmd.Parameters.AddWithValue(paramNames[i], idList[i]);

                rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return rowsAffected;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Marks all supplied LogIds as 'Consumed' where they are currently 'InStock'.
    /// Deliberately NOT all-or-nothing (unlike ConsumeLogsAsync): Kafka can redeliver the
    /// same event, and by the time it is processed some logs may already be Consumed
    /// (prior delivery) or be in some other state (Removed/absent). Those are simply not
    /// matched — a second delivery updates 0 rows harmlessly. Returns rows affected so the
    /// caller can log the difference as useful signal rather than an error.
    /// </summary>
    public async Task<int> MarkLogsConsumedAsync(IEnumerable<int> logIds)
    {
        var idList = logIds.ToList();
        if (idList.Count == 0) return 0;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var paramNames = idList.Select((_, i) => $"@lid{i}").ToArray();
            var updateSql = $@"
                UPDATE Logs
                SET Status = 'Consumed'
                WHERE LogId IN ({string.Join(",", paramNames)})
                  AND Status = 'InStock';";

            int rowsAffected;
            await using (var updateCmd = new MySqlCommand(updateSql, connection, (MySqlTransaction)transaction))
            {
                for (int i = 0; i < idList.Count; i++)
                    updateCmd.Parameters.AddWithValue(paramNames[i], idList[i]);

                rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return rowsAffected;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}