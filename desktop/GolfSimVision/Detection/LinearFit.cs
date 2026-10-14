using System.Collections.Generic;

namespace GolfSimVision.Detection;

/// <summary>Ordinary-least-squares line fit -- the same algorithm numpy.polyfit(x, y, 1)
/// uses, returning only the slope (the only part putt_calculator.py's np.polyfit calls
/// ever use).</summary>
internal static class LinearFit
{
    public static double Slope(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        var n = xs.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += xs[i];
            sumY += ys[i];
            sumXY += xs[i] * ys[i];
            sumXX += xs[i] * xs[i];
        }
        var denominator = (n * sumXX) - (sumX * sumX);
        return denominator == 0 ? 0.0 : ((n * sumXY) - (sumX * sumY)) / denominator;
    }
}
