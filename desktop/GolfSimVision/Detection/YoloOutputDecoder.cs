namespace GolfSimVision.Detection;

/// <summary>
/// Decodes a YOLOv8/v11-style ONNX output tensor (shape [1, 5, numAnchors]: rows 0-3 are
/// box cx/cy/w/h in the padded letterboxed-input pixel space, row 4 is a confidence
/// already sigmoid-activated by the export graph -- verified empirically against the
/// real exported model, not assumed) into the single highest-confidence BallDetection in the
/// original frame's coordinate space, or null if nothing clears the threshold.
///
/// Deliberately does not run NMS: this detector only ever wants the single best
/// detection (BallDetector.Detect's whole contract, ported from detector.py), and the
/// global highest-confidence anchor is identical whether or not NMS runs first --
/// greedy NMS processes the highest-confidence box first and can never suppress it, so
/// skipping NMS changes nothing for a top-1 result while avoiding machinery this
/// contract doesn't need.
///
/// Pure function, no OpenCvSharp or ONNX Runtime involved -- independently testable
/// against a hand-constructed tensor, which is exactly how its correctness was verified
/// here (cross-checked against the real model's raw output on a real corpus frame).
/// </summary>
public static class YoloOutputDecoder
{
    public static BallDetection? Decode(
        float[] output,
        int numAnchors,
        LetterboxTransform transform,
        int originalWidth,
        int originalHeight,
        double confThreshold,
        double ballDiameterMm)
    {
        var bestIdx = -1;
        var bestConf = double.NegativeInfinity;
        for (var i = 0; i < numAnchors; i++)
        {
            var conf = output[(4 * numAnchors) + i];
            if (conf > bestConf)
            {
                bestConf = conf;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || bestConf < confThreshold) return null;

        var cx = output[(0 * numAnchors) + bestIdx];
        var cy = output[(1 * numAnchors) + bestIdx];
        var bw = output[(2 * numAnchors) + bestIdx];
        var bh = output[(3 * numAnchors) + bestIdx];

        // Invert the letterbox: remove padding, then undo the scale-to-fit resize.
        var x1 = (cx - (bw / 2) - transform.PadX) / transform.Scale;
        var y1 = (cy - (bh / 2) - transform.PadY) / transform.Scale;
        var x2 = (cx + (bw / 2) - transform.PadX) / transform.Scale;
        var y2 = (cy + (bh / 2) - transform.PadY) / transform.Scale;

        var pixelWidth = x2 - x1;
        var normX = (x1 + x2) / 2 / originalWidth;
        var normY = (y1 + y2) / 2 / originalHeight;
        var normW = pixelWidth / originalWidth;
        var normH = (y2 - y1) / originalHeight;
        var pixelsPerMm = pixelWidth > 0 ? pixelWidth / ballDiameterMm : 0.0;

        return new BallDetection(normX, normY, normW, normH, bestConf, pixelsPerMm);
    }
}
