using LogIntakeService.Utils;
using Xunit;

namespace LogIntakeService.Tests;

public class LogVolumeCalculatorTests
{
    [Theory]
    [InlineData(16.0, 4.0, 0.4531)] // (4/4)^2 * 16 = 16 cu ft -> 16 * 0.028316846592 = 0.453069... -> 0.4531 m3
    [InlineData(10.0, 8.0, 1.1327)] // (8/4)^2 * 10 = 40 cu ft -> 40 * 0.028316846592 = 1.13267... -> 1.1327 m3
    [InlineData(12.0, 6.0, 0.7646)] // (6/4)^2 * 12 = 27 cu ft -> 27 * 0.028316846592 = 0.76455... -> 0.7646 m3
    [InlineData(8.0, 3.5, 0.1734)]  // (3.5/4)^2 * 8 = 6.125 cu ft -> 6.125 * 0.028316846592 = 0.17344... -> 0.1734 m3
    public void CalculateVolumeM3_KnownDimensions_ReturnsExpectedVolume(decimal lengthFt, decimal girthFt, decimal expectedVolumeM3)
    {
        var result = LogVolumeCalculator.CalculateVolumeM3(lengthFt, girthFt);

        Assert.Equal(expectedVolumeM3, result);
    }

    [Theory]
    [InlineData(0, 4.0)]
    [InlineData(10.0, 0)]
    [InlineData(-5.0, 4.0)]
    [InlineData(10.0, -2.0)]
    public void CalculateVolumeM3_NonPositiveDimensions_ReturnsZero(decimal lengthFt, decimal girthFt)
    {
        var result = LogVolumeCalculator.CalculateVolumeM3(lengthFt, girthFt);

        Assert.Equal(0m, result);
    }
}
