"""GSPro OpenConnect V1 wire protocol.

Ported from example-apps/openflight-*/src/openflight/gspro/messages.py and
sim/transport.py's find_json_end. See SPEC.md for the vocabulary/rename notes.
Ships nowhere — this file is Phase 0 prototype code, written carefully because
its shape is a rehearsal for the future C# GSProDirect (see ADR-0006).
"""

import json
import socket
import struct
import sys
import threading
import time
from dataclasses import asdict, dataclass, field
from enum import Enum, auto
from typing import Callable, Optional, Tuple


@dataclass
class BallData:
    Speed: float = 0.0
    SpinAxis: float = 0.0
    TotalSpin: float = 0.0
    BackSpin: float = 0.0
    SideSpin: float = 0.0
    HLA: float = 0.0
    VLA: float = 0.0
    CarryDistance: float = 0.0


@dataclass
class ClubData:
    Speed: float = 0.0
    AngleOfAttack: float = 0.0
    FaceToTarget: float = 0.0
    Lie: float = 0.0
    Loft: float = 0.0
    Path: float = 0.0
    SpeedAtImpact: float = 0.0
    VerticalFaceImpact: float = 0.0
    HorizontalFaceImpact: float = 0.0
    ClosureRate: float = 0.0


@dataclass
class ShotDataOptions:
    ContainsBallData: bool = True
    ContainsClubData: bool = False
    LaunchMonitorIsReady: bool = True
    LaunchMonitorBallDetected: bool = True
    IsHeartBeat: bool = False


@dataclass
class ShotPayload:
    DeviceID: str
    Units: str
    ShotNumber: int
    APIversion: str  # literal string "1", not int — GSPro's spec requires a string
    BallData: BallData = field(default_factory=BallData)
    ClubData: ClubData = field(default_factory=ClubData)
    ShotDataOptions: ShotDataOptions = field(default_factory=ShotDataOptions)


@dataclass
class GSProResponse:
    Code: int
    Message: str = ""
    Player: Optional[dict] = None


def serialize_payload(payload: ShotPayload) -> bytes:
    return json.dumps(asdict(payload), separators=(",", ":")).encode("utf-8")


