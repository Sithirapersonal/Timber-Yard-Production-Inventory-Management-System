using SawmillService.Utils;

namespace SawmillService.Tests;

public class SawJobCalculatorTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // ComputeTotalVolumeM3
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new double[] { 0.4531, 1.1327, 0.7646 }, 2.3504)]
    [InlineData(new double[] { 0.1734 }, 0.1734)]
    [InlineData(new double[] { }, 0.0000)]
    public void ComputeTotalVolumeM3_SumsCorrectly(double[] volumes, double expectedTotal)
    {
        var decimalVolumes = volumes.Select(v => (decimal)v);
        var result = SawJobCalculator.ComputeTotalVolumeM3(decimalVolumes);
        Assert.Equal((decimal)expectedTotal, result);
    }

    [Fact]
    public void ComputeTotalVolumeM3_SingleLog_ReturnsThatVolume()
    {
        var result = SawJobCalculator.ComputeTotalVolumeM3(new[] { 0.9876m });
        Assert.Equal(0.9876m, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FindInvalidLogIds — "all logs must belong to the same stock" validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FindInvalidLogIds_AllValid_ReturnsEmptyList()
    {
        var requested = new[] { 1, 2, 3 };
        var inStock   = new[] { 1, 2, 3, 4, 5 };

        var invalid = SawJobCalculator.FindInvalidLogIds(requested, inStock);

        Assert.Empty(invalid);
    }

    [Fact]
    public void FindInvalidLogIds_SomeInvalid_ReturnsInvalidIds()
    {
        var requested = new[] { 1, 2, 99, 100 };
        var inStock   = new[] { 1, 2, 3 };

        var invalid = SawJobCalculator.FindInvalidLogIds(requested, inStock);

        Assert.Equal(2, invalid.Count);
        Assert.Contains(99, invalid);
        Assert.Contains(100, invalid);
    }

    [Fact]
    public void FindInvalidLogIds_AllInvalid_ReturnsAllRequestedIds()
    {
        var requested = new[] { 50, 51 };
        var inStock   = new[] { 1, 2, 3 };

        var invalid = SawJobCalculator.FindInvalidLogIds(requested, inStock);

        Assert.Equal(2, invalid.Count);
    }

    [Fact]
    public void FindInvalidLogIds_EmptyRequested_ReturnsEmpty()
    {
        var invalid = SawJobCalculator.FindInvalidLogIds(
            Enumerable.Empty<int>(),
            new[] { 1, 2, 3 });

        Assert.Empty(invalid);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FormatJobCode — SAW-NNN padding logic
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,    "SAW-001")]
    [InlineData(9,    "SAW-009")]
    [InlineData(42,   "SAW-042")]
    [InlineData(100,  "SAW-100")]
    [InlineData(999,  "SAW-999")]
    [InlineData(1000, "SAW-1000")]  // no leading-zero truncation for 4-digit numbers
    public void FormatJobCode_VariousSequenceNumbers_ReturnsCorrectCode(int seq, string expected)
    {
        var result = SawJobCalculator.FormatJobCode(seq);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatJobCode_ZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SawJobCalculator.FormatJobCode(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SawJobCalculator.FormatJobCode(-5));
    }
}
