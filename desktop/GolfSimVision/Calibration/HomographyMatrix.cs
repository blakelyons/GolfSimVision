using System;
using System.Linq;

namespace GolfSimVision.Calibration;

/// <summary>
/// Row-major 3x3 projective transform from pixel coordinates to real-world mm (ADR-0005:
/// a homography, perspective-correct across the whole ROI -- not a per-putt ball-radius
/// guess). A plain double[9], not an OpenCvSharp Mat, so this module and anything
/// persisting it (CalibrationStore) has no native-memory dependency -- only
/// HomographySolver touches OpenCvSharp directly.
/// </summary>
public sealed record HomographyMatrix(double[] Values)
{
    public RealWorldPoint MapPixelToReal(PixelPoint p)
    {
        var w = Values[6] * p.X + Values[7] * p.Y + Values[8];
        var x = (Values[0] * p.X + Values[1] * p.Y + Values[2]) / w;
        var y = (Values[3] * p.X + Values[4] * p.Y + Values[5]) / w;
        return new RealWorldPoint(x, y);
    }

    // Records synthesize Equals/GetHashCode from an array field's *reference* identity,
    // not its contents -- overridden here for real structural equality. This matters in
    // practice: CalibrationStore round-trips this through JSON, which always produces a
    // new array instance with the same values.
    public bool Equals(HomographyMatrix? other) => other is not null && Values.SequenceEqual(other.Values);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var v in Values) hash.Add(v);
        return hash.ToHashCode();
    }
}
