using TreatmentService.Events;
using TreatmentService.Repositories;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Integration tests for Treat.7: treatment turnaround / duration report.
/// Serialized with the other DB-touching tests via [Collection("TreatmentDbIntegration")].
/// </summary>
[Collection("TreatmentDbIntegration")]
public class TreatmentDurationReportTests : IDisposable
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

    private static async Task<string> GetIdleTankAsync(TreatmentStockRepository repo)
    {
        var tanks = await repo.GetTanksAvailabilityAsync();
        var idle = tanks.FirstOrDefault(t => t.Status == "Idle")
            ?? throw new InvalidOperationException("No idle tank available for duration-report tests.");
        return idle.TankCode;
    }

    private static async Task<int> GetTankIdAsync(string tankCode)
    {
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT TankId FROM Tanks WHERE TankCode = @Code AND IsActive = TRUE;", conn);
        cmd.Parameters.AddWithValue("@Code", tankCode);
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result is DBNull ? -1 : Convert.ToInt32(result);
    }

    /// <summary>Backdates a batch so its turnaround is exactly <paramref name="durationHours"/>.</summary>
    private static async Task BackdateBatchAsync(int batchId, double durationHours)
    {
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE TreatmentBatches " +
            "SET CompletedAt = UTC_TIMESTAMP() - INTERVAL 1 HOUR, " +
            "    StartedAt = UTC_TIMESTAMP() - INTERVAL " + ((int)durationHours + 1) + " HOUR " +
            "WHERE BatchId = @Id;", conn);
        cmd.Parameters.AddWithValue("@Id", batchId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Report_CalculatesAverageDurationPerChemicalType()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        var chemical = $"CHEM_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, chemical, 2m);
        await repo.StartBatchAsync(batchId, await GetTankIdAsync(await GetIdleTankAsync(repo)));
        await repo.CompleteBatchAsync(batchId, 2m, 0m);
        await BackdateBatchAsync(batchId, durationHours: 3);

        var report = await repo.GetDurationReportAsync(null, null);

        Assert.NotNull(report.AverageTurnaroundHours);
        Assert.True(report.CompletedBatches >= 1);

        var entry = report.ByChemicalType.FirstOrDefault(c => c.ChemicalType == chemical);
        Assert.NotNull(entry);
        Assert.Equal(1, entry!.CompletedCount);
        Assert.Equal(3, entry.AverageDurationHours, precision: 1);
        Assert.Equal(entry.MinDurationHours, entry.MaxDurationHours);
        Assert.Equal(3, entry.AverageDurationHours, precision: 1);
    }

    [Fact]
    public async Task Report_DateRangeFilter_UpdatesMetrics()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        var chemical = $"CHEM_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, chemical, 1m);
        await repo.StartBatchAsync(batchId, await GetTankIdAsync(await GetIdleTankAsync(repo)));
        await repo.CompleteBatchAsync(batchId, 1m, 0m);
        await BackdateBatchAsync(batchId, durationHours: 2);

        // Range covering the (backdated) completion -> included
        var covering = await repo.GetDurationReportAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        var included = covering.ByChemicalType.FirstOrDefault(c => c.ChemicalType == chemical);
        Assert.NotNull(included);
        Assert.Equal(1, included!.CompletedCount);

        // Range far in the past -> excluded (dynamic metric update)
        var past = await repo.GetDurationReportAsync(
            new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(1999, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.DoesNotContain(past.ByChemicalType, c => c.ChemicalType == chemical);
        Assert.Equal(0, past.CompletedBatches);
        Assert.Equal(0, past.ActiveCycles);
    }

    [Fact]
    public async Task Report_EmptyRange_ShowsEmptyStateNotError()
    {
        var repo = CreateRepository();

        var report = await repo.GetDurationReportAsync(
            new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(1999, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(report);
        Assert.Equal(0, report.CompletedBatches);
        Assert.Equal(0, report.ActiveCycles);
        Assert.Null(report.AverageTurnaroundHours);
        Assert.Empty(report.ByChemicalType);
    }

    [Fact]
    public async Task Report_CountsActiveCycles()
    {
        var repo = CreateRepository();
        var species = $"Species_{Guid.NewGuid():N}";
        var chemical = $"CHEM_{Guid.NewGuid():N}";
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10m));
        var batchId = await repo.CreateBatchAsync(species, dimensions, chemical, 1m);
        await repo.StartBatchAsync(batchId, await GetTankIdAsync(await GetIdleTankAsync(repo)));

        var report = await repo.GetDurationReportAsync(null, null);
        Assert.True(report.ActiveCycles >= 1);

        // Past-only range: an active cycle started today is excluded
        var past = await repo.GetDurationReportAsync(
            new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(1999, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(0, past.ActiveCycles);
    }
}
