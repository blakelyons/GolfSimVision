"""E6 Connect ingress spike (P0-4) -- runner + transcript logging.

Starts protocol.E6Server on port 2483 and waits for the Garmin Golf app to connect.
Logs every message both directions to logs/session_<ts>/transcript.log (human-readable)
and transcript.jsonl (machine-readable), matching gspro-harness/harness.py's format so
evidence from both harnesses is consistent.

Run (Windows PC, same LAN as your phone):
    python harness.py [--port 2483]

Then, on your phone: open the Garmin Golf app, make sure the R10 is paired, and connect
it to a simulator at this PC's LAN IP on the port above. Watch this console and
logs/session_<ts>/transcript.log for what actually happens.
"""

import argparse
import json
import time
from datetime import datetime
from pathlib import Path

from protocol import DEFAULT_PORT, E6Server

LOGS_DIR = Path(__file__).parent / "logs"


def make_session_dir() -> Path:
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    session_dir = LOGS_DIR / f"session_{ts}"
    session_dir.mkdir(parents=True, exist_ok=True)
    return session_dir


class Transcript:
    """Same on-disk shape as gspro-harness/harness.py's Transcript, with "Garmin" as the
    remote-party label instead of "GSPro"."""

    def __init__(self, session_dir: Path):
        self.log_path = session_dir / "transcript.log"
        self.jsonl_path = session_dir / "transcript.jsonl"
        self._log_fh = open(self.log_path, "a", encoding="utf-8")
        self._jsonl_fh = open(self.jsonl_path, "a", encoding="utf-8")

    def _write(self, direction: str, obj: dict) -> None:
        now = time.time()
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        text = json.dumps(obj, separators=(",", ":"))
        self._log_fh.write(f"{ts}  Garmin {direction} {text}\n")
        self._log_fh.flush()
        self._jsonl_fh.write(
            json.dumps({"ts": now, "direction": direction, "raw": text}) + "\n"
        )
        self._jsonl_fh.flush()

    def outbound(self, obj: dict) -> None:
        """We -> Garmin Golf app."""
        self._write("<<", obj)

    def inbound(self, obj: dict) -> None:
        """Garmin Golf app -> us."""
        self._write(">>", obj)

    def close(self) -> None:
        self._log_fh.close()
        self._jsonl_fh.close()


def main() -> None:
    parser = argparse.ArgumentParser(description="E6 Connect ingress spike (P0-4)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    args = parser.parse_args()

    session_dir = make_session_dir()
    transcript = Transcript(session_dir)
    print(f"session: {session_dir}")

    shots_seen = []

    def on_connection_change(connected: bool) -> None:
        print(f"[status] {'CONNECTED' if connected else 'DISCONNECTED'}")

    def on_shot(ball_data: dict) -> None:
        shots_seen.append(ball_data)
        print(f"[shot #{len(shots_seen)}] BallData = {ball_data}")

    server = E6Server(
        port=args.port,
        on_send=transcript.outbound,
        on_receive=transcript.inbound,
        on_shot=on_shot,
        on_connection_change=on_connection_change,
    )
    server.start()
    print(f"E6 server listening on 0.0.0.0:{args.port}")
    print(
        "On your phone: open the Garmin Golf app, connect to a simulator at this PC's "
        "LAN IP on the port above. Ctrl+C here to stop."
    )

    try:
        while True:
            time.sleep(1.0)
    except KeyboardInterrupt:
        print("\ninterrupted")
    finally:
        server.stop()
        transcript.close()
        print(f"stopped. {len(shots_seen)} shot(s) received this session.")


if __name__ == "__main__":
    main()
