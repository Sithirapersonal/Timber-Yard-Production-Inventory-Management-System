using TreatmentService.Events;
using TreatmentService.Repositories;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Integration tests for Treat.4: completing a treatment batch credits treated stock,
/// enforces quantity limits, and only applies to InTreatment batches.
/// Uses the locally-running MySQL TreatmentDB.
/// </summary>
[Collection("TreatmentDbIntegration")]
public class TreatmentBatchCompleteTests : IDisposable
{
    private const string ConnStr = "Server=localhost;Database=TreatmentDB;User=root;Password=;";

    public void Dispose()
    {
        // Clean up the rows this test class creates (Species_<guid>), so the DB is not polluted
        using var conn = new MySqlConnection(ConnStr);
        conn.Open();
        using var cmd = new MySqlCommand(
            "DELETE FROM StockMovements WHERE Species LIKE 'Species\\_%' OR Species LIKE 'TestSpecies\\_%'; " +
            "DELETE FROM TreatmentBatches WHERE Species LIKE 'Species\\_%' OR Species LIKE 'TestSpecies\\_%'; " +
            "DELETE FROM SawnStock WHERE Species LIKE 'Species\\_%' OR Species LIKE 'TestSpecies\\_%';",
            conn);
        cmd.ExecuteNonQuery();
    }

    private static TreatmentStockRepository CreateRepository()
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TreatmentDb"] = ConnStr
            })
            .Build());

    private static async Task<int> GetTankIdAsync(string code)
    {
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT TankId FROM Tanks WHERE TankCode = @Code AND IsActive = TRUE;", conn);
        cmd.Parameters.AddWithValue("@Code", code);
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result is DBNull ? -1 : Convert.ToInt32(result);
    }

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

    private static async Task<(int batchId, string species)> CreateAndStartBatchAsync(TreatmentStockRepository repo, string dimensions, decimal qty, string chemical)
    {
        var species = $"Species_{Guid.NewGuid():N}";
        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, chemical, qty);
        var tankId = await GetTankIdAsync("TANK-A");
        await repo.StartBatchAsync(batchId, tankId);
        return (batchId, species);
    }

    [Fact]
    public async Task CompleteBatch_CreditsTreatedStockAndMarksCompleted()
    {
        var repo = CreateRepository();
        var (batchId, species) = await CreateAndStartBatchAsync(repo, "2x4x10", 2.0m, "CCA");

        var completed = await repo.CompleteBatchAsync(batchId, 1.5m, 0.5m);

        Assert.True(completed);

        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.NotNull(batch);
        Assert.Equal("Completed", batch.Status);
        Assert.NotNull(batch.CompletedAt);
        Assert.Equal(1.5m, batch.TreatedM3);
        Assert.Equal(0.5m, batch.RejectedM3);

        var treatedStock = (await repo.GetTreatedStockAsync()).ToList();
        var row = treatedStock.FirstOrDefault(s => s.Species == species && s.Dimensions == "2x4x10" && s.ChemicalType == "CCA");
        Assert.NotNull(row);
        Assert.Equal(1.5m, row.VolumeM3);
    }

    [Fact]
    public async Task CompleteBatch_TreatedPlusRejectedExceedsQuantity_Rejected()
    {
        var repo = CreateRepository();
        var (batchId, _) = await CreateAndStartBatchAsync(repo, "2x4x10", 2.0m, "CCA");

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CompleteBatchAsync(batchId, 1.5m, 1.0m));

        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.Equal("InTreatment", batch!.Status);
    }

    [Fact]
    public async Task CompleteBatch_NotInTreatment_Rejected()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        await repo.CreditSawnStockAsync(CreditEvent(species, "2x4x10", 10m));
        var batchId = await repo.CreateBatchAsync(species, "2x4x10", "CCA", 2.0m);

        // Still Pending — completion must be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CompleteBatchAsync(batchId, 1.0m, 0m));
    }
}
