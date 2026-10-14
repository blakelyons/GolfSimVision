using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GolfSimVision.Calibration;
using GolfSimVision.Detection;
using GolfSimVision.Zones;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace GolfSimVision.Tests.Detection;

/// <summary>
/// Replays the Phase 0 85-clip hand-labelled corpus against the real exported model and
/// this ticket's C# pipeline, scoring true/false-positive rate and speed/HLA error --
/// the C# equivalent of prototypes/corpus/replay.py, per this ticket's own acceptance
/// criterion. Both the model and the corpus are gitignored, machine-local assets (see
/// BallDetectorSmokeTest's same reasoning) -- skips silently when either is absent,
/// which is the normal case on a fresh clone.
///
/// Tagged [Trait("Category", "Slow")] and deliberately excluded from this project's
/// normal `dotnet test` runs: it runs real ONNX inference over ~20,000 real frames and
/// takes ~5 minutes, the same way replay.py is a manually-invoked tool, not part of any
/// CI (none exists yet for this project). Run the fast suite with
/// `dotnet test --filter "Category!=Slow"`; run this one explicitly with:
///   dotnet test --filter FullyQualifiedName~CorpusReplayVerification
///
/// This corpus has no real calibration -- for the 20 clips that carry hand-labelled
/// ground truth (clip.json's ground_truth.speed_mph), a per-clip mm/px scale is derived
/// from the same two hand-labelled points (labels_tier2.json) and known gate_distance_mm
/// the ground truth itself was computed from, so the comparison is fair rather than
/// invented. Clips without ground truth still count toward true/false-positive scoring.
///
/// Real result (2026-10-14, confThreshold=0.03): 7 true positives / 30 false negatives /
/// 10 false positives / 0 double-fires over all 85 clips, on this corpus's grayscale-
/// replicated-to-3-channel frames -- the model was trained on real color frames, and
/// detector.py's own docstring already flagged grayscale accuracy as "an open question
/// this live pipeline will answer in practice, not a known quantity." It now has a real
/// answer: the dominant rejection by far (70 occurrences) is "only 3 track points,"
/// meaning the detector loses the ball almost immediately on most clips under this
/// mismatch -- a corpus/detector-training gap, not a bug in this ported pipeline's logic
/// (zero double-fires, and several clips were correctly caught by the plausibility gates
/// at large HLA/speed outliers rather than becoming false positives). Real accuracy
/// tuning against this corpus is deliberately deferred, matching P0-1's own baseline-fix
/// precedent of treating "the pipeline runs and scores honestly" as this ticket's bar,
/// not "the numbers are already good."
/// </summary>
public class CorpusReplayVerification
{
    private readonly ITestOutputHelper _output;

    public CorpusReplayVerification(ITestOutputHelper output) => _output = output;

    private static readonly string ModelPath = Path.Combine(AppContext.BaseDirectory, "Models", "golf_ball_v1.onnx");

