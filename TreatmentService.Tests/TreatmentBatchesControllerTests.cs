using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TreatmentService.Controllers;
using TreatmentService.Models;
using TreatmentService.Repositories;
using Xunit;

namespace TreatmentService.Tests;

/// <summary>
/// Comprehensive, 100% isolated unit test suite for TreatmentBatchesController.
/// Uses Moq to mock ITreatmentStockRepository with zero dependencies on MySQL or Kafka.
/// Covers: Happy paths, boundary conditions, negative inputs, business rule guards,
/// and behavior verification (Times.Once, Times.Never).
/// </summary>
public class TreatmentBatchesControllerTests
{
    private readonly Mock<ITreatmentStockRepository> _repoMock;
    private readonly TreatmentBatchesController _controller;

    public TreatmentBatchesControllerTests()
    {
        _repoMock = new Mock<ITreatmentStockRepository>(MockBehavior.Strict);
        _controller = new TreatmentBatchesController(_repoMock.Object);
    }

    #region CreateBatch Tests

    [Fact]
    public async Task CreateBatch_ValidDto_Returns201CreatedAndCallsRepoOnce()
    {
        // Arrange
        var dto = new CreateTreatmentBatchDto
        {
            Species = "Teak",
            Dimensions = "2x4x10",
            ChemicalType = "CCA",
            QuantityM3 = 3.5000m
        };

        _repoMock
            .Setup(r => r.CreateBatchAsync("Teak", "2x4x10", "CCA", 3.5000m))
            .ReturnsAsync(101);

        // Act
        var result = await _controller.CreateBatch(dto);

        // Assert
        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);

