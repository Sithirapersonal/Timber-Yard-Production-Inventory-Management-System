using LogIntakeService.DTOs;
using LogIntakeService.Models;
using LogIntakeService.Repositories;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LogIntakeService.Tests;

/// <summary>
/// Integration-style verification that a supplier inserted through
/// AddSupplierAsync is immediately visible to GetActiveSuppliersAsync
/// (i.e. the write path and the read path hit the same database and no
/// stale/cached result is returned).
/// </summary>
public class SupplierRepositoryTests
{
    private static LogIntakeRepository CreateRepository()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.test.json"), optional: true)
            .Build();

        var connectionString = config.GetConnectionString("LogIntakeDb")
            ?? "Server=localhost;Database=LogIntakeDB;User=root;Password=;";

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LogIntakeDb"] = connectionString
            })
            .Build();

        return new LogIntakeRepository(settings);
    }

    [Fact]
    public async Task AddedSupplier_IsReturnedByGetActiveSuppliers()
    {
        var repo = CreateRepository();
        var uniqueName = $"Test Supplier {Guid.NewGuid():N}";

        var newId = await repo.AddSupplierAsync(new Supplier
        {
            SupplierName = uniqueName,
            ContactNumber = "+94770001122",
            Address = "Test Address"
        });

        Assert.True(newId > 0);

        var suppliers = (await repo.GetActiveSuppliersAsync()).ToList();
        Assert.Contains(suppliers, s => s.SupplierId == newId && s.SupplierName == uniqueName && s.IsActive);

        // Cleanup: mark inactive so the test database is not polluted
        await repo.DeactivateSupplierAsync(newId);
    }

    [Fact]
    public async Task DeleteSupplier_RejectedWhenDeliveryHistoryExists()
    {
        var repo = CreateRepository();
        var uniqueName = $"History Supplier {Guid.NewGuid():N}";

        // Create a supplier and record a delivery against it
        var supplierId = await repo.AddSupplierAsync(new Supplier
        {
            SupplierName = uniqueName,
            ContactNumber = "+94770001122",
            Address = "Test Address"
        });

        var species = (await repo.GetSpeciesAsync()).First();
        var lengths = (await repo.GetLogLengthsAsync()).First();

        await repo.RecordDeliveryAsync(new TimberDelivery
        {
            SupplierId = supplierId,
            ReceivedBy = 1,
            LogCount = 1
        }, new[]
        {
            new DeliveryLogEntryDto { SpeciesId = species.SpeciesId, LengthId = lengths.LengthId, GirthFt = 3.5m }
        });

        // Deactivate it so the inactive guard passes and the delivery-history guard is what fails
        await repo.DeactivateSupplierAsync(supplierId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteSupplierAsync(supplierId));

        // The supplier must still exist (delete was rejected)
        var suppliers = (await repo.GetActiveSuppliersAsync(includeInactive: true)).ToList();
        Assert.Contains(suppliers, s => s.SupplierId == supplierId);
    }

    [Fact]
    public async Task ReactivateSupplier_DeactivatedSupplier_BecomesActiveAndReturnedByGetActiveSuppliers()
    {
        var repo = CreateRepository();
        var uniqueName = $"Reactivate Supplier {Guid.NewGuid():N}";

        var newId = await repo.AddSupplierAsync(new Supplier
        {
            SupplierName = uniqueName,
            ContactNumber = "+94770003344",
            Address = "Reactivate Address"
        });

        Assert.True(newId > 0);

        // Deactivate supplier
        var deactivated = await repo.DeactivateSupplierAsync(newId);
        Assert.True(deactivated);

        var activeSuppliersAfterDeactivation = (await repo.GetActiveSuppliersAsync()).ToList();
        Assert.DoesNotContain(activeSuppliersAfterDeactivation, s => s.SupplierId == newId);

        // Reactivate supplier
        var reactivated = await repo.ReactivateSupplierAsync(newId);
        Assert.True(reactivated);

        // Verify it is active again and returned by GetActiveSuppliersAsync
        var activeSuppliersAfterReactivation = (await repo.GetActiveSuppliersAsync()).ToList();
        var reactivatedSupplier = activeSuppliersAfterReactivation.FirstOrDefault(s => s.SupplierId == newId);
        Assert.NotNull(reactivatedSupplier);
        Assert.True(reactivatedSupplier.IsActive);
        Assert.Equal(uniqueName, reactivatedSupplier.SupplierName);

        // Cleanup: deactivate
        await repo.DeactivateSupplierAsync(newId);
    }
}
