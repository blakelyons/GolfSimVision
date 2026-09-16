"""GSPro protocol truth harness — REPL + scenario runner.

Run (Windows PC, GSPro must already be running and listening on 127.0.0.1:921):
    python harness.py [--host 127.0.0.1] [--port 921] [--device-id GolfSimVision]
                      [--units Yards] [--scenario NAME]

No --scenario: interactive REPL (type 'help' for commands). With --scenario NAME:
imports scenarios.NAME, calls its run(client), and exits when run() returns.
See SPEC.md for the full design and README.md for prerequisites.
"""

import argparse
import importlib
import json
import time
from datetime import datetime
from pathlib import Path
from typing import Callable, Dict, Optional

from protocol import ConnectionState, GSProCodec, GSProDirect, GSProResponse

LOGS_DIR = Path(__file__).parent / "logs"

TRUE_STRINGS = {"1", "true", "t", "yes", "y"}


def parse_kv(args: str) -> Dict[str, str]:
    """'speed=5.0 hla=-1.5 vla=0' -> {'speed': '5.0', 'hla': '-1.5', 'vla': '0'}"""
    result = {}
    for token in args.split():
        if "=" not in token:
            continue
        key, _, value = token.partition("=")
        result[key] = value
    return result


def parse_bool(s: str) -> bool:
    return s.strip().lower() in TRUE_STRINGS


def make_session_dir() -> Path:
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    session_dir = LOGS_DIR / f"session_{ts}"
    session_dir.mkdir(parents=True, exist_ok=True)
    return session_dir


def write_notes(session_dir: Path, question: str) -> None:
    (session_dir / "NOTES.md").write_text(f"# {question}\n\n", encoding="utf-8")


class Transcript:
    """Every byte both directions, perf_counter-adjacent wall-clock timestamped,
    to transcript.log (human-readable) and transcript.jsonl (machine-readable)."""

    def __init__(self, session_dir: Path):
        self.log_path = session_dir / "transcript.log"
        self.jsonl_path = session_dir / "transcript.jsonl"
        self._log_fh = open(self.log_path, "a", encoding="utf-8")
        self._jsonl_fh = open(self.jsonl_path, "a", encoding="utf-8")

    def _write(self, direction: str, raw: bytes) -> None:
        now = time.time()
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            text = repr(raw)
        self._log_fh.write(f"{ts}  GSPro {direction} {text}\n")
        self._log_fh.flush()
        self._jsonl_fh.write(
            json.dumps({"ts": now, "direction": direction, "raw": text}) + "\n"
        )
        self._jsonl_fh.flush()

    def outbound(self, raw: bytes) -> None:
        """We -> GSPro."""
        self._write("<<", raw)

    def inbound(self, raw: bytes) -> None:
        """GSPro -> us."""
        self._write(">>", raw)

    def close(self) -> None:
        self._log_fh.close()
        self._jsonl_fh.close()


COMMAND_HELP = [
    ("connect", "opens the connection, starts the reconnect-loop thread"),
    ("disconnect", "orderly close"),
    ("drop", "closes the raw socket without a clean shutdown (experiment 16)"),
    ("heartbeat", "ready=<bool> [detected=<bool>] - sends a heartbeat"),
    ("putt", "speed=<mph> hla=<deg> vla=<deg> [spin=<rpm>] - GSProCodec.build_putt + send"),
    ("shot", "speed=<mph> hla=<deg> vla=<deg> spin=<rpm> [spinaxis=<deg>] - full-shot payload + send"),
    ("raw", "<literal JSON> - sends the bytes exactly as typed"),
    ("shotnum", "<int> - overrides the next auto-incremented ShotNumber"),
    ("watch", "<seconds> - waits, logging inbound traffic only"),
    ("status", "prints ConnectionState, last ShotNumber, session uptime"),
    ("help", "lists these commands"),
    ("quit / exit", "flush + close transcripts, disconnect, exit"),
]


