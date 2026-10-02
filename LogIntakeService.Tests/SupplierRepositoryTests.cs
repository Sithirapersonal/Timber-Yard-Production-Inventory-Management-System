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
}
