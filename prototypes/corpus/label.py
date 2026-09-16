"""Frame-stepper labelling tool.

Per docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 3: two ground-truth
methods, plus tier-2 per-frame ball labels.

- Two-gate manual: a human frame-steps to find the frame where the ball centre crosses
  each of two marks a measured distance apart; speed = gate_distance_mm / dt.
- Ramp release height: v = sqrt(10 * g * h / 7) for a solid sphere rolling without slip.

The frame-stepper (arrow keys to step, click to mark, G to mark a gate crossing) is the
interactive shell -- not unit tested here, only manually run. The math and the
clip.json read-modify-write around it are pure/file-I/O and are covered in test_label.py.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import cv2

from corpus import GroundTruth, frames_dir, load_clip, save_clip

GRAVITY_M_S2 = 9.80665


def two_gate_speed_mph(timestamps_s: list[float], gate1_frame: int, gate2_frame: int, gate_distance_mm: float) -> float:
    """Speed in mph from the elapsed time between two marked gate-crossing frames."""
    dt = timestamps_s[gate2_frame] - timestamps_s[gate1_frame]
    if dt <= 0:
        raise ValueError(f"gate2 (frame {gate2_frame}) must be after gate1 (frame {gate1_frame})")
    speed_mm_s = gate_distance_mm / dt
    return speed_mm_s * 0.00223694  # mm/s -> mph


def ramp_release_speed_mph(height_cm: float) -> float:
    """v = sqrt(10 * g * h / 7) for a solid sphere rolling without slipping."""
    h_m = height_cm / 100.0
    v_m_s = (10.0 * GRAVITY_M_S2 * h_m / 7.0) ** 0.5
    return v_m_s * 2.23694  # m/s -> mph


def record_two_gate_ground_truth(
    corpus_root: Path,
    clip_id: str,
    gate1_frame: int,
    gate2_frame: int,
    gate_distance_mm: float,
    hla_deg: float = 0.0,
    notes: str = "",
) -> GroundTruth:
    """Compute two-gate speed and write it into clip.json's ground_truth section."""
    meta = load_clip(corpus_root, clip_id)
    speed = two_gate_speed_mph(meta.frames.timestamps_s, gate1_frame, gate2_frame, gate_distance_mm)
    meta.ground_truth = GroundTruth(
        method="two_gate_manual",
        speed_mph=speed,
        hla_deg=hla_deg,
        gate_distance_mm=gate_distance_mm,
        gate_frames=[gate1_frame, gate2_frame],
        notes=notes,
    )
    save_clip(corpus_root, meta)
    return meta.ground_truth


def record_ramp_ground_truth(
    corpus_root: Path, clip_id: str, release_height_cm: float, hla_deg: float = 0.0, notes: str = ""
) -> GroundTruth:
    """Compute ramp-release speed and write it into clip.json's ground_truth section."""
    meta = load_clip(corpus_root, clip_id)
    speed = ramp_release_speed_mph(release_height_cm)
    meta.ground_truth = GroundTruth(
        method="ramp_release_height",
        speed_mph=speed,
        hla_deg=hla_deg,
        notes=notes or f"release height {release_height_cm}cm",
    )
    save_clip(corpus_root, meta)
    return meta.ground_truth


def mark_tier2_labelled(corpus_root: Path, clip_id: str) -> None:
    """Flip labels_tier2 to True once per-frame ball positions have been recorded."""
    meta = load_clip(corpus_root, clip_id)
    meta.labels_tier2 = True
    save_clip(corpus_root, meta)


def save_tier2_labels(corpus_root: Path, clip_id: str, labels: dict[int, tuple[int, int]]) -> Path:
    """Write per-frame ball-centre (x, y) labels to <clip_dir>/labels_tier2.json."""
    path = corpus_root / clip_id / "labels_tier2.json"
    path.write_text(
        json.dumps({str(k): list(v) for k, v in sorted(labels.items())}, indent=2),
        encoding="utf-8",
    )
    mark_tier2_labelled(corpus_root, clip_id)
    return path


def _run_stepper(corpus_root: Path, clip_id: str) -> None:
    """Interactive frame-stepper: arrow keys to step, click to mark, G for a gate crossing."""
    frame_paths = sorted(frames_dir(corpus_root, clip_id).glob("*.png"))
    if not frame_paths:
        print(f"[label] no frames found for {clip_id}")
        return

    idx = 0
    gate_frames: list[int] = []
    clicked_points: dict[int, tuple[int, int]] = {}

    def on_click(event, x, y, flags, userdata):
        if event == cv2.EVENT_LBUTTONDOWN:
            clicked_points[idx] = (x, y)
            print(f"[label] frame {idx}: marked ball at ({x}, {y})")

    cv2.namedWindow("label")
    cv2.setMouseCallback("label", on_click)

    print("[label] LEFT/RIGHT=step  G=mark gate crossing  Q=quit")
    while True:
        frame = cv2.imread(str(frame_paths[idx]))
        cv2.putText(frame, f"frame {idx}/{len(frame_paths) - 1}", (10, 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
        cv2.imshow("label", frame)
        key = cv2.waitKeyEx(0)
        if key in (2424832, ord("a")):  # left arrow
            idx = max(0, idx - 1)
        elif key in (2555904, ord("d")):  # right arrow
            idx = min(len(frame_paths) - 1, idx + 1)
        elif key == ord("g"):
            gate_frames.append(idx)
            print(f"[label] gate crossing marked at frame {idx} ({gate_frames})")
        elif key == ord("q"):
            break

    cv2.destroyAllWindows()
    if len(gate_frames) >= 2:
        print(f"[label] two-gate frames: {gate_frames[:2]} -- use --record-two-gate to save")
    if clicked_points:
        save_tier2_labels(corpus_root, clip_id, clicked_points)
        print(f"[label] saved {len(clicked_points)} tier-2 labels")


def main() -> None:
    parser = argparse.ArgumentParser(description="Frame-stepper ground-truth/labelling tool.")
    parser.add_argument("--corpus", default="corpus", help="path to corpus root")
    parser.add_argument("clip_id", help="e.g. clip_0001")
    parser.add_argument("--record-two-gate", nargs=3, type=float, metavar=("GATE1_FRAME", "GATE2_FRAME", "GATE_DISTANCE_MM"))
    parser.add_argument("--record-ramp", type=float, metavar="HEIGHT_CM")
    parser.add_argument("--hla", type=float, default=0.0)
    args = parser.parse_args()

    if args.record_two_gate:
        g1, g2, dist = args.record_two_gate
        gt = record_two_gate_ground_truth(Path(args.corpus), args.clip_id, int(g1), int(g2), dist, args.hla)
        print(f"[label] {args.clip_id}: {gt.speed_mph:.2f} mph (two-gate)")
    elif args.record_ramp is not None:
        gt = record_ramp_ground_truth(Path(args.corpus), args.clip_id, args.record_ramp, args.hla)
        print(f"[label] {args.clip_id}: {gt.speed_mph:.2f} mph (ramp release)")
    else:
        _run_stepper(Path(args.corpus), args.clip_id)


if __name__ == "__main__":
    main()
