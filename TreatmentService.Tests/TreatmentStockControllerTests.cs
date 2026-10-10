using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TreatmentService.Controllers;
using TreatmentService.Models;
using TreatmentService.Repositories;

namespace TreatmentService.Tests;

public class TreatmentStockControllerTests
{
    private readonly Mock<ITreatmentStockRepository> _repoMock;
    private readonly TreatmentStockController _controller;

    public TreatmentStockControllerTests()
    {
        _repoMock = new Mock<ITreatmentStockRepository>();
        _controller = new TreatmentStockController(_repoMock.Object, NullLogger<TreatmentStockController>.Instance);
    }

    [Fact]
    public async Task GetAllStock_ReturnsOkWithList()
    {
        var sampleStock = new List<SawnStock>
        {
            new() { StockId = 1, Species = "Teak", Dimensions = "2x4x10", VolumeM3 = 3.5m, LastUpdated = DateTime.UtcNow }
        };

        _repoMock.Setup(r => r.GetAllStockAsync()).ReturnsAsync(sampleStock);

        var result = await _controller.GetAllStock();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<SawnStock>>(okResult.Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetStockBalance_ValidParams_ReturnsBalance()
    {
        _repoMock.Setup(r => r.GetStockBalanceAsync("Teak", "2x4x10")).ReturnsAsync(4.25m);

        var result = await _controller.GetStockBalance("Teak", "2x4x10");

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic val = okResult.Value!;
        Assert.NotNull(val);
    }

    [Theory]
    [InlineData("", "2x4x10")]
    [InlineData("Teak", "")]
    [InlineData(" ", " ")]
    public async Task GetStockBalance_MissingParams_ReturnsBadRequest(string species, string dimensions)
    {
        var result = await _controller.GetStockBalance(species, dimensions);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Health_ReturnsOk()
    {
        var result = _controller.Health();
        Assert.IsType<OkObjectResult>(result);
    }
}
