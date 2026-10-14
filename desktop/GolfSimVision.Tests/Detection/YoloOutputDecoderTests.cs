using GolfSimVision.Detection;
using Xunit;

namespace GolfSimVision.Tests.Detection;

public class YoloOutputDecoderTests
{
    private const int NumAnchors = 4;

    /// <summary>Builds a flat [5, numAnchors] row-major tensor from per-anchor rows
    /// {cx, cy, w, h, conf}.</summary>
    private static float[] BuildTensor(params float[][] anchors)
    {
        var flat = new float[5 * anchors.Length];
        for (var row = 0; row < 5; row++)
        {
            for (var i = 0; i < anchors.Length; i++)
            {
                flat[(row * anchors.Length) + i] = anchors[i][row];
            }
        }
        return flat;
    }

    [Fact]
    public void ReturnsNullWhenNothingClearsTheThreshold()
    {
        var tensor = BuildTensor(
            new float[] { 100, 100, 10, 10, 0.05f },
            new float[] { 50, 50, 10, 10, 0.02f });
        var transform = new LetterboxTransform(1.0, 0, 0);

        var result = YoloOutputDecoder.Decode(tensor, 2, transform, 320, 320, confThreshold: 0.1, ballDiameterMm: 42.67);

        Assert.Null(result);
    }

    [Fact]
    public void PicksTheHighestConfidenceAnchorAboveThreshold()
    {
        var tensor = BuildTensor(
            new float[] { 100, 100, 10, 10, 0.20f },
            new float[] { 200, 150, 16, 16, 0.80f }, // the winner
            new float[] { 50, 50, 8, 8, 0.30f });
        var transform = new LetterboxTransform(1.0, 0, 0);

        var result = YoloOutputDecoder.Decode(tensor, 3, transform, 320, 320, confThreshold: 0.1, ballDiameterMm: 42.67);

        Assert.NotNull(result);
        Assert.Equal(0.80, result!.Confidence, precision: 6);
        Assert.Equal(200.0 / 320.0, result.X, precision: 6);
        Assert.Equal(150.0 / 320.0, result.Y, precision: 6);
    }

    [Fact]
    public void InvertsLetterboxScaleAndPaddingCorrectly()
    {
        // Matches the real exported model's measured output shape on a real corpus
        // frame: scale=0.25, pad_y=70, pad_x=0 (1280x720 -> fit into 320x320), decoded
        // box at cx=120.35, cy=151.34, w=8.17, h=8.21 in padded-canvas pixel space ->
        // original-frame xyxy ~= (465.06, 308.94, 497.75, 341.78), matching ultralytics'
        // own reference detection on this exact frame (see PROGRESS.md).
        var tensor = BuildTensor(new float[] { 120.35f, 151.34f, 8.17f, 8.21f, 0.0812f });
        var transform = new LetterboxTransform(0.25, 0, 70);

        var result = YoloOutputDecoder.Decode(tensor, 1, transform, 1280, 720, confThreshold: 0.01, ballDiameterMm: 42.67);

        Assert.NotNull(result);
        var pixelX = result!.X * 1280;
        var pixelY = result.Y * 720;
        Assert.InRange(pixelX, 481.0, 481.8);
        Assert.InRange(pixelY, 325.0, 325.8);
    }

    [Fact]
    public void PixelsPerMmDerivedFromBoundingBoxWidth()
    {
        // 320px frame, box width 32px in a transform with scale=1 and no padding ->
        // 32px wide in original-frame pixels -> ppm = 32 / 42.67.
        var tensor = BuildTensor(new float[] { 160, 160, 32, 32, 0.9f });
        var transform = new LetterboxTransform(1.0, 0, 0);

        var result = YoloOutputDecoder.Decode(tensor, 1, transform, 320, 320, confThreshold: 0.1, ballDiameterMm: 42.67);

        Assert.NotNull(result);
        Assert.Equal(32.0 / 42.67, result!.PixelsPerMm, precision: 6);
    }
}