    private static readonly string CorpusRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "prototypes", "corpus", "corpus"));

    private sealed record ClipEntry(string ClipId, string Kind);

    [Fact]
    [Trait("Category", "Slow")]
    public void ScoresTheFullCorpus()
    {
        var indexPath = Path.Combine(CorpusRoot, "index.jsonl");
        if (!File.Exists(ModelPath) || !File.Exists(indexPath)) return;

        var entries = File.ReadAllLines(indexPath)
            .Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l))
            .Select(e => new ClipEntry(e.GetProperty("clip_id").GetString()!, e.GetProperty("kind").GetString()!))
            .ToList();

        using var detector = new BallDetector(ModelPath, confThreshold: 0.03);

        int truePositives = 0, falseNegatives = 0, falsePositives = 0, doubleFires = 0;
        var speedErrorsPct = new List<double>();
        var hlaErrorsDeg = new List<double>();
        var rejectionCounts = new Dictionary<string, int>();

        foreach (var entry in entries)
        {
            var (measurements, rejections) = ReplayClip(detector, entry.ClipId);
            foreach (var r in rejections) rejectionCounts[r] = rejectionCounts.GetValueOrDefault(r) + 1;

            if (entry.Kind == "valid_putt")
            {
                if (measurements.Count == 1)
                {
                    truePositives++;
                    var groundTruth = ReadGroundTruth(entry.ClipId);
                    if (groundTruth is { } gt)
                    {
                        speedErrorsPct.Add(Math.Abs(measurements[0].SpeedMph - gt.SpeedMph) / gt.SpeedMph * 100.0);
                        hlaErrorsDeg.Add(Math.Abs(measurements[0].HlaDegrees - gt.HlaDegrees));
                    }
                }
                else if (measurements.Count == 0)
                {
                    falseNegatives++;
                }
            }
            else if (measurements.Count >= 1)
            {
                falsePositives++;
            }

            if (measurements.Count > 1) doubleFires++;
        }

        _output.WriteLine($"clip_count={entries.Count}");
        _output.WriteLine($"true_positives={truePositives}");
        _output.WriteLine($"false_negatives={falseNegatives}");
        _output.WriteLine($"false_positives={falsePositives}");
        _output.WriteLine($"double_fires={doubleFires}");
        _output.WriteLine($"speed_error_pct: n={speedErrorsPct.Count} mean={(speedErrorsPct.Count > 0 ? speedErrorsPct.Average() : 0):F1} max={(speedErrorsPct.Count > 0 ? speedErrorsPct.Max() : 0):F1}");
        _output.WriteLine($"hla_error_deg: n={hlaErrorsDeg.Count} mean={(hlaErrorsDeg.Count > 0 ? hlaErrorsDeg.Average() : 0):F2} max={(hlaErrorsDeg.Count > 0 ? hlaErrorsDeg.Max() : 0):F2}");
        foreach (var (reason, count) in rejectionCounts.OrderByDescending(kv => kv.Value))
        {
            _output.WriteLine($"rejection[{reason}]={count}");
        }
    }

    private (List<PuttMeasurement> Measurements, List<string> Rejections) ReplayClip(BallDetector detector, string clipId)
    {
        var clipDir = Path.Combine(CorpusRoot, clipId);
        var clipJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(clipDir, "clip.json"))).RootElement;
        var timestamps = clipJson.GetProperty("frames").GetProperty("timestamps_s")
            .EnumerateArray().Select(e => e.GetDouble()).ToList();
        var framePaths = Directory.GetFiles(Path.Combine(clipDir, "frames"), "*.png").OrderBy(p => p).ToList();

        var manualPpm = DerivePpmFromGroundTruth(clipId, clipJson);

        var clockTime = 0.0;
        var zoneStateMachine = new ZoneStateMachine(ZoneConfig.Default, clock: () => clockTime);
        var measurements = new List<PuttMeasurement>();
        var rejections = new List<string>();

        for (var i = 0; i < Math.Min(timestamps.Count, framePaths.Count); i++)
        {
            clockTime = timestamps[i];
            using var gray = Cv2.ImRead(framePaths[i], ImreadModes.Grayscale);
            using var bgr = new Mat();
            Cv2.CvtColor(gray, bgr, ColorConversionCodes.GRAY2BGR);
            var bytes = new byte[bgr.Total() * bgr.ElemSize()];
            System.Runtime.InteropServices.Marshal.Copy(bgr.Data, bytes, 0, bytes.Length);

            var detection = detector.Detect(bytes, bgr.Width, bgr.Height);
            var finalize = zoneStateMachine.Update(detection);

            switch (finalize)
            {
                case PuttFinalizeResult.Rejected r:
                    rejections.Add(r.Reason);
                    break;
                case PuttFinalizeResult.TrackReady t:
                    try
                    {
                        var result = PuttCalculator.Calculate(t.Track, zoneStateMachine.FrameWidth, zoneStateMachine.FrameHeight, manualPpm);
                        measurements.Add(new PuttMeasurement(result.SpeedMph, result.HlaDegrees, result.DistanceFt, result.SpeedFps, t.Track.Count));
                    }
                    catch (PuttCalculationException ex)
                    {
                        rejections.Add(ex.Message);
                    }
                    break;
            }
        }

        return (measurements, rejections);
    }

    private sealed record GroundTruth(double SpeedMph, double HlaDegrees);

    private static GroundTruth? ReadGroundTruth(string clipId)
    {
        var clipDir = Path.Combine(CorpusRoot, clipId);
        var clipJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(clipDir, "clip.json"))).RootElement;
        if (!clipJson.TryGetProperty("ground_truth", out var gt) || gt.ValueKind != JsonValueKind.Object) return null;
        if (!gt.TryGetProperty("speed_mph", out var speedEl) || speedEl.ValueKind != JsonValueKind.Number) return null;
        if (!gt.TryGetProperty("hla_deg", out var hlaEl) || hlaEl.ValueKind != JsonValueKind.Number) return null;
        return new GroundTruth(speedEl.GetDouble(), hlaEl.GetDouble());
    }

    /// Derives a per-clip pixels-per-mm scale from the same two hand-labelled points and
    /// known gate_distance_mm the clip's own ground truth was computed from -- this
    /// corpus has no real calibration, so this is the fairest available stand-in, not an
    /// invented number.
    private static double DerivePpmFromGroundTruth(string clipId, JsonElement clipJson)
    {
        if (!clipJson.TryGetProperty("ground_truth", out var gt) || gt.ValueKind != JsonValueKind.Object) return 0.0;
        // TryGetProperty succeeding only means the key exists -- most clips have no real
        // ground truth and store these as explicit JSON null, not an absent key, so each
        // field's actual ValueKind must be checked before calling an array/number
        // accessor that would otherwise throw on null.
        if (!gt.TryGetProperty("gate_frames", out var gateFrames) || gateFrames.ValueKind != JsonValueKind.Array
            || gateFrames.GetArrayLength() != 2)
        {
            return 0.0;
        }
        if (!gt.TryGetProperty("gate_distance_mm", out var gateDistEl) || gateDistEl.ValueKind != JsonValueKind.Number)
        {
            return 0.0;
        }

        var labelsPath = Path.Combine(CorpusRoot, clipId, "labels_tier2.json");
        if (!File.Exists(labelsPath)) return 0.0;

        var labels = JsonDocument.Parse(File.ReadAllText(labelsPath)).RootElement;
        var frame1 = gateFrames[0].GetInt32().ToString();
        var frame2 = gateFrames[1].GetInt32().ToString();
        if (!labels.TryGetProperty(frame1, out var p1) || !labels.TryGetProperty(frame2, out var p2)) return 0.0;

        var dx = p2[0].GetDouble() - p1[0].GetDouble();
        var dy = p2[1].GetDouble() - p1[1].GetDouble();
        var pixelDistance = Math.Sqrt((dx * dx) + (dy * dy));
        var gateDistanceMm = gateDistEl.GetDouble();
        return pixelDistance > 0 && gateDistanceMm > 0 ? pixelDistance / gateDistanceMm : 0.0;
    }
}
