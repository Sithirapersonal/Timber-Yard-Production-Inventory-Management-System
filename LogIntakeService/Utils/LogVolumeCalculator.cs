namespace LogIntakeService.Utils;

public static class LogVolumeCalculator
{
    private const decimal CubicFeetToCubicMeters = 0.028316846592m;

    /// <summary>
    /// Calculates log volume in cubic meters using the quarter-girth formula:
    /// Volume (cu ft) = (GirthFt / 4)^2 * LengthFt
    /// Volume (m3) = Volume (cu ft) * 0.028316846592
    /// </summary>
    /// <param name="lengthFt">Length of the log in feet</param>
    /// <param name="girthFt">Quarter girth of the log in feet</param>
    /// <returns>Volume in cubic meters rounded to 4 decimal places</returns>
    public static decimal CalculateVolumeM3(decimal lengthFt, decimal girthFt)
    {
        if (lengthFt <= 0 || girthFt <= 0)
        {
            return 0m;
        }

        var quarterGirth = girthFt / 4m;
        var volumeCuFt = quarterGirth * quarterGirth * lengthFt;
        var volumeM3 = volumeCuFt * CubicFeetToCubicMeters;

        return Math.Round(volumeM3, 4, MidpointRounding.AwayFromZero);
    }
}
