"""Live webcam putting -> GSPro pipeline.

Combines three already-proven pieces from this project's Phase 0 work instead
of writing any of them fresh:
- prototypes/corpus/capture.py's open_camera() -- CAP_MSMF backend, confirmed
  60fps real ceiling on this hardware, step-by-step startup logging (opening +
  each property .set() call can genuinely take ~15s apiece on this camera --
  see prototypes/corpus/docs/CAMERA_CAPABILITIES.md).
- detection/ -- V1 (SimLinkPutting)'s BallDetector (YOLO) + ZoneStateMachine +
  PuttCalculator, ported for live use (see each file's docstring for exactly
  what changed vs. V1, which is: nothing except detector.py's default model
  path). Unlike the P0-1 corpus's offline replay.py harness, this is a real
  live pipeline, so ZoneStateMachine's internal time.monotonic() calls are
  exactly right here (that was a problem only for batch replay).
- prototypes/gspro-harness/protocol.py's GSProDirect/GSProCodec -- P0-2's
  already-tested GSPro OpenConnect client (port 921, per ADR-0001).

Prerequisite you must do by hand: GSPro must have an active round, and the
currently selected club in GSPro's own UI must be the putter. OpenConnect has
no message to select a club programmatically (see
prototypes/gspro-harness/docs/GSPRO_OPENCONNECT.md) -- GSPro infers "this is a
putt" purely from whatever club is selected there. This script watches GSPro's
own club-change responses and warns (does not block) if it looks wrong.

Camera framing prerequisite: the zone/gateway model assumes the ball's resting
position is on the LEFT ~30% of frame and it travels LEFT -> RIGHT (V1's own
convention, matching --start-zone/--gateway-x defaults). Override with
--start-zone/--gateway-x if your camera is mounted differently.
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path
from typing import Optional

import cv2

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent / "corpus"))
sys.path.insert(0, str(_HERE.parent / "gspro-harness"))

from capture import open_camera  # noqa: E402
from protocol import GSProResponse  # noqa: E402
from scenarios import connect_and_announce, make_codec  # noqa: E402

from detection.detector import BallDetector  # noqa: E402
from detection.zone_state_machine import ZoneStateMachine, PuttState  # noqa: E402
from detection.putt_calculator import PuttCalculator  # noqa: E402

# Real implication from P0-2's exp15 (docs/GSPRO_OPENCONNECT.md): back-to-back
# sends with no gap reliably lose acks. A real putting session naturally has
# gaps between putts, but this floor keeps a double-fire (e.g. a bad re-arm)
# from ever hammering GSPro.
MIN_SHOT_INTERVAL_S = 2.0

PUTTER_CLUB_CODE = "PT"


class GSProPuttSender:
    """Paces putt sends and tracks GSPro's own club selection (read-only --
    this pipeline never sends a club-change message, GSPro has no such
    message; see module docstring)."""

    def __init__(self, client, codec):
        self.client = client
        self.codec = codec
        self.current_club: Optional[str] = None
        self._last_sent_at: float = 0.0
        client.on_response = self._on_response

    def _on_response(self, resp: GSProResponse) -> None:
        if resp.Code == 201 and resp.Player and "Club" in resp.Player:
            club = resp.Player["Club"]
            if club != self.current_club:
                print(f"[gspro] club selection changed: {self.current_club} -> {club}")
            self.current_club = club
        elif resp.Code == 200:
            print(f"[gspro] ack: {resp.Message}")
        elif resp.Code == 202:
            print(f"[gspro] ready for next shot")
        else:
            print(f"[gspro] response code={resp.Code} message={resp.Message}")

    def send_putt(self, speed_mph: float, hla_deg: float) -> bool:
        """Returns False (and does not send) if paced out; True once sent."""
        now = time.monotonic()
        if now - self._last_sent_at < MIN_SHOT_INTERVAL_S:
            print(f"[gspro] skipped send -- {now - self._last_sent_at:.1f}s since last shot, "
                  f"minimum is {MIN_SHOT_INTERVAL_S}s")
            return False
        if self.current_club is not None and self.current_club != PUTTER_CLUB_CODE:
            print(f"[gspro] WARNING: GSPro's selected club is '{self.current_club}', not "
                  f"'{PUTTER_CLUB_CODE}' -- sending anyway, but this shot will likely be "
                  f"misinterpreted. Select the putter in GSPro.")
        self._last_sent_at = now
        self.client.send(self.codec.build_putt(speed=speed_mph, hla=hla_deg, vla=0))
        print(f"[gspro] sent putt speed={speed_mph:.2f}mph hla={hla_deg:.2f}deg")
        return True


def draw_overlay(frame, zone_sm: ZoneStateMachine, detection, last_result_text: str) -> None:
    h, w = frame.shape[:2]
    sz = zone_sm.start_zone
    x1, y1 = int(sz["x"] * w), int(sz["y"] * h)
    x2, y2 = int((sz["x"] + sz["w"]) * w), int((sz["y"] + sz["h"]) * h)
    cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 200, 0), 1)
    gate_x = int(zone_sm.gateway_x * w)
    cv2.line(frame, (gate_x, 0), (gate_x, h), (0, 165, 255), 1)

    if detection is not None:
        cx, cy = int(detection.x * w), int(detection.y * h)
        r = max(3, int(detection.w * w / 2))
        cv2.circle(frame, (cx, cy), r, (0, 0, 255), 2)

    cv2.putText(frame, f"state={zone_sm.state.value}", (10, 20),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
    cv2.putText(frame, last_result_text, (10, 40),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)


def run(args: argparse.Namespace) -> None:
    cap, requested, actual = open_camera(args.index, args.width, args.height, args.fps)
    if not cap.isOpened():
        print(f"[live_putt] failed to open camera index {args.index}", file=sys.stderr)
        sys.exit(1)
    print(f"[live_putt] camera requested: {requested}")
    print(f"[live_putt] camera actual:    {actual}")

    detector = BallDetector(model_path=args.model, conf_threshold=args.conf_threshold)
    zone_sm = ZoneStateMachine(
        start_zone={"x": args.start_zone[0], "y": args.start_zone[1],
                    "w": args.start_zone[2], "h": args.start_zone[3]},
        gateway_x=args.gateway_x,
        frame_width=actual["width"],
        frame_height=actual["height"],
    )
    calculator = PuttCalculator()

    sender: Optional[GSProPuttSender] = None
    if not args.dry_run:
        from protocol import GSProDirect  # noqa: E402 (local import, path added above)
        client = GSProDirect(host=args.gspro_host, port=args.gspro_port, device_id=args.device_id)
        codec = make_codec(client)
        sender = GSProPuttSender(client, codec)
        print(f"[live_putt] connecting to GSPro at {args.gspro_host}:{args.gspro_port} ...")
        connect_and_announce(client)
        print("[live_putt] connected. Make sure GSPro has an active round and the putter selected.")
    else:
        print("[live_putt] --dry-run: detected putts will be printed, not sent to GSPro")

    print("[live_putt] warming up detector (first inference call loads the model) ...")
    last_result_text = "no putt yet"

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                print("[live_putt] frame read failed, stopping", file=sys.stderr)
                break

            detection = detector.detect(frame)
            result = zone_sm.update(detection)

            if result is not None:
                if result.get("rejected"):
                    last_result_text = f"rejected: {result['reason']}"
                    print(f"[live_putt] {last_result_text}")
                else:
                    try:
                        putt = calculator.calculate(result["track"], zone_sm.frame_width, zone_sm.frame_height)
                    except ValueError as e:
                        last_result_text = f"calc rejected: {e}"
                        print(f"[live_putt] {last_result_text}")
                    else:
                        last_result_text = f"putt: {putt.speed_mph:.2f}mph {putt.hla_degrees:+.2f}deg"
                        print(f"[live_putt] {last_result_text}")
                        if sender is not None:
                            sender.send_putt(putt.speed_mph, putt.hla_degrees)

            if not args.headless:
                draw_overlay(frame, zone_sm, detection, last_result_text)
                cv2.imshow("live putt (preview)", frame)
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
    except KeyboardInterrupt:
        pass
    finally:
        # A second Ctrl+C landing during cleanup itself (rather than in the loop
        # above, where it's already caught) would otherwise print a scary-looking
        # traceback on a completely normal exit.
        try:
            cap.release()
            cv2.destroyAllWindows()
            if sender is not None:
                sender.client.disconnect()
        except KeyboardInterrupt:
            pass


def main() -> None:
    parser = argparse.ArgumentParser(description="Live webcam putting -> GSPro pipeline.")
    parser.add_argument("--index", type=int, default=0, help="camera index")
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=int, default=60)  # confirmed hardware ceiling
    parser.add_argument("--model", default=None, help="override YOLO model path")
    parser.add_argument("--conf-threshold", type=float, default=0.15)
    parser.add_argument("--start-zone", type=float, nargs=4, default=[0.05, 0.1, 0.25, 0.8],
                         metavar=("X", "Y", "W", "H"), help="normalized ball-rest zone")
    parser.add_argument("--gateway-x", type=float, default=0.55,
                         help="normalized x the ball must cross to trigger tracking")
    parser.add_argument("--gspro-host", default="127.0.0.1")
    parser.add_argument("--gspro-port", type=int, default=921)
    parser.add_argument("--device-id", default="GolfSimVision")
    parser.add_argument("--dry-run", action="store_true", help="detect only, don't connect to GSPro")
    parser.add_argument("--headless", action="store_true", help="no preview window")
    args = parser.parse_args()
    run(args)


if __name__ == "__main__":
    main()