def parse_response(raw: bytes) -> GSProResponse:
    """Parse a GSPro reply. Raises ValueError on malformed JSON."""
    try:
        obj = json.loads(raw.decode("utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError) as e:
        raise ValueError(f"Malformed GSPro response: {e}") from e
    return GSProResponse(
        Code=int(obj.get("Code", 0)),
        Message=str(obj.get("Message", "")),
        Player=obj.get("Player"),
    )


def find_json_end(buf: bytes) -> Optional[int]:
    """Index past the first complete top-level JSON object in buf, or None.

    GSPro neither length-prefixes nor delimits frames, so we frame on balanced
    top-level braces, string-aware so quoted braces don't affect depth.
    """
    depth = 0
    in_str = False
    escape = False
    started = False
    for i, b in enumerate(buf):
        ch = chr(b) if b < 128 else ""
        if in_str:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_str = False
        else:
            if ch == '"':
                in_str = True
            elif ch == "{":
                depth += 1
                started = True
            elif ch == "}":
                depth -= 1
                if started and depth == 0:
                    return i + 1
    return None


DEFAULT_BACKOFF: Tuple[float, ...] = (1.0, 2.0, 4.0, 8.0, 16.0, 30.0)

# Backstop for the inbound framer: real GSPro frames are a few hundred bytes, so if
# the undrained buffer ever passes this with no complete frame, resync by dropping it
# rather than growing memory without limit.
_MAX_FRAME_BYTES = 64 * 1024

# recv() timeout, so the receive loop can notice _stop_event/drop without blocking.
_RECV_POLL_TIMEOUT = 0.2

# connect() timeout — GSPro is localhost-only, so a slow connect means it isn't running.
_CONNECT_TIMEOUT = 5.0


class ConnectionState(Enum):
    DISABLED = auto()
    CONNECTING = auto()
    CONNECTED = auto()
    RECONNECTING = auto()  # was once connected, dropped, now retrying
    STOPPED = auto()


class GSProDirect:
    """TCP connection to GSPro's OpenConnect listener (127.0.0.1:921).

    Ported from openflight/sim/transport.py's TcpSimClient — connection
    lifecycle, JSON framing, and reconnect/backoff — with two changes: renamed
    per this project's vocabulary (see SPEC.md), and no automatic background
    heartbeat thread. Heartbeats are sent explicitly via send_heartbeat(), so
    experiments that control heartbeat timing/content aren't fighting a timer.

    Callbacks (all optional, called from the connection/receive thread):
      on_send(bytes)              — every outbound frame, after it's sent
      on_receive(bytes)           — every complete inbound frame, before parsing
      on_response(GSProResponse)  — every successfully parsed inbound frame
      on_status(ConnectionState)  — every state transition
    """

    def __init__(
        self,
        host: str,
        port: int,
        device_id: str,
        units: str = "Yards",
        backoff_seconds: Tuple[float, ...] = DEFAULT_BACKOFF,
        on_send: Optional[Callable[[bytes], None]] = None,
        on_receive: Optional[Callable[[bytes], None]] = None,
        on_response: Optional[Callable[[GSProResponse], None]] = None,
        on_status: Optional[Callable[[ConnectionState], None]] = None,
    ):
        self.host = host
        self.port = port
        self.device_id = device_id
        self.units = units
        self.on_send = on_send
        self.on_receive = on_receive
        self.on_response = on_response
        self.on_status = on_status

        self._backoff = backoff_seconds
        self._state = ConnectionState.DISABLED
        self._state_lock = threading.Lock()
        self._sock: Optional[socket.socket] = None
        self._sock_lock = threading.Lock()
        self._stop_event = threading.Event()
        self._conn_thread: Optional[threading.Thread] = None

        self._shot_number = int(time.time())
        self._shot_number_lock = threading.Lock()

        self.connected_at: Optional[float] = None

    # --- state ------------------------------------------------------------

    @property
    def state(self) -> ConnectionState:
        with self._state_lock:
            return self._state

    def is_connected(self) -> bool:
        return self.state == ConnectionState.CONNECTED

    def _set_state(self, new_state: ConnectionState) -> None:
        with self._state_lock:
            if self._state == new_state:
                return
            self._state = new_state
        if self.on_status is not None:
            self.on_status(new_state)

    # --- shot numbering -----------------------------------------------------

    @property
    def last_shot_number(self) -> int:
        with self._shot_number_lock:
            return self._shot_number

    def next_shot_number(self) -> int:
        """Returns the ShotNumber to use for the next send, then increments it."""
        with self._shot_number_lock:
            n = self._shot_number
            self._shot_number += 1
            return n

    def override_shot_number(self, value: int) -> None:
        """Forces the next next_shot_number() call to return exactly `value`."""
        with self._shot_number_lock:
            self._shot_number = value

    # --- connect / disconnect / drop ----------------------------------------

    def connect(self) -> None:
        """Starts the connection thread (idempotent). Non-blocking."""
        if self._conn_thread is not None and self._conn_thread.is_alive():
            return
        self._stop_event.clear()
        self._conn_thread = threading.Thread(
            target=self._connection_loop, name="gspro-conn", daemon=True
        )
        self._conn_thread.start()

    def disconnect(self) -> None:
        """Orderly shutdown: stops the connection thread and closes the socket."""
        self._stop_event.set()
        self._close_socket(graceful=True)
        if self._conn_thread is not None:
            self._conn_thread.join(timeout=3.0)
        self._conn_thread = None
        self.connected_at = None
        self._set_state(ConnectionState.STOPPED)

    def drop(self) -> None:
        """Simulates an ungraceful disconnect (experiment 16): forces a TCP RST
        instead of a clean FIN, then lets the connection loop's normal
        reconnect/backoff path take over — the thread keeps running."""
        self._close_socket(graceful=False)

    # --- send -----------------------------------------------------------------

    def send(self, payload: ShotPayload) -> None:
        self.send_raw(serialize_payload(payload))

    def send_raw(self, raw: bytes) -> None:
        """Sends bytes exactly as given, bypassing serialize_payload — for the
        REPL's `raw` command (malformed-input/casing experiments 14, 19, 20)."""
        with self._sock_lock:
            if self._sock is None:
                raise ConnectionError("send_raw() called while not connected")
            self._sock.sendall(raw)
        if self.on_send is not None:
            self.on_send(raw)

    def send_heartbeat(self, ready: bool, ball_detected: bool = False) -> None:
        payload = ShotPayload(
            DeviceID=self.device_id,
            Units=self.units,
            ShotNumber=self.next_shot_number(),
            APIversion="1",
            ShotDataOptions=ShotDataOptions(
                ContainsBallData=False,
                ContainsClubData=False,
                LaunchMonitorIsReady=ready,
                LaunchMonitorBallDetected=ball_detected,
                IsHeartBeat=True,
            ),
        )
        self.send(payload)

    # --- internals --------------------------------------------------------

    def _close_socket(self, graceful: bool) -> None:
        with self._sock_lock:
            if self._sock is None:
                return
            sock = self._sock
            self._sock = None
        if not graceful:
            # SO_LINGER with a zero timeout forces a RST on close() instead of
            # the normal FIN handshake, simulating a dropped link rather than
            # an orderly disconnect.
            try:
                sock.setsockopt(
                    socket.SOL_SOCKET, socket.SO_LINGER, struct.pack("ii", 1, 0)
                )
            except OSError:
                pass
        else:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        try:
            sock.close()
        except OSError:
            pass

    def _try_connect(self) -> bool:
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(_CONNECT_TIMEOUT)
        try:
            s.connect((self.host, self.port))
        except OSError:
            try:
                s.close()
            except OSError:
                pass
            return False
        with self._sock_lock:
            self._sock = s
        self.connected_at = time.time()
        return True

    def _backoff_for_attempt(self, attempt: int) -> float:
        idx = min(attempt, len(self._backoff) - 1)
        return self._backoff[idx]

    def _connection_loop(self) -> None:
        attempt = 0
        ever_connected = False
        while not self._stop_event.is_set():
            self._set_state(ConnectionState.CONNECTING)
            if self._try_connect():
                attempt = 0
                ever_connected = True
                self._set_state(ConnectionState.CONNECTED)
                self._recv_loop()
                self._close_socket(graceful=True)
                self.connected_at = None
                if self._stop_event.is_set():
                    break
                # Connection dropped (or _recv_loop returned) — fall through to reconnect.
            wait = self._backoff_for_attempt(attempt)
            # CONNECTING means "never yet connected" (e.g. wrong port); RECONNECTING
            # means "worked once, then dropped" — see Readiness Review §6 item 5.
            self._set_state(
                ConnectionState.RECONNECTING if ever_connected else ConnectionState.CONNECTING
            )
            attempt += 1
            self._stop_event.wait(timeout=wait)

    def _recv_loop(self) -> None:
        buffer = bytearray()
        while not self._stop_event.is_set():
            with self._sock_lock:
                sock = self._sock
            if sock is None:
                return
            sock.settimeout(_RECV_POLL_TIMEOUT)
            try:
                data = sock.recv(4096)
            except socket.timeout:
                continue
            except OSError:
                return
            if not data:
                return
            buffer.extend(data)
            while True:
                end = find_json_end(bytes(buffer))
                if end is None:
                    break
                frame = bytes(buffer[:end])
                del buffer[:end]
                if self.on_receive is not None:
                    self.on_receive(frame)
                if self.on_response is not None:
                    try:
                        self.on_response(parse_response(frame))
                    except ValueError:
                        pass
            if len(buffer) > _MAX_FRAME_BYTES:
                print(
                    f"[GSProDirect] resync: {len(buffer)} bytes buffered with no complete "
                    f"frame (limit {_MAX_FRAME_BYTES}) -- clearing",
                    file=sys.stderr,
                )
                buffer.clear()


def rolling_spin_rpm(speed_mph: float) -> float:
    """Rough putt backspin estimate, used when a putt's spin isn't given
    explicitly (experiment 10, Harness Specs §4)."""
    return 200.0 * speed_mph


class GSProCodec:
    """Builds ShotPayloads for putts and full shots.

    Stateless wire-format layer (Readiness Review §6 item 1) — given inputs,
    returns a ShotPayload. ShotNumber values are pulled from next_shot_number
    (normally a GSProDirect's next_shot_number bound method) so the REPL and
    any scenario building payloads through the same codec share one counter
    rather than drifting out of sync with two.
    """

    def __init__(
        self,
        device_id: str,
        next_shot_number: Callable[[], int],
        units: str = "Yards",
    ):
        self.device_id = device_id
        self.units = units
        self.next_shot_number = next_shot_number

    def build_putt(
        self,
        speed: float,
        hla: float,
        vla: float,
        spin: Optional[float] = None,
    ) -> ShotPayload:
        back_spin = rolling_spin_rpm(speed) if spin is None else spin
        return ShotPayload(
            DeviceID=self.device_id,
            Units=self.units,
            ShotNumber=self.next_shot_number(),
            APIversion="1",
            BallData=BallData(
                Speed=speed,
                HLA=hla,
                VLA=vla,
                BackSpin=back_spin,
                TotalSpin=back_spin,
            ),
            ShotDataOptions=ShotDataOptions(),
        )

    def build_shot(
        self,
        speed: float,
        hla: float,
        vla: float,
        total_spin: float,
        spin_axis: float,
        back_spin: Optional[float] = None,
        side_spin: Optional[float] = None,
    ) -> ShotPayload:
        return ShotPayload(
            DeviceID=self.device_id,
            Units=self.units,
            ShotNumber=self.next_shot_number(),
            APIversion="1",
            BallData=BallData(
                Speed=speed,
                HLA=hla,
                VLA=vla,
                TotalSpin=total_spin,
                SpinAxis=spin_axis,
                BackSpin=back_spin if back_spin is not None else total_spin,
                SideSpin=side_spin if side_spin is not None else 0.0,
            ),
            ShotDataOptions=ShotDataOptions(),
        )
