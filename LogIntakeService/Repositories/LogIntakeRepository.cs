using System.Data;
using MySql.Data.MySqlClient;
using LogIntakeService.Models;

namespace LogIntakeService.Repositories;

public class LogIntakeRepository : ILogIntakeRepository
{
    private readonly string _connectionString;

    public LogIntakeRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("LogIntakeDb")
            ?? throw new InvalidOperationException("LogIntakeDb connection string is not configured.");
    }

    public async Task<int> RecordDeliveryAsync(TimberDelivery delivery)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string deliverySql = @"
                INSERT INTO Deliveries (SupplierId, Species, Grade, VolumeM3, VehicleNumber, ReceivedBy, ReceivedAt)
                VALUES (@SupplierId, @Species, @Grade, @VolumeM3, @VehicleNumber, @ReceivedBy, UTC_TIMESTAMP());
                SELECT LAST_INSERT_ID();";

            await using var deliveryCmd = new MySqlCommand(deliverySql, connection, (MySqlTransaction)transaction);
            deliveryCmd.Parameters.AddWithValue("@SupplierId", delivery.SupplierId);
            deliveryCmd.Parameters.AddWithValue("@Species", delivery.Species);
            deliveryCmd.Parameters.AddWithValue("@Grade", delivery.Grade);
            deliveryCmd.Parameters.AddWithValue("@VolumeM3", delivery.VolumeM3);
            deliveryCmd.Parameters.AddWithValue("@VehicleNumber", (object?)delivery.VehicleNumber ?? DBNull.Value);
            deliveryCmd.Parameters.AddWithValue("@ReceivedBy", delivery.ReceivedBy);

            var deliveryId = Convert.ToInt32(await deliveryCmd.ExecuteScalarAsync());

            const string stockSql = @"
                INSERT INTO RawStock (Species, Grade, CurrentVolumeM3, LowStockThreshold, LastUpdated)
                VALUES (@Species, @Grade, @VolumeM3, 10.00, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE 
                    CurrentVolumeM3 = CurrentVolumeM3 + @VolumeM3,
                    LastUpdated = UTC_TIMESTAMP();";

            await using var stockCmd = new MySqlCommand(stockSql, connection, (MySqlTransaction)transaction);
            stockCmd.Parameters.AddWithValue("@Species", delivery.Species);
            stockCmd.Parameters.AddWithValue("@Grade", delivery.Grade);
            stockCmd.Parameters.AddWithValue("@VolumeM3", delivery.VolumeM3);
            await stockCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            return deliveryId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<RawStock>> GetAllStockAsync()
    {
        var items = new List<RawStock>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT StockId, Species, Grade, CurrentVolumeM3, LowStockThreshold, LastUpdated FROM RawStock ORDER BY Species, Grade;";
        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new RawStock
            {
                StockId = reader.GetInt32("StockId"),
                Species = reader.GetString("Species"),
                Grade = reader.GetString("Grade"),
                CurrentVolumeM3 = reader.GetDecimal("CurrentVolumeM3"),
                LowStockThreshold = reader.GetDecimal("LowStockThreshold"),
                LastUpdated = reader.GetDateTime("LastUpdated")
            });
        }
        return items;
    }

    public async Task<IEnumerable<TimberDelivery>> GetDeliveriesAsync()
    {
        var deliveries = new List<TimberDelivery>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT DeliveryId, SupplierId, Species, Grade, VolumeM3, VehicleNumber, ReceivedAt, ReceivedBy FROM Deliveries ORDER BY ReceivedAt DESC;";
        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            deliveries.Add(new TimberDelivery
            {
                DeliveryId = reader.GetInt32("DeliveryId"),
                SupplierId = reader.GetInt32("SupplierId"),
                Species = reader.GetString("Species"),
                Grade = reader.GetString("Grade"),
                VolumeM3 = reader.GetDecimal("VolumeM3"),
                VehicleNumber = reader.IsDBNull(reader.GetOrdinal("VehicleNumber")) ? null : reader.GetString("VehicleNumber"),
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

    public async Task<IEnumerable<Supplier>> GetActiveSuppliersAsync()
    {
        var suppliers = new List<Supplier>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT SupplierId, SupplierName, ContactNumber, Email, IsActive, CreatedAt FROM Suppliers WHERE IsActive = TRUE ORDER BY SupplierName ASC;";
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
                IsActive = reader.GetBoolean("IsActive"),
                CreatedAt = reader.GetDateTime("CreatedAt")
            });
        }
        return suppliers;
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
}