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
                INSERT INTO Logs (DeliveryId, SpeciesId, LengthId, Grade, GirthFt, VolumeM3, Status, CreatedAt)
                VALUES (@DeliveryId, @SpeciesId, @LengthId, @Grade, @GirthFt, @VolumeM3, 'InStock', UTC_TIMESTAMP());";

            foreach (var log in logs)
            {
                if (!lengthsMap.TryGetValue(log.LengthId, out var lengthFt))
                {
                    throw new InvalidOperationException($"Invalid LengthId: {log.LengthId}");
                }

                var volumeM3 = LogVolumeCalculator.CalculateVolumeM3(lengthFt, log.GirthFt);

                await using var logCmd = new MySqlCommand(logSql, connection, (MySqlTransaction)transaction);
                logCmd.Parameters.AddWithValue("@DeliveryId", deliveryId);
                logCmd.Parameters.AddWithValue("@SpeciesId", log.SpeciesId);
                logCmd.Parameters.AddWithValue("@LengthId", log.LengthId);
                logCmd.Parameters.AddWithValue("@Grade", log.Grade.Trim().ToUpperInvariant());
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
                s.SpeciesId,
                s.Name AS SpeciesName,
                l.LengthId,
                l.LengthFt,
                lg.Grade,
                COUNT(lg.LogId) AS LogCount,
                COALESCE(SUM(lg.VolumeM3), 0.0000) AS TotalVolumeM3,
                COALESCE(st.LowStockThreshold, 10.00) AS LowStockThreshold
            FROM Logs lg
            JOIN Species s ON lg.SpeciesId = s.SpeciesId
            JOIN LogLengths l ON lg.LengthId = l.LengthId
            LEFT JOIN StockThresholds st ON lg.SpeciesId = st.SpeciesId AND lg.LengthId = st.LengthId AND lg.Grade = st.Grade
            WHERE lg.Status = 'InStock'
            GROUP BY s.SpeciesId, s.Name, l.LengthId, l.LengthFt, lg.Grade, st.LowStockThreshold
            ORDER BY s.Name, l.LengthFt, lg.Grade;";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new StockSummaryDto
            {
                SpeciesId = reader.GetInt32("SpeciesId"),
                Species = reader.GetString("SpeciesName"),
                LengthId = reader.GetInt32("LengthId"),
                LengthFt = reader.GetDecimal("LengthFt"),
                Grade = reader.GetString("Grade"),
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
                d.Grade, 
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
                Grade = reader.IsDBNull(reader.GetOrdinal("Grade")) ? null : reader.GetString("Grade"),
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

    public async Task<bool> UpdateThresholdAsync(string species, string grade, decimal threshold)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE RawStock 
            SET LowStockThreshold = @Threshold,
                LastUpdated = UTC_TIMESTAMP()
            WHERE Species = @Species AND Grade = @Grade;";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Threshold", threshold);
        cmd.Parameters.AddWithValue("@Species", species);
        cmd.Parameters.AddWithValue("@Grade", grade);

        return await cmd.ExecuteNonQueryAsync() > 0;
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

    public async Task<IEnumerable<LogItem>> GetLogsAsync(int? speciesId = null, string? species = null, int? lengthId = null, string? grade = null)
    {
        var logs = new List<LogItem>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT 
                lg.LogId,
                lg.DeliveryId,
                lg.SpeciesId,
                s.Name AS SpeciesName,
                lg.LengthId,
                l.LengthFt,
                lg.Grade,
                lg.GirthFt,
                lg.VolumeM3,
                lg.Status,
                lg.CreatedAt,
                lg.RemovalReason,
                lg.RemovedAt,
                lg.RemovedBy
            FROM Logs lg
            JOIN Species s ON lg.SpeciesId = s.SpeciesId
            JOIN LogLengths l ON lg.LengthId = l.LengthId
            WHERE lg.Status = 'InStock'";

        if (speciesId.HasValue)
        {
            sql += " AND lg.SpeciesId = @SpeciesId";
        }
        else if (!string.IsNullOrWhiteSpace(species))
        {
            sql += " AND (s.Name = @Species OR CAST(s.SpeciesId AS CHAR) = @Species)";
        }

        if (lengthId.HasValue)
        {
            sql += " AND lg.LengthId = @LengthId";
        }

        if (!string.IsNullOrWhiteSpace(grade))
        {
            sql += " AND lg.Grade = @Grade";
        }

        sql += " ORDER BY lg.LogId DESC;";

        await using var cmd = new MySqlCommand(sql, connection);
        if (speciesId.HasValue)
            cmd.Parameters.AddWithValue("@SpeciesId", speciesId.Value);
        else if (!string.IsNullOrWhiteSpace(species))
            cmd.Parameters.AddWithValue("@Species", species.Trim());

        if (lengthId.HasValue)
            cmd.Parameters.AddWithValue("@LengthId", lengthId.Value);

        if (!string.IsNullOrWhiteSpace(grade))
            cmd.Parameters.AddWithValue("@Grade", grade.Trim().ToUpperInvariant());

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            logs.Add(new LogItem
            {
                LogId = reader.GetInt32("LogId"),
                DeliveryId = reader.GetInt32("DeliveryId"),
                SpeciesId = reader.GetInt32("SpeciesId"),
                SpeciesName = reader.GetString("SpeciesName"),
                LengthId = reader.GetInt32("LengthId"),
                LengthFt = reader.GetDecimal("LengthFt"),
                Grade = reader.GetString("Grade"),
                GirthFt = reader.GetDecimal("GirthFt"),
                VolumeM3 = reader.GetDecimal("VolumeM3"),
                Status = reader.GetString("Status"),
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
}