using Microsoft.AspNetCore.Mvc;
using Moq;
using LogIntakeService.Controllers;
using LogIntakeService.DTOs;
using LogIntakeService.Models;
using LogIntakeService.Repositories;
using Xunit;

namespace LogIntakeService.Tests;

public class LogIntakeControllerTests
{
    private readonly Mock<ILogIntakeRepository> _mockRepository;
    private readonly LogIntakeController _controller;

    public LogIntakeControllerTests()
    {
        _mockRepository = new Mock<ILogIntakeRepository>();
        _controller = new LogIntakeController(_mockRepository.Object);
    }

    [Fact]
    public async Task RecordDelivery_ValidDto_ReturnsCreatedResultWithDeliveryId()
    {
        var dto = new CreateDeliveryDto
        {
            SupplierId = 1,
            Species = "Teak",
            Grade = "A",
            VolumeM3 = 12.50m,
            VehicleNumber = "WP-CAB-1234",
            ReceivedBy = 101
        };

        _mockRepository
            .Setup(repo => repo.RecordDeliveryAsync(It.IsAny<TimberDelivery>()))
            .ReturnsAsync(42);

        var result = await _controller.RecordDelivery(dto);

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, createdResult.StatusCode);
    }

    [Fact]
    public async Task GetStock_ReturnsOkResultWithStockList()
    {
        var sampleStock = new List<RawStock>
        {
            new() { StockId = 1, Species = "Teak", Grade = "A", CurrentVolumeM3 = 30.00m, LowStockThreshold = 10.00m },
            new() { StockId = 2, Species = "Mahogany", Grade = "B", CurrentVolumeM3 = 5.00m, LowStockThreshold = 12.00m }
        };

        _mockRepository
            .Setup(repo => repo.GetAllStockAsync())
            .ReturnsAsync(sampleStock);

        var result = await _controller.GetStock();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedStock = Assert.IsAssignableFrom<IEnumerable<RawStock>>(okResult.Value);
        Assert.Equal(2, returnedStock.Count());
    }

    [Fact]
    public async Task AdjustStock_ExistingStock_ReturnsOkResult()
    {
        var dto = new StockAdjustmentDto
        {
            Species = "Teak",
            Grade = "A",
            AdjustedVolumeM3 = -2.5m,
            Reason = "Quality inspection trim",
            AdjustedBy = 101
        };

        _mockRepository
            .Setup(repo => repo.AdjustStockAsync(It.IsAny<StockAdjustment>()))
            .ReturnsAsync(true);

        var result = await _controller.AdjustStock(dto);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AdjustStock_NonExistingStock_ReturnsNotFound()
    {
        var dto = new StockAdjustmentDto
        {
            Species = "UnknownSpecies",
            Grade = "Z",
            AdjustedVolumeM3 = -1.0m,
            Reason = "Non-existent item adjustment",
            AdjustedBy = 101
        };

        _mockRepository
            .Setup(repo => repo.AdjustStockAsync(It.IsAny<StockAdjustment>()))
            .ReturnsAsync(false);

        var result = await _controller.AdjustStock(dto);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeactivateSupplier_ExistingSupplier_ReturnsOkResult()
    {
        _mockRepository
            .Setup(repo => repo.DeactivateSupplierAsync(3))
            .ReturnsAsync(true);

        var result = await _controller.DeactivateSupplier(3);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DeactivateSupplier_NonExistingSupplier_ReturnsNotFound()
    {
        _mockRepository
            .Setup(repo => repo.DeactivateSupplierAsync(999))
            .ReturnsAsync(false);

        var result = await _controller.DeactivateSupplier(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}