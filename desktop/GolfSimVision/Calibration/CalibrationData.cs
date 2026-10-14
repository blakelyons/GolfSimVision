using System;

namespace GolfSimVision.Calibration;

/// <summary>
/// A completed calibration, stored and versioned (ADR-0005: "Calibration is versioned
/// and stored; a re-calibration prompt fires if the camera moves" -- the prompt is a
/// later ticket's job, this is the versioned artifact it would key off of).
/// MillimetersPerPixel is a representative cross-check value only -- never the primary
/// ruler; Homography is the actual per-point transform the detection pipeline uses.
/// </summary>
public sealed record CalibrationData(
    HomographyMatrix Homography,
    double MillimetersPerPixel,
    int Version,
    DateTimeOffset CalibratedAtUtc);
