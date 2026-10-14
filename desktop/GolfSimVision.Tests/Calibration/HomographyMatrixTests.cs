using GolfSimVision.Calibration;
using Xunit;

namespace GolfSimVision.Tests.Calibration;

public class HomographyMatrixTests
{
    [Fact]
    public void IdentityMatrixMapsPointsUnchanged()
    {
        var identity = new HomographyMatrix(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });

        var result = identity.MapPixelToReal(new PixelPoint(42, 17));

        Assert.Equal(42, result.Xmm, precision: 9);
        Assert.Equal(17, result.Ymm, precision: 9);
    }

    [Fact]
    public void ScaleAndTranslateMapsAsExpected()
    {
        // 2x scale, +10/+20 translation, no perspective term.
        var h = new HomographyMatrix(new double[] { 2, 0, 10, 0, 2, 20, 0, 0, 1 });

        var result = h.MapPixelToReal(new PixelPoint(5, 5));

        Assert.Equal(20, result.Xmm, precision: 9); // 2*5 + 10
        Assert.Equal(30, result.Ymm, precision: 9); // 2*5 + 20
    }

    [Fact]
    public void TwoInstancesWithEqualValuesButDifferentArraysAreEqual()
    {
        // The pitfall this overrides: records synthesize array-field equality from
        // *reference* identity, not contents -- exactly what JSON round-tripping via
        // CalibrationStore always produces (a brand new array with the same values).
        var a = new HomographyMatrix(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        var b = new HomographyMatrix(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentValuesAreNotEqual()
    {
        var a = new HomographyMatrix(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        var b = new HomographyMatrix(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 99 });

        Assert.NotEqual(a, b);
    }
}
