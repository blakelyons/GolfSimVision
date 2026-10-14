using GolfSimVision.Calibration;
using Xunit;

namespace GolfSimVision.Tests.Calibration;

public class RectangleCalibrationTests
{
    [Fact]
    public void BuildsFourCorrespondencesInOrder()
    {
        var topLeft = new PixelPoint(100, 100);
        var topRight = new PixelPoint(500, 110);
        var bottomRight = new PixelPoint(490, 400);
        var bottomLeft = new PixelPoint(90, 390);

        var correspondences = RectangleCalibration.BuildCorrespondences(
            topLeft, topRight, bottomRight, bottomLeft, realWidthMm: 300, realHeightMm: 200);

        Assert.Equal(4, correspondences.Count);
        Assert.Equal(new RealWorldPoint(0, 0), correspondences[0].RealWorld);
        Assert.Equal(new RealWorldPoint(300, 0), correspondences[1].RealWorld);
        Assert.Equal(new RealWorldPoint(300, 200), correspondences[2].RealWorld);
        Assert.Equal(new RealWorldPoint(0, 200), correspondences[3].RealWorld);
        Assert.Equal(topLeft, correspondences[0].Pixel);
        Assert.Equal(bottomLeft, correspondences[3].Pixel);
    }
}
