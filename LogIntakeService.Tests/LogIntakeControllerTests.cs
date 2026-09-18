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
            VehicleNumber = "WP-CAB-1234",
            ReceivedBy = 101,
            LogCount = 1,
            Notes = "Standard single-log delivery",
            Logs = new List<DeliveryLogEntryDto>
            {
                new() { SpeciesId = 1, LengthId = 1, Grade = "A", GirthFt = 4.5m }
            }
        };

        _mockRepository
            .Setup(repo => repo.RecordDeliveryAsync(It.IsAny<TimberDelivery>(), It.IsAny<IEnumerable<DeliveryLogEntryDto>>()))
            .ReturnsAsync(42);

        var result = await _controller.RecordDelivery(dto);

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, createdResult.StatusCode);
    }

    [Fact]
    public async Task RecordDelivery_MultipleLogs_PassesAllLogEntriesToRepository()
    {
        var logs = new List<DeliveryLogEntryDto>
        {
            new() { SpeciesId = 1, LengthId = 1, Grade = "A", GirthFt = 3.5m },
            new() { SpeciesId = 2, LengthId = 3, Grade = "B", GirthFt = 4.2m },
            new() { SpeciesId = 3, LengthId = 2, Grade = "A", GirthFt = 5.0m }
        };

        var dto = new CreateDeliveryDto
        {
            SupplierId = 2,
            VehicleNumber = "WP-XYZ-9876",
            ReceivedBy = 202,
            LogCount = logs.Count,
            Notes = "Multi-log batch test",
            Logs = logs
        };

        _mockRepository
            .Setup(repo => repo.RecordDeliveryAsync(It.IsAny<TimberDelivery>(), It.IsAny<IEnumerable<DeliveryLogEntryDto>>()))
            .ReturnsAsync(99);

        var result = await _controller.RecordDelivery(dto);

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, createdResult.StatusCode);

        _mockRepository.Verify(repo => repo.RecordDeliveryAsync(
            It.Is<TimberDelivery>(d => d.SupplierId == 2 && d.ReceivedBy == 202 && d.LogCount == 3),
            It.Is<IEnumerable<DeliveryLogEntryDto>>(passedLogs =>
                passedLogs.Count() == 3 &&
                passedLogs.First().GirthFt == 3.5m &&
                passedLogs.Last().GirthFt == 5.0m)
        ), Times.Once);
    }

    [Fact]
    public async Task GetStock_ReturnsOkResultWithStockList()
    {
        var sampleStock = new List<StockSummaryDto>
        {
            new()
            {
                SpeciesId = 1,
                Species = "Teak",
                LengthId = 1,
                LengthFt = 10.00m,
                Grade = "A",
                LogCount = 5,
                TotalVolumeM3 = 30.00m,
                LowStockThreshold = 10.00m
            },
            new()
            {
                SpeciesId = 2,
                Species = "Mahogany",
                LengthId = 2,
                LengthFt = 12.00m,
                Grade = "B",
                LogCount = 2,
                TotalVolumeM3 = 5.00m,
                LowStockThreshold = 12.00m
            }
        };

        _mockRepository
            .Setup(repo => repo.GetAllStockAsync())
            .ReturnsAsync(sampleStock);

        var result = await _controller.GetStock();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedStock = Assert.IsAssignableFrom<IEnumerable<StockSummaryDto>>(okResult.Value);
        var stockList = returnedStock.ToList();
        Assert.Equal(2, stockList.Count);
        Assert.Equal("Teak", stockList[0].Species);
        Assert.Equal(30.00m, stockList[0].TotalVolumeM3);
        Assert.False(stockList[0].IsLowStock);
        Assert.True(stockList[1].IsLowStock);
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