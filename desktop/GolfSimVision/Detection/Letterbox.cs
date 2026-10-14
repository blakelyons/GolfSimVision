using System;
using OpenCvSharp;

namespace GolfSimVision.Detection;

/// <summary>Scale and padding applied by letterboxing, needed to map a detected box back
/// from the padded square input back to the original frame's pixel space.</summary>
public sealed record LetterboxTransform(double Scale, double PadX, double PadY);

public sealed record LetterboxResult(float[] Tensor, LetterboxTransform Transform);

/// <summary>
/// Standard YOLO preprocessing: scale-to-fit (preserving aspect ratio), pad with gray
/// (114) to a square, BGR-to-RGB, normalize to [0,1], NCHW layout. One of two classes in
/// this module that touch OpenCvSharp (the other is BallDetector's crop helper) -- used
/// for resizing specifically because this exact scale/pad math was cross-checked against
/// the real exported model's ONNX output and ultralytics' own reference inference on a
/// real corpus frame (see PROGRESS.md), not assumed correct from documentation alone.
/// </summary>
public static class Letterbox
{
    private const byte PadValue = 114;

    public static LetterboxResult Preprocess(byte[] bgrBytes, int width, int height, int targetSize)
    {
        using var mat = Mat.FromPixelData(height, width, MatType.CV_8UC3, bgrBytes);

        var scale = Math.Min((double)targetSize / width, (double)targetSize / height);
        var newWidth = (int)Math.Round(width * scale);
        var newHeight = (int)Math.Round(height * scale);

        using var resized = new Mat();
        Cv2.Resize(mat, resized, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);

        using var canvas = new Mat(targetSize, targetSize, MatType.CV_8UC3, new Scalar(PadValue, PadValue, PadValue));
        var padX = (targetSize - newWidth) / 2;
        var padY = (targetSize - newHeight) / 2;
        using (var roi = new Mat(canvas, new Rect(padX, padY, newWidth, newHeight)))
        {
            resized.CopyTo(roi);
        }

        var tensor = new float[3 * targetSize * targetSize];
        var plane = targetSize * targetSize;
        for (var y = 0; y < targetSize; y++)
        {
            for (var x = 0; x < targetSize; x++)
            {
                var pixel = canvas.At<Vec3b>(y, x); // OpenCV native order: B, G, R
                var idx = y * targetSize + x;
                tensor[(0 * plane) + idx] = pixel.Item2 / 255f; // R
                tensor[(1 * plane) + idx] = pixel.Item1 / 255f; // G
                tensor[(2 * plane) + idx] = pixel.Item0 / 255f; // B
            }
        }

        return new LetterboxResult(tensor, new LetterboxTransform(scale, padX, padY));
    }
}
