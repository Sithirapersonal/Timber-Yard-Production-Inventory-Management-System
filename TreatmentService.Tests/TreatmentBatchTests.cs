using TreatmentService.Events;
using TreatmentService.Repositories;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Integration tests for treatment batch creation and batch listing.
/// Uses the locally-running MySQL TreatmentDB (same convention as the
/// integration-style supplier tests in LogIntakeService.Tests).
/// </summary>
public class TreatmentBatchTests
{
    private static TreatmentStockRepository CreateRepository()
    {
        var connectionString = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TreatmentDb"] = "Server=localhost;Database=TreatmentDB;User=root;Password=;"
            })
            .Build();

        return new TreatmentStockRepository(connectionString);
    }

    private static string UniqueSpecies() => $"TestSpecies_{Guid.NewGuid():N}";

    private static SawnStockCreditedEvent CreditEvent(string species, string dimensions, decimal volume, string eventId) => new()
    {
        EventId = eventId,
        EventType = "sawn-stock-credited",
        JobId = 1,
        Species = species,
        Dimensions = dimensions,
        VolumeM3 = volume,
        OccurredAt = DateTime.UtcNow
    };

    [Fact]
    public async Task CreateBatch_DeductsSawnStockAndCreatesPendingBatch()
    {
        var repo = CreateRepository();
        var species = UniqueSpecies();
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 5.0m, Guid.NewGuid().ToString()));

        var batchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 2.0m);

        Assert.True(batchId > 0);
        var remaining = await repo.GetStockBalanceAsync(species, dimensions);
        Assert.Equal(3.0m, remaining);

        var batch = await repo.GetBatchByIdAsync(batchId);
        Assert.NotNull(batch);
        Assert.Equal("Pending", batch.Status);
        Assert.Equal(2.0m, batch.QuantityM3);
        Assert.Equal("CCA", batch.ChemicalType);
    }

    [Fact]
    public async Task CreateBatch_OverAllocation_RejectedWithErrorAndNoStockChange()
    {
        var repo = CreateRepository();
        var species = UniqueSpecies();
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 1.0m, Guid.NewGuid().ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CreateBatchAsync(species, dimensions, "CCA", 2.0m));

        var remaining = await repo.GetStockBalanceAsync(species, dimensions);
        Assert.Equal(1.0m, remaining);
    }

    [Fact]
    public async Task GetBatches_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var repo = CreateRepository();
        var species = UniqueSpecies();
        const string dimensions = "2x4x10";

        await repo.CreditSawnStockAsync(CreditEvent(species, dimensions, 10.0m, Guid.NewGuid().ToString()));

        var batchId = await repo.CreateBatchAsync(species, dimensions, "CCA", 1.0m);
        var pending = (await repo.GetBatchesAsync("Pending")).ToList();
        Assert.Contains(pending, b => b.BatchId == batchId);

        var cancelled = (await repo.GetBatchesAsync("Cancelled")).ToList();
        Assert.DoesNotContain(cancelled, b => b.BatchId == batchId);
    }
}
