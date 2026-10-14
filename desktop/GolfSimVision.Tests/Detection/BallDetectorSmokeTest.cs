using System;
using System.IO;
using GolfSimVision.Detection;
using OpenCvSharp;
using Xunit;

namespace GolfSimVision.Tests.Detection;

/// <summary>
/// Runs the real exported model against a real, hand-labelled corpus frame -- both
/// gitignored assets, present only on a machine that has exported the model and
/// collected the Phase 0 corpus. Skips silently (no assertion, no failure) when either
/// is absent, which is the normal case on a fresh clone -- this is a verification tool
/// for whoever has the assets locally, not a hard requirement of the committed suite.
/// </summary>
public class BallDetectorSmokeTest
{
    private static readonly string ModelPath = Path.Combine(
        AppContext.BaseDirectory, "Models", "golf_ball_v1.onnx");

    // clip_0026, frame 0 -- hand-labelled ground truth (labels_tier2.json) places the
    // ball at approximately (482, 322) in this 1280x720 frame. Anchored off
    // AppContext.BaseDirectory, not the current working directory -- `dotnet test`'s CWD
    // is the invocation directory, not the test binary's own directory, and a bare
    // relative path here resolved from the wrong place and silently skipped every run.
    private static readonly string CorpusFramePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "prototypes", "corpus", "corpus", "clip_0026", "frames", "000000.png"));

    [Fact]
    public void DetectsTheBallNearItsHandLabelledPosition()
    {
        if (!File.Exists(ModelPath) || !File.Exists(CorpusFramePath)) return;

        using var gray = Cv2.ImRead(CorpusFramePath, ImreadModes.Grayscale);
        using var bgr = new Mat();
        Cv2.CvtColor(gray, bgr, ColorConversionCodes.GRAY2BGR);
        var bytes = new byte[bgr.Total() * bgr.ElemSize()];
        System.Runtime.InteropServices.Marshal.Copy(bgr.Data, bytes, 0, bytes.Length);

        using var detector = new BallDetector(ModelPath, confThreshold: 0.03);
        var detection = detector.Detect(bytes, bgr.Width, bgr.Height);

        Assert.NotNull(detection);
        var pixelX = detection!.X * bgr.Width;
        var pixelY = detection.Y * bgr.Height;
        // Generous tolerance: this is a grayscale-replicated frame feeding a model
        // trained on color, so confidence is low (see PROGRESS.md) -- the point is
        // localization, not confidence, matching the same frame's ultralytics reference
        // detection at ~(481, 325).
        Assert.InRange(pixelX, 460, 505);
        Assert.InRange(pixelY, 300, 350);
    }
}
