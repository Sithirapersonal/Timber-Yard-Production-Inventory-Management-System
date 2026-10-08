using TreatmentService.Events;
using TreatmentService.Repositories;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Integration tests for Treat.6: treated stock low-stock thresholds and alerts.
/// Serialized with the other DB-touching tests via [Collection("TreatmentDbIntegration")].
/// </summary>
[Collection("TreatmentDbIntegration")]
public class TreatedStockThresholdTests : IDisposable
{
    private const string ConnStr = "Server=localhost;Database=TreatmentDB;User=root;Password=;";

    public void Dispose()
    {
        using var conn = new MySqlConnection(ConnStr);
        conn.Open();
        using var cmd = new MySqlCommand(
            "DELETE FROM StockMovements WHERE Species LIKE 'Species\\_%'; " +
            "DELETE FROM TreatmentBatches WHERE Species LIKE 'Species\\_%'; " +
            "DELETE FROM SawnStock WHERE Species LIKE 'Species\\_%'; " +
            "DELETE FROM TreatedStock WHERE Species LIKE 'Species\\_%'; " +
            "DELETE FROM TreatedStockThresholds WHERE Species LIKE 'Species\\_%';", conn);
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

    private static async Task<int> GetTankIdAsync(string code)
    {
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT TankId FROM Tanks WHERE TankCode = @Code AND IsActive = TRUE;", conn);
        cmd.Parameters.AddWithValue("@Code", code);
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result is DBNull ? -1 : Convert.ToInt32(result);
    }

    [Fact]
    public async Task Threshold_Trigger_WhenStockBelowThreshold()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";
        const string chemical = "CCA";

        await repo.SetTreatedStockThresholdAsync(species, dimensions, chemical, 3m);

        // No treated stock yet -> no alerts
        var noAlerts = (await repo.GetTreatedStockAlertsAsync())
            .Where(a => a.Species == species)
            .ToList();
        Assert.Empty(noAlerts);

        // Credit, create, start, and complete a batch with treated 1m3 (< threshold 3m3)
        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, chemical, 2m);
        var tankId = await GetTankIdAsync("TANK-C");
        await repo.StartBatchAsync(batchId, tankId);
        await repo.CompleteBatchAsync(batchId, 1m, 0m);

        var alerts = (await repo.GetTreatedStockAlertsAsync())
            .Where(a => a.Species == species)
            .ToList();
        Assert.Single(alerts);
        Assert.True(alerts[0].VolumeM3 < alerts[0].ThresholdM3);
        Assert.Equal(1m, alerts[0].VolumeM3);
        Assert.Equal(3m, alerts[0].ThresholdM3);
    }

    [Fact]
    public async Task Threshold_NoTrigger_WhenStockAtOrAbove()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";
        const string chemical = "CCA";

        await repo.SetTreatedStockThresholdAsync(species, dimensions, chemical, 3m);

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, chemical, 3m);
        var tankId = await GetTankIdAsync("TANK-A");
        await repo.StartBatchAsync(batchId, tankId);
        await repo.CompleteBatchAsync(batchId, 3m, 0m);

        var alerts = (await repo.GetTreatedStockAlertsAsync())
            .Where(a => a.Species == species)
            .ToList();
        Assert.Empty(alerts);
    }

    [Fact]
    public async Task Threshold_Clear_WhenStockRestoredAbove()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";
        const string chemical = "CCA";

        await repo.SetTreatedStockThresholdAsync(species, dimensions, chemical, 3m);

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var firstBatchId = await repo.CreateBatchAsync(species, dimensions, chemical, 1m);
        var tankId = await GetTankIdAsync("TANK-C");
        await repo.StartBatchAsync(firstBatchId, tankId);
        await repo.CompleteBatchAsync(firstBatchId, 1m, 0m);

        // Below threshold -> alert present
        var alertBefore = (await repo.GetTreatedStockAlertsAsync())
            .Where(a => a.Species == species)
            .ToList();
        Assert.Single(alertBefore);

        // Complete another batch on TANK-A (idle) to lift volume to 4m >= 3m
        var secondBatchId = await repo.CreateBatchAsync(species, dimensions, chemical, 3m);
        var tankAId = await GetTankIdAsync("TANK-A");
        await repo.StartBatchAsync(secondBatchId, tankAId);
        await repo.CompleteBatchAsync(secondBatchId, 3m, 0m);

        var alertAfter = (await repo.GetTreatedStockAlertsAsync())
            .Where(a => a.Species == species)
            .ToList();
        Assert.Empty(alertAfter);
    }
}