class Harness:
    def __init__(self, host: str, port: int, device_id: str, units: str, session_dir: Path):
        self.session_dir = session_dir
        self.transcript = Transcript(session_dir)
        self.client = GSProDirect(
            host=host,
            port=port,
            device_id=device_id,
            units=units,
            on_send=self.transcript.outbound,
            on_receive=self.transcript.inbound,
            on_response=self._on_response,
            on_status=self._on_status,
        )
        self.codec = GSProCodec(
            device_id=device_id, units=units, next_shot_number=self.client.next_shot_number
        )
        self.started_at = time.monotonic()
        self.last_response: Optional[GSProResponse] = None

        self.commands: Dict[str, Callable[[str], None]] = {
            "connect": self.cmd_connect,
            "disconnect": self.cmd_disconnect,
            "drop": self.cmd_drop,
            "heartbeat": self.cmd_heartbeat,
            "putt": self.cmd_putt,
            "shot": self.cmd_shot,
            "raw": self.cmd_raw,
            "shotnum": self.cmd_shotnum,
            "watch": self.cmd_watch,
            "status": self.cmd_status,
            "help": self.cmd_help,
        }

    # --- callbacks ----------------------------------------------------------

    def _on_response(self, resp: GSProResponse) -> None:
        self.last_response = resp

    def _on_status(self, state: ConnectionState) -> None:
        print(f"[status] {state.name}")

    # --- commands ------------------------------------------------------------

    def cmd_connect(self, args: str) -> None:
        self.client.connect()
        print("connecting...")

    def cmd_disconnect(self, args: str) -> None:
        self.client.disconnect()
        print("disconnected")

    def cmd_drop(self, args: str) -> None:
        self.client.drop()
        print("dropped raw socket (simulated link failure)")

    def cmd_heartbeat(self, args: str) -> None:
        kv = parse_kv(args)
        if "ready" not in kv:
            print("usage: heartbeat ready=<bool> [detected=<bool>]")
            return
        self.client.send_heartbeat(
            ready=parse_bool(kv["ready"]), ball_detected=parse_bool(kv.get("detected", "false"))
        )

    def cmd_putt(self, args: str) -> None:
        kv = parse_kv(args)
        try:
            speed, hla, vla = float(kv["speed"]), float(kv["hla"]), float(kv["vla"])
        except (KeyError, ValueError):
            print("usage: putt speed=<mph> hla=<deg> vla=<deg> [spin=<rpm>]")
            return
        spin = float(kv["spin"]) if "spin" in kv else None
        self.client.send(self.codec.build_putt(speed=speed, hla=hla, vla=vla, spin=spin))

    def cmd_shot(self, args: str) -> None:
        kv = parse_kv(args)
        try:
            speed, hla, vla = float(kv["speed"]), float(kv["hla"]), float(kv["vla"])
            total_spin = float(kv["spin"])
            spin_axis = float(kv.get("spinaxis", "0"))
        except (KeyError, ValueError):
            print("usage: shot speed=<mph> hla=<deg> vla=<deg> spin=<rpm> [spinaxis=<deg>]")
            return
        self.client.send(
            self.codec.build_shot(
                speed=speed, hla=hla, vla=vla, total_spin=total_spin, spin_axis=spin_axis
            )
        )

    def cmd_raw(self, args: str) -> None:
        if not args.strip():
            print("usage: raw <literal JSON>")
            return
        self.client.send_raw(args.encode("utf-8"))

    def cmd_shotnum(self, args: str) -> None:
        try:
            value = int(args.strip())
        except ValueError:
            print("usage: shotnum <int>")
            return
        self.client.override_shot_number(value)
        print(f"next ShotNumber forced to {value}")

    def cmd_watch(self, args: str) -> None:
        try:
            seconds = float(args.strip())
        except ValueError:
            print("usage: watch <seconds>")
            return
        print(f"watching for {seconds}s (inbound traffic is already logged as it arrives)...")
        time.sleep(seconds)

    def cmd_status(self, args: str) -> None:
        uptime = time.monotonic() - self.started_at
        print(
            f"state={self.client.state.name} "
            f"last_shot_number={self.client.last_shot_number} "
            f"uptime={uptime:.1f}s"
        )

    def cmd_help(self, args: str) -> None:
        for name, doc in COMMAND_HELP:
            print(f"  {name:<12} {doc}")

    # --- run modes -----------------------------------------------------------

    def repl(self) -> None:
        print(f"GSPro harness REPL. session: {self.session_dir}")
        self.cmd_help("")
        try:
            while True:
                try:
                    line = input("> ").strip()
                except EOFError:
                    break
                if not line:
                    continue
                command, _, rest = line.partition(" ")
                command = command.lower()
                if command in ("quit", "exit"):
                    break
                handler = self.commands.get(command)
                if handler is None:
                    print(f"unknown command: {command!r} (try 'help')")
                    continue
                try:
                    handler(rest.strip())
                except Exception as e:
                    print(f"error: {e}")
        finally:
            self.shutdown()

    def run_scenario(self, name: str) -> None:
        module = importlib.import_module(f"scenarios.{name}")
        question = getattr(module, "QUESTION", "(no QUESTION defined)")
        write_notes(self.session_dir, question)
        print(f"session: {self.session_dir}")
        print(f"scenario: {name}\nquestion: {question}")
        try:
            module.run(self.client)
        finally:
            self.shutdown()

    def shutdown(self) -> None:
        self.client.disconnect()
        self.transcript.close()


def main() -> None:
    parser = argparse.ArgumentParser(description="GSPro protocol truth harness")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=921)
    parser.add_argument("--device-id", default="GolfSimVision")
    parser.add_argument("--units", default="Yards")
    parser.add_argument("--scenario", default=None)
    args = parser.parse_args()

    session_dir = make_session_dir()
    harness = Harness(
        host=args.host,
        port=args.port,
        device_id=args.device_id,
        units=args.units,
        session_dir=session_dir,
    )
    try:
        if args.scenario:
            harness.run_scenario(args.scenario)
        else:
            harness.repl()
    except KeyboardInterrupt:
        print("\ninterrupted")


if __name__ == "__main__":
    main()
