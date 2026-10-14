"""YOLO ball detector -- ported from V1 (SimLinkPutting)'s
backend/detection/detector.py, unmodified except the default model_path (now
points at this project's copy of the fine-tuned weights, models/golf_ball_v1.pt
-- V1's own config.py reference to that filename didn't actually exist on disk;
the real trained weights live at backend/runs/detect/golf_ball_auto/weights/best.pt
in V1, copied here under the name V1's config expected).

Color-agnostic: detect()/detect_in_roi() pass the frame straight into the YOLO
model with no cvtColor, so a grayscale-replicated-to-3-channel frame works with
no code changes -- see live_putt.py for the replication step. The fine-tuned
model was trained on color frames, so accuracy on replicated grayscale is an
open question this live pipeline will answer in practice, not a known quantity.
"""

import logging
import numpy as np
from dataclasses import dataclass
from typing import Optional
from pathlib import Path

log = logging.getLogger(__name__)

GOLF_BALL_DIAMETER_MM = 42.67
SPORTS_BALL_CLASS_ID = 32  # COCO class index for "sports ball"
GOLF_BALL_NAMES = {"golf_ball", "golfball", "golf ball", "ball"}  # fine-tuned model names

DEFAULT_MODEL_PATH = Path(__file__).parent / "models" / "golf_ball_v1.pt"


def _resolve_device(preference: str) -> str:
    if preference == "auto":
        try:
            import torch
            if torch.cuda.is_available():
                return "cuda"
        except ImportError:
            pass
        return "cpu"
    return preference


def _device_display_name(device: str) -> str:
    if device == "cuda":
        try:
            import torch
            return f"CUDA ({torch.cuda.get_device_name(0)})"
        except Exception:
            return "CUDA"
    if device == "directml":
        return "DirectML"
    return "CPU"


@dataclass
class Detection:
    x: float        # center x, normalized 0-1
    y: float        # center y, normalized 0-1
    w: float        # width, normalized 0-1
    h: float        # height, normalized 0-1
    confidence: float
    pixels_per_mm: float  # derived from bounding box width


