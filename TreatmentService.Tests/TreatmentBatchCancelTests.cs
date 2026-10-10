using TreatmentService.Events;
using TreatmentService.Repositories;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Integration tests for Treat.5: cancelling a Pending treatment batch restores
/// the allocated sawn stock and rejects non-Pending batches. DB-touching classes
/// are in the TreatmentDbIntegration collection so they run serially.
/// </summary>
[Collection("TreatmentDbIntegration")]
public class TreatmentBatchCancelTests : IDisposable
{
    private const string ConnStr = "Server=localhost;Database=TreatmentDB;User=root;Password=;";

    public void Dispose()
    {
        using var conn = new MySqlConnection(ConnStr);
        conn.Open();
        using var cmd = new MySqlCommand(
            "DELETE FROM StockMovements WHERE Species LIKE 'Species\\_%'; " +
            "DELETE FROM TreatmentBatches WHERE Species LIKE 'Species\\_%'; " +
            "DELETE FROM SawnStock WHERE Species LIKE 'Species\\_%';", conn);
        cmd.ExecuteNonQuery();
    }

    private static TreatmentStockRepository CreateRepository()
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TreatmentDb"] = ConnStr
            })
            .Build());

    private static SawnStockCreditedEvent CreditEvent(string species, string dimensions, decimal volume) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = "sawn-stock-credited",
        JobId = 1,
        Species = species,
        Dimensions = dimensions,
        VolumeM3 = volume,
        OccurredAt = DateTime.UtcNow
    };

    [Fact]
    public async Task CancelBatch_Pending_RestoresSawnStockAndCancels()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 2m);

        var between = await repo.GetStockBalanceAsync(species, dimensions);
        Assert.Equal(8m, between); // 10 - 2 (batch deduction)

        var cancelled = await repo.CancelBatchAsync(batchId, "Schedule change");

        Assert.True(cancelled);
        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.Equal("Cancelled", batch!.Status);
        Assert.Equal("Schedule change", batch.CancellationReason);

        var after = await repo.GetStockBalanceAsync(species, dimensions);
        Assert.Equal(10m, after); // stock restored

        // Audit movement is logged
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT MovementType, VolumeM3 FROM StockMovements WHERE Species = @Species AND MovementType = 'TREATMENT_CANCELLED';", conn);
        cmd.Parameters.AddWithValue("@Species", species);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2m, reader.GetDecimal(1));
    }

    [Fact]
    public async Task CancelBatch_InTreatment_Rejected()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 2m);

        // Find TANK-A id
        int tankId;
        await using (var conn = new MySqlConnection(ConnStr))
        {
            await conn.OpenAsync();
            await using var sel = new MySqlCommand("SELECT TankId FROM Tanks WHERE TankCode = 'TANK-A';", conn);
            tankId = Convert.ToInt32(await sel.ExecuteScalarAsync());
        }

        await repo.StartBatchAsync(batchId, tankId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CancelBatchAsync(batchId, "too late"));

        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.Equal("InTreatment", batch!.Status);
    }

    [Fact]
    public async Task CancelBatch_MissingReason_Rejected()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => repo.CancelBatchAsync(1, "  "));
    }
}
