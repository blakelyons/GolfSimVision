using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GolfSimVision.Detection;

/// <summary>Seam over BallDetector so PuttDetectionPipeline's orchestration (zone state
/// machine wiring, calibration unit conversion) is testable with a scripted fake,
/// without needing the real ONNX model.</summary>
public interface IBallDetector
{
    BallDetection? Detect(byte[] bgrBytes, int width, int height);
}

/// <summary>
/// YOLO ball detector via ONNX Runtime, ported from prototypes/webcam-putting/
/// detection/detector.py's BallDetector. The fine-tuned model is single-class
/// ("golf_ball", class 0) -- this port drops detector.py's COCO-class/multi-model
/// fallback machinery (a dev convenience for "weights not found," not something a
/// shipped app should silently substitute for).
///
/// The model itself is not committed to this repo (*.onnx, see .gitignore, matching the
/// project's "ship binaries via releases" convention). Export it from the trained
/// checkpoint with:
///   from ultralytics import YOLO
///   YOLO("prototypes/webcam-putting/detection/models/golf_ball_v1.pt") \
///       .export(format="onnx", imgsz=320, opset=12)
/// and place the result at desktop/GolfSimVision/Models/golf_ball_v1.onnx. Preprocessing
/// (Letterbox) and output decoding (YoloOutputDecoder) were cross-checked against this
/// exact exported model's real output on a real corpus frame, not assumed from
/// documentation -- see PROGRESS.md.
/// </summary>
public sealed class BallDetector : IBallDetector, IDisposable
{
    public const double GolfBallDiameterMm = 42.67;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _imgSize;
    private readonly double _confThreshold;

    public BallDetector(string modelPath, double confThreshold = 0.15, int imgSize = 320)
    {
        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
        _confThreshold = confThreshold;
        _imgSize = imgSize;
    }

    /// <summary>True if a model file exists at the given path -- lets callers check
    /// before constructing, mirroring UsbWebcam's graceful-failure pattern rather than
    /// letting a missing model surface as a raw exception.</summary>
    public static bool ModelExists(string modelPath) => System.IO.File.Exists(modelPath);

    public BallDetection? Detect(byte[] bgrBytes, int width, int height)
    {
        var pre = Letterbox.Preprocess(bgrBytes, width, height, _imgSize);
        var inputTensor = new DenseTensor<float>(pre.Tensor, new[] { 1, 3, _imgSize, _imgSize });
        var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();
        var numAnchors = output.Dimensions[2];
        var flat = output.ToArray();

        return YoloOutputDecoder.Decode(flat, numAnchors, pre.Transform, width, height, _confThreshold, GolfBallDiameterMm);
    }

    /// <summary>
    /// Detects within a normalized-coordinate crop, returning a full-frame-normalized
    /// BallDetection -- ported from detector.py's detect_in_roi. Cropping makes the ball
    /// appear larger in the model's input tensor, which can push a small/distant ball
    /// into a more reliably-detected size range. PixelsPerMm needs no rescaling: a crop
    /// is a plain slice, not a resize/zoom, so it is already in the same native
    /// pixel-density units as the full frame.
    /// </summary>
    public BallDetection? DetectInRoi(byte[] bgrBytes, int width, int height, double x1n, double y1n, double x2n, double y2n)
    {
        var x1 = (int)(x1n * width);
        var y1 = (int)(y1n * height);
        var x2 = (int)(x2n * width);
        var y2 = (int)(y2n * height);
        if (x2 <= x1 || y2 <= y1) return null;

        var cropWidth = x2 - x1;
        var cropHeight = y2 - y1;
        var crop = CropBgr(bgrBytes, width, x1, y1, cropWidth, cropHeight);

        var det = Detect(crop, cropWidth, cropHeight);
        if (det is null) return null;

        var fullX = x1n + (det.X * (x2n - x1n));
        var fullY = y1n + (det.Y * (y2n - y1n));
        var fullW = det.W * (x2n - x1n);
        var fullH = det.H * (y2n - y1n);
        return det with { X = fullX, Y = fullY, W = fullW, H = fullH };
    }

    private static byte[] CropBgr(byte[] bgrBytes, int sourceWidth, int x, int y, int cropWidth, int cropHeight)
    {
        var cropped = new byte[cropWidth * cropHeight * 3];
        for (var row = 0; row < cropHeight; row++)
        {
            Buffer.BlockCopy(bgrBytes, (((y + row) * sourceWidth) + x) * 3, cropped, row * cropWidth * 3, cropWidth * 3);
        }
        return cropped;
    }

    public void Dispose() => _session.Dispose();
}