        _repoMock.Verify(r => r.CreateBatchAsync("Teak", "2x4x10", "CCA", 3.5000m), Times.Once);
    }

    [Theory]
    [InlineData("", "2x4x10", "CCA")]
    [InlineData("   ", "2x4x10", "CCA")]
    [InlineData("Teak", "", "CCA")]
    [InlineData("Teak", "   ", "CCA")]
    [InlineData("Teak", "2x4x10", "")]
    [InlineData("Teak", "2x4x10", "   ")]
    public async Task CreateBatch_MissingOrWhitespaceFields_ReturnsBadRequestAndNeverCallsRepo(
        string species, string dimensions, string chemicalType)
    {
        // Arrange
        var dto = new CreateTreatmentBatchDto
        {
            Species = species,
            Dimensions = dimensions,
            ChemicalType = chemicalType,
            QuantityM3 = 2.0m
        };

        // Act
        var result = await _controller.CreateBatch(dto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);

        _repoMock.Verify(r => r.CreateBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.0001)]
    [InlineData(-10.5)]
    public async Task CreateBatch_ZeroOrNegativeQuantity_ReturnsBadRequestAndNeverCallsRepo(decimal quantity)
    {
        // Arrange
        var dto = new CreateTreatmentBatchDto
        {
            Species = "Teak",
            Dimensions = "2x4x10",
            ChemicalType = "CCA",
            QuantityM3 = quantity
        };

        // Act
        var result = await _controller.CreateBatch(dto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);

        _repoMock.Verify(r => r.CreateBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task CreateBatch_OverAllocation_ReturnsConflictWhenRepoThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateTreatmentBatchDto
        {
            Species = "Teak",
            Dimensions = "2x4x10",
            ChemicalType = "CCA",
            QuantityM3 = 50.0m
        };

        _repoMock
            .Setup(r => r.CreateBatchAsync("Teak", "2x4x10", "CCA", 50.0m))
            .ThrowsAsync(new InvalidOperationException("Insufficient sawn stock balance for Teak 2x4x10."));

        // Act
        var result = await _controller.CreateBatch(dto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);

        _repoMock.Verify(r => r.CreateBatchAsync("Teak", "2x4x10", "CCA", 50.0m), Times.Once);
    }

    #endregion

    #region StartBatch Tests

    [Fact]
    public async Task StartBatch_ValidParameters_Returns200OkAndCallsRepoOnce()
    {
        // Arrange
        var dto = new StartBatchDto { TankId = 1 };
        _repoMock
            .Setup(r => r.StartBatchAsync(10, 1))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.StartBatch(10, dto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        _repoMock.Verify(r => r.StartBatchAsync(10, 1), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StartBatch_InvalidTankId_ReturnsBadRequestAndNeverCallsRepo(int invalidTankId)
    {
        // Arrange
        var dto = new StartBatchDto { TankId = invalidTankId };

        // Act
        var result = await _controller.StartBatch(10, dto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);

        _repoMock.Verify(r => r.StartBatchAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task StartBatch_BatchNotFound_Returns404NotFoundWhenRepoThrowsKeyNotFoundException()
    {
        // Arrange
        var dto = new StartBatchDto { TankId = 2 };
        _repoMock
            .Setup(r => r.StartBatchAsync(999, 2))
            .ThrowsAsync(new KeyNotFoundException("Treatment batch 999 not found."));

        // Act
        var result = await _controller.StartBatch(999, dto);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);

        _repoMock.Verify(r => r.StartBatchAsync(999, 2), Times.Once);
    }

    [Fact]
    public async Task StartBatch_TankBusyOrInvalidStatus_Returns409ConflictWhenRepoThrowsInvalidOperation()
    {
        // Arrange
        var dto = new StartBatchDto { TankId = 1 };
        _repoMock
            .Setup(r => r.StartBatchAsync(10, 1))
            .ThrowsAsync(new InvalidOperationException("Tank 1 is currently busy with another batch."));

        // Act
        var result = await _controller.StartBatch(10, dto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);

        _repoMock.Verify(r => r.StartBatchAsync(10, 1), Times.Once);
    }

    #endregion

    #region CancelBatch Tests

    [Fact]
    public async Task CancelBatch_ValidReason_Returns200OkAndCallsRepoOnce()
    {
        // Arrange
        var dto = new CancelBatchDto { Reason = "Operator requested cancellation due to incorrect chemical type." };
        _repoMock
            .Setup(r => r.CancelBatchAsync(15, dto.Reason))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CancelBatch(15, dto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        _repoMock.Verify(r => r.CancelBatchAsync(15, dto.Reason), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancelBatch_EmptyOrWhitespaceReason_ReturnsBadRequestAndNeverCallsRepo(string emptyReason)
    {
        // Arrange
        var dto = new CancelBatchDto { Reason = emptyReason };

        // Act
        var result = await _controller.CancelBatch(15, dto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);

        _repoMock.Verify(r => r.CancelBatchAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancelBatch_BatchNotFound_Returns404NotFoundWhenRepoThrowsKeyNotFoundException()
    {
        // Arrange
        var dto = new CancelBatchDto { Reason = "Cancel batch" };
        _repoMock
            .Setup(r => r.CancelBatchAsync(888, dto.Reason))
            .ThrowsAsync(new KeyNotFoundException("Treatment batch 888 not found."));

        // Act
        var result = await _controller.CancelBatch(888, dto);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);

        _repoMock.Verify(r => r.CancelBatchAsync(888, dto.Reason), Times.Once);
    }

    [Fact]
    public async Task CancelBatch_AlreadyInTreatmentOrCompleted_Returns409ConflictWhenRepoThrowsInvalidOperation()
    {
        // Arrange
        var dto = new CancelBatchDto { Reason = "Cancel ongoing batch" };
        _repoMock
            .Setup(r => r.CancelBatchAsync(15, dto.Reason))
            .ThrowsAsync(new InvalidOperationException("Cannot cancel batch 15: batch is currently InTreatment."));

        // Act
        var result = await _controller.CancelBatch(15, dto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);

        _repoMock.Verify(r => r.CancelBatchAsync(15, dto.Reason), Times.Once);
    }

    #endregion

    #region CompleteBatch Tests

    [Fact]
    public async Task CompleteBatch_ValidQuantities_Returns200OkAndCallsRepoOnce()
    {
        // Arrange
        var dto = new CompleteBatchDto
        {
            TreatedM3 = 3.2000m,
            RejectedM3 = 0.3000m
        };

        _repoMock
            .Setup(r => r.CompleteBatchAsync(20, 3.2000m, 0.3000m))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CompleteBatch(20, dto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        _repoMock.Verify(r => r.CompleteBatchAsync(20, 3.2000m, 0.3000m), Times.Once);
    }

    [Theory]
    [InlineData(-0.1, 0)]
    [InlineData(0, -0.5)]
    [InlineData(-1.0, -1.0)]
    public async Task CompleteBatch_NegativeQuantities_ReturnsBadRequestAndNeverCallsRepo(decimal treated, decimal rejected)
    {
        // Arrange
        var dto = new CompleteBatchDto { TreatedM3 = treated, RejectedM3 = rejected };

        // Act
        var result = await _controller.CompleteBatch(20, dto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);

        _repoMock.Verify(r => r.CompleteBatchAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task CompleteBatch_BatchNotFound_Returns404NotFoundWhenRepoThrowsKeyNotFoundException()
    {
        // Arrange
        var dto = new CompleteBatchDto { TreatedM3 = 1.0m, RejectedM3 = 0m };
        _repoMock
            .Setup(r => r.CompleteBatchAsync(777, 1.0m, 0m))
            .ThrowsAsync(new KeyNotFoundException("Treatment batch 777 not found."));

        // Act
        var result = await _controller.CompleteBatch(777, dto);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);

        _repoMock.Verify(r => r.CompleteBatchAsync(777, 1.0m, 0m), Times.Once);
    }

    [Fact]
    public async Task CompleteBatch_OverCompletion_Returns409ConflictWhenRepoThrowsInvalidOperation()
    {
        // Arrange
        var dto = new CompleteBatchDto { TreatedM3 = 5.0m, RejectedM3 = 2.0m };
        _repoMock
            .Setup(r => r.CompleteBatchAsync(20, 5.0m, 2.0m))
            .ThrowsAsync(new InvalidOperationException("Sum of treated (5.0) and rejected (2.0) exceeds input volume (3.5)."));

        // Act
        var result = await _controller.CompleteBatch(20, dto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);

        _repoMock.Verify(r => r.CompleteBatchAsync(20, 5.0m, 2.0m), Times.Once);
    }

    #endregion

    #region Query & Reporting Tests

    [Fact]
    public async Task GetBatches_StatusFilterPassed_ReturnsFilteredList()
    {
        // Arrange
        var sampleBatches = new List<TreatmentBatch>
        {
            new() { BatchId = 1, Species = "Teak", Status = "Pending" },
            new() { BatchId = 2, Species = "Mahogany", Status = "Pending" }
        };

        _repoMock
            .Setup(r => r.GetBatchesAsync("Pending"))
            .ReturnsAsync(sampleBatches);

        // Act
        var result = await _controller.GetBatches("Pending");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedBatches = Assert.IsAssignableFrom<IEnumerable<TreatmentBatch>>(okResult.Value);
        Assert.Equal(2, returnedBatches.Count());

        _repoMock.Verify(r => r.GetBatchesAsync("Pending"), Times.Once);
    }

    [Fact]
    public async Task GetDurationReport_ValidDateRange_ReturnsOkWithReport()
    {
        // Arrange
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
        var mockReport = new TreatmentDurationReport
        {
            CompletedBatches = 12,
            ActiveCycles = 2,
            AverageTurnaroundHours = 22.3,
            ByChemicalType = new List<ChemicalTypeDuration>
            {
                new() { ChemicalType = "CCA", CompletedCount = 8, AverageDurationHours = 24.5 },
                new() { ChemicalType = "Boron", CompletedCount = 4, AverageDurationHours = 18.0 }
            }
        };

        _repoMock
            .Setup(r => r.GetDurationReportAsync(fromDate, toDate))
            .ReturnsAsync(mockReport);

        // Act
        var result = await _controller.GetDurationReport(fromDate, toDate);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<TreatmentDurationReport>(okResult.Value);
        Assert.Equal(12, report.CompletedBatches);
        Assert.Equal(2, report.ActiveCycles);

        _repoMock.Verify(r => r.GetDurationReportAsync(fromDate, toDate), Times.Once);
    }

    [Fact]
    public async Task GetDurationReport_InvertedDateRange_ReturnsBadRequestAndNeverCallsRepo()
    {
        // Arrange
        var fromDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _controller.GetDurationReport(fromDate, toDate);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);

        _repoMock.Verify(r => r.GetDurationReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public async Task GetBatch_ExistingId_ReturnsOkWithBatch()
    {
        // Arrange
        var batch = new TreatmentBatch
        {
            BatchId = 42,
            Species = "Teak",
            Dimensions = "2x4x10",
            ChemicalType = "CCA",
            QuantityM3 = 3.5m,
            Status = "InTreatment"
        };

        _repoMock
            .Setup(r => r.GetBatchByIdAsync(42))
            .ReturnsAsync(batch);

        // Act
        var result = await _controller.GetBatch(42);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedBatch = Assert.IsType<TreatmentBatch>(okResult.Value);
        Assert.Equal(42, returnedBatch.BatchId);
        Assert.Equal("InTreatment", returnedBatch.Status);

        _repoMock.Verify(r => r.GetBatchByIdAsync(42), Times.Once);
    }

    [Fact]
    public async Task GetBatch_NonExistentId_ReturnsNotFound()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetBatchByIdAsync(999))
            .ReturnsAsync((TreatmentBatch?)null);

        // Act
        var result = await _controller.GetBatch(999);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);

        _repoMock.Verify(r => r.GetBatchByIdAsync(999), Times.Once);
    }

    #endregion
}
