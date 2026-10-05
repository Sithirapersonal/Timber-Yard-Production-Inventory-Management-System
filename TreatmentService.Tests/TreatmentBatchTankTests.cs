using TreatmentService.Events;
using TreatmentService.Repositories;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Integration tests for starting a treatment batch on a tank (Treat.3).
/// Uses the locally-running MySQL TreatmentDB (same convention as the
/// supplier/batch integration tests in the other test projects).
/// </summary>
public class TreatmentBatchTankTests
{
    private const string ConnStr = "Server=localhost;Database=TreatmentDB;User=root;Password=;";

    private static TreatmentStockRepository CreateRepository()
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TreatmentDb"] = ConnStr
            })
            .Build());

    private static async Task<int> EnsureTestTankAsync(string code, decimal capacity)
    {
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();

        await using (var insert = new MySqlCommand(
            "INSERT INTO Tanks (TankCode, CapacityM3, IsActive) VALUES (@Code, @Cap, TRUE) ON DUPLICATE KEY UPDATE CapacityM3 = @Cap;", conn))
        {
            insert.Parameters.AddWithValue("@Code", code);
            insert.Parameters.AddWithValue("@Cap", capacity);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var select = new MySqlCommand("SELECT TankId FROM Tanks WHERE TankCode = @Code;", conn))
        {
            select.Parameters.AddWithValue("@Code", code);
            return Convert.ToInt32(await select.ExecuteScalarAsync());
        }
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

    [Fact]
    public async Task StartBatch_Success_SetsInTreatmentStatusTankAndTimestamp()
    {
        var repo = CreateRepository();
        var tankId = await EnsureTestTankAsync($"T{Guid.NewGuid():N}".Substring(0, 20), 10m);
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 2m);

        var started = await repo.StartBatchAsync(batchId, tankId);

        Assert.True(started);
        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.NotNull(batch);
        Assert.Equal("InTreatment", batch.Status);
        Assert.Equal(tankId, batch.TankId);
        Assert.NotNull(batch.StartedAt);
    }

    [Fact]
    public async Task StartBatch_TankAlreadyBusy_RejectedWithConflict()
    {
        var repo = CreateRepository();
        var tankId = await EnsureTestTankAsync($"T{Guid.NewGuid():N}".Substring(0, 20), 10m);
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var firstBatchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 2m);
        await repo.StartBatchAsync(firstBatchId, tankId);

        var secondBatchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 1m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.StartBatchAsync(secondBatchId, tankId));

        var secondBatch = await repo.GetBatchByIdAsync(secondBatchId);
        Assert.Equal("Pending", secondBatch!.Status);
    }

    [Fact]
    public async Task StartBatch_QuantityExceedsTankCapacity_RejectedWithCapacityError()
    {
        var repo = CreateRepository();
        var tankId = await EnsureTestTankAsync($"T{Guid.NewGuid():N}".Substring(0, 20), 1m);
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 2m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.StartBatchAsync(batchId, tankId));

        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.Equal("Pending", batch!.Status);
    }
}