class BallDetector:
    """YOLOv11 nano ball detector. Lazy-loads the model on first use.

    YOLOv11n is preferred over v8n for overhead small-object detection:
    the C2PSA (Cross-Stage Partial Spatial Attention) module improves mAP
    on tiny objects by ~5.5% vs v8 on UAV benchmarks, with 22% fewer params.
    """

    def __init__(
        self,
        model_path: Optional[str] = None,
        conf_threshold: float = 0.15,  # overhead golf ball cameras often score 0.15-0.22
        inference_device: str = "auto",
        detection_imgsz: int = 320,
    ):
        self.conf_threshold = conf_threshold
        self._model_path = model_path or str(DEFAULT_MODEL_PATH)
        self._model = None
        self._device = _resolve_device(inference_device)
        self._imgsz = detection_imgsz
        self.device_info = _device_display_name(self._device)
        log.info("BallDetector: device=%s (%s)  imgsz=%d", self._device, self.device_info, self._imgsz)

    def _ensure_model(self):
        if self._model is None:
            from ultralytics import YOLO
            try:
                self._model = YOLO(self._model_path)
            except Exception as e:
                fallback = "yolov8n.pt"
                if self._model_path != fallback:
                    log.warning(
                        "Could not load %s (%s) -- falling back to %s. "
                        "Check internet access or manually download the model.",
                        self._model_path, e, fallback
                    )
                    self._model_path = fallback
                    self._model = YOLO(self._model_path)
                else:
                    raise
            # Resolve which class to filter for -- COCO model: class 32 "sports ball";
            # fine-tuned model: class 0 "golf_ball" (or similar).
            names = getattr(self._model, "names", {})
            self._target_class = next(
                (k for k, v in names.items() if v.lower().replace("-", "_") in GOLF_BALL_NAMES),
                SPORTS_BALL_CLASS_ID,
            )
            if self._target_class != SPORTS_BALL_CLASS_ID:
                log.info("BallDetector: fine-tuned model detected -- using class %d (%s)",
                         self._target_class, names.get(self._target_class))
            dummy = np.zeros((self._imgsz, self._imgsz, 3), dtype=np.uint8)
            self._model(dummy, device=self._device, imgsz=self._imgsz, verbose=False,
                        classes=[self._target_class])
            log.info("BallDetector warmed up on %s with model=%s", self.device_info, self._model_path)

    def detect_in_roi(self, frame: np.ndarray, x1n: float, y1n: float, x2n: float, y2n: float) -> Optional[Detection]:
        """Run detection on a normalized-coord crop. Returns full-frame-normalized Detection.

        Cropping to the known region of interest makes the ball appear 2-3x larger in
        YOLO's input tensor (e.g. start zone crop at 400px wide -> ball is 13px vs 4px
        at full 1280px frame), pushing it into P3 detection head's effective range.
        """
        h, w = frame.shape[:2]
        x1 = int(x1n * w)
        y1 = int(y1n * h)
        x2 = int(x2n * w)
        y2 = int(y2n * h)
        if x2 <= x1 or y2 <= y1:
            return None
        crop = frame[y1:y2, x1:x2]
        det = self.detect(crop)
        if det is None:
            return None
        # Scale back to full-frame normalized coordinates. Note: pixels_per_mm
        # needs NO rescaling here -- cropping (frame[y1:y2, x1:x2]) is a plain
        # slice, not a resize/zoom, so det.pixels_per_mm from detect(crop) is
        # already in the same native pixel-density units as the full frame.
        full_x = x1n + det.x * (x2n - x1n)
        full_y = y1n + det.y * (y2n - y1n)
        full_w = det.w * (x2n - x1n)
        full_h = det.h * (y2n - y1n)
        return Detection(full_x, full_y, full_w, full_h, det.confidence, det.pixels_per_mm)

    def detect(self, frame: np.ndarray) -> Optional[Detection]:
        """Return the highest-confidence ball detection, or None."""
        self._ensure_model()
        h, w = frame.shape[:2]
        # Only pass half= on CUDA -- this ultralytics version warns (every single
        # call, i.e. every frame) that `half` is deprecated in favor of `quantize`,
        # even when passed as False. CPU inference never wants half precision
        # anyway, so omitting the kwarg entirely on CPU is behavior-equivalent and
        # silences the per-frame spam.
        predict_kwargs = dict(device=self._device, imgsz=self._imgsz, verbose=False,
                               classes=[self._target_class])
        if self._device == "cuda":
            predict_kwargs["half"] = True
        results = self._model(frame, **predict_kwargs)[0]

        best: Optional[Detection] = None
        best_raw: Optional[float] = None  # highest conf seen, even if below threshold

        for box in results.boxes:
            conf = float(box.conf[0])
            if best_raw is None or conf > best_raw:
                best_raw = conf
            if conf < self.conf_threshold:
                continue
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            cx = ((x1 + x2) / 2) / w
            cy = ((y1 + y2) / 2) / h
            bw = (x2 - x1) / w
            bh = (y2 - y1) / h
            pixel_w = x2 - x1
            ppm = pixel_w / GOLF_BALL_DIAMETER_MM if pixel_w > 0 else 0.0
            d = Detection(cx, cy, bw, bh, conf, ppm)
            if best is None or conf > best.confidence:
                best = d

        if best_raw is not None and best is None:
            log.info("YOLO: sports ball at conf=%.2f below threshold=%.2f (filtered)", best_raw, self.conf_threshold)
        elif best is not None:
            log.debug("YOLO: ball accepted conf=%.2f at (%.3f, %.3f)", best.confidence, best.x, best.y)

        return best


def create_detector(model_path: Optional[str] = None) -> BallDetector:
    return BallDetector(model_path=model_path)
