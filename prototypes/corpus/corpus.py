"""Shared clip.json / index.jsonl schema, read/write.

Every other tool in this harness (capture.py, label.py, replay.py, stats.py) reads and
writes clips through this module instead of touching JSON directly, so the on-disk shape
stays in exactly one place. Field names and nesting match the `clip.json` example in
docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 1 verbatim.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path


@dataclass
class CameraProps:
    requested: dict
    actual: dict
    source: str = "usb_webcam"
    device_name: str = ""
    exposure_locked: bool = False
    exposure_note: str = ""
    props: dict = field(default_factory=dict)


@dataclass
class Geometry:
    camera_height_in: float | None = None
    camera_fov_deg: float | None = None
    mount: str = ""


@dataclass
class Conditions:
    lighting: str = ""
    surface: str = ""
    ball: str = ""


@dataclass
class Frames:
    count: int
    first_index: int
    timestamps_s: list[float]


@dataclass
class Timing:
    measured_fps: float
    interval_ms: dict
    gap_count: int


@dataclass
class GroundTruth:
    method: str | None = None
    speed_mph: float | None = None
    hla_deg: float | None = None
    gate_distance_mm: float | None = None
    gate_frames: list[int] | None = None
    notes: str = ""


@dataclass
class ClipMetadata:
    clip_id: str
    created_utc: str
    kind: str  # "valid_putt" | "adversarial" | ...
    camera: CameraProps
    frames: Frames
    timing: Timing
    geometry: Geometry = field(default_factory=Geometry)
    conditions: Conditions = field(default_factory=Conditions)
    ground_truth: GroundTruth = field(default_factory=GroundTruth)
    labels_tier2: bool = False
    notes: str = ""


def clip_dir(corpus_root: Path, clip_id: str) -> Path:
    return corpus_root / clip_id


def frames_dir(corpus_root: Path, clip_id: str) -> Path:
    return clip_dir(corpus_root, clip_id) / "frames"


def clip_json_path(corpus_root: Path, clip_id: str) -> Path:
    return clip_dir(corpus_root, clip_id) / "clip.json"


def save_clip(corpus_root: Path, meta: ClipMetadata) -> Path:
    """Write clip.json for meta.clip_id, creating its directory if needed."""
    d = clip_dir(corpus_root, meta.clip_id)
    d.mkdir(parents=True, exist_ok=True)
    path = clip_json_path(corpus_root, meta.clip_id)
    path.write_text(json.dumps(asdict(meta), indent=2), encoding="utf-8")
    return path


def load_clip(corpus_root: Path, clip_id: str) -> ClipMetadata:
    raw = json.loads(clip_json_path(corpus_root, clip_id).read_text(encoding="utf-8"))
    return ClipMetadata(
        clip_id=raw["clip_id"],
        created_utc=raw["created_utc"],
        kind=raw["kind"],
        camera=CameraProps(**raw["camera"]),
        frames=Frames(**raw["frames"]),
        timing=Timing(**raw["timing"]),
        geometry=Geometry(**raw.get("geometry", {})),
        conditions=Conditions(**raw.get("conditions", {})),
        ground_truth=GroundTruth(**raw.get("ground_truth", {})),
        labels_tier2=raw.get("labels_tier2", False),
        notes=raw.get("notes", ""),
    )


def index_path(corpus_root: Path) -> Path:
    return corpus_root / "index.jsonl"


def append_index_entry(corpus_root: Path, meta: ClipMetadata) -> None:
    """Append one flattened summary line for meta to index.jsonl."""
    entry = {
        "clip_id": meta.clip_id,
        "created_utc": meta.created_utc,
        "kind": meta.kind,
        "frame_count": meta.frames.count,
        "measured_fps": meta.timing.measured_fps,
        "labels_tier2": meta.labels_tier2,
        "has_ground_truth": meta.ground_truth.method is not None,
    }
    corpus_root.mkdir(parents=True, exist_ok=True)
    with index_path(corpus_root).open("a", encoding="utf-8") as f:
        f.write(json.dumps(entry) + "\n")


def read_index(corpus_root: Path) -> list[dict]:
    p = index_path(corpus_root)
    if not p.exists():
        return []
    lines = p.read_text(encoding="utf-8").splitlines()
    return [json.loads(line) for line in lines if line.strip()]


def next_clip_id(corpus_root: Path) -> str:
    """Smallest unused clip_NNNN, scanning existing clip directories."""
    existing = set()
    if corpus_root.exists():
        for p in corpus_root.iterdir():
            if p.is_dir() and p.name.startswith("clip_"):
                existing.add(p.name)
    n = 1
    while f"clip_{n:04d}" in existing:
        n += 1
    return f"clip_{n:04d}"
