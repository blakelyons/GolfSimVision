"""E6 Connect protocol server — receives Garmin R10 shots via the Garmin Golf app.

Ported from SimLinkPutting (V1)'s backend/monitors/r10.py, renamed to this project's
vocabulary and given the same send/receive callback + transcript-logging shape as
gspro-harness/protocol.py's GSProDirect, so evidence collection is consistent across
both harnesses. Ships nowhere — Phase 0 prototype code (ADR-0006); P0-4's job is to
verify this handshake still works against the *current* Garmin Golf app, not to produce
shippable code.

E6 handshake sequence (server is us, client is the Garmin Golf app):
  1. Client -> {"Type":"Handshake"}
  2. Server -> {"Type":"Handshake","Challenge":"...","E6Version":"2, 0, 0, 0",
                "ProtocolVersion":"1.0.0.5","RequiredProtocolVersion":"1.0.0.0"}
  3. Client -> {"Type":"Challenge",...}
  4. Server -> {"Type":"Authentication","Success":"true"}
  5. Server sends {"Type":"SimCommand","SubType":"Ping"} every 10s

Real shot sequence (confirmed live 2026-09-17 against the current Garmin Golf app; see
docs/E6_CONNECT.md), not what V1 assumed:
  1. Client -> {"Type":"SetBallData","BallData":{...}}  (the real shot data)
  2. Client -> {"Type":"SetClubData","ClubData":{...}}
  3. Client -> {"Type":"SendShot"}  (empty -- just a "shot done" marker)
  4. Server -> Disarm, then (ARM_SEQUENCE_DELAY_SECONDS later) ShotComplete carrying
     the BallData/ClubData from steps 1-2, then (another delay later) Arm -- ported
     from gsp-r10-adapter's R10Session.CompleteShot, a known-working reference E6
     Connect server. Without this, the app hangs in a "waiting" state and rejects
     the next shot ("Shot Not Saved").
"""

import json
import socket
import threading
import time
from typing import Callable, Optional, Tuple

DEFAULT_PORT = 2483
CHALLENGE = "GolfSimVisionE6ChallengeXK9mQ2vN8pR4wL6j"
PING_INTERVAL_SECONDS = 10
# Delay between each step of the post-shot Disarm -> ShotComplete -> Arm sequence
# (see build_disarm/build_shot_complete/build_arm and _complete_shot_sequence below).
# Matches the timing used by the known-working gsp-r10-adapter reference server.
ARM_SEQUENCE_DELAY_SECONDS = 0.5
# The Garmin Golf app sends {"Type":"Disconnect"} both when the user really ends the
# session AND, seconds apart, as a same-socket soft-restart immediately followed by a
# fresh Handshake (observed live: ~10ms apart, far too fast for a real TCP teardown).
# This grace window lets a following Handshake cancel the close; if nothing else
# arrives in time, the Disconnect was real and we close our end.
DISCONNECT_GRACE_SECONDS = 1.0


def extract_json(buf: str) -> Tuple[Optional[dict], str]:
    """Extracts the first complete top-level JSON object from buf via brace-counting,
    string- and escape-aware. Returns (None, buf) if no complete object is present yet.
    A complete-but-invalid object is dropped (returns None for it) rather than raising,
    so one malformed frame doesn't wedge the receive loop -- ported as-is from V1."""
    depth = 0
    in_string = False
    escape = False
    start = None
    for i, ch in enumerate(buf):
        if escape:
            escape = False
            continue
        if ch == "\\" and in_string:
            escape = True
            continue
        if ch == '"':
            in_string = not in_string
            continue
        if in_string:
            continue
        if ch == "{":
            if depth == 0:
                start = i
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0 and start is not None:
                try:
                    obj = json.loads(buf[start : i + 1])
                    return obj, buf[i + 1 :]
                except json.JSONDecodeError:
                    return None, buf[i + 1 :]
    return None, buf


def build_handshake_response() -> dict:
    return {
        "Type": "Handshake",
        "Challenge": CHALLENGE,
        "E6Version": "2, 0, 0, 0",
        "ProtocolVersion": "1.0.0.5",
        "RequiredProtocolVersion": "1.0.0.0",
    }


def build_auth_response() -> dict:
    return {"Type": "Authentication", "Success": "true"}


def build_ack(sub_type: str) -> dict:
    return {"Type": "ACK", "SubType": sub_type, "Details": "Success."}


def build_ping() -> dict:
    return {"Type": "SimCommand", "SubType": "Ping"}


def build_disarm() -> dict:
    return {"Type": "SimCommand", "SubType": "Disarm"}


def build_arm() -> dict:
    return {"Type": "SimCommand", "SubType": "Arm"}


def build_shot_complete(ball_data: dict, club_data: dict) -> dict:
    # Details' numeric fields are zeroed placeholders, matching gsp-r10-adapter's
    # reference server -- the app doesn't appear to require real physics here, just
    # this message shape, to consider the shot cycle closed out.
    return {
        "Type": "SimCommand",
        "SubType": "ShotComplete",
        "Details": {
            "Apex": 0,
            "BallData": ball_data,
            "BallInHole": False,
            "BallLocation": "Fairway",
            "CarryDeviationAngle": 0,
            "TotalDeviationAngle": 0,
            "CarryDeviationFeet": 0,
            "TotalDeviationFeet": 0,
            "CarryDistance": 0,
            "TotalDistance": 0,
            "ClubData": club_data,
            "DistanceToPin": 0,
        },
    }


class E6Server:
    """TCP server speaking the E6 Connect protocol. The Garmin Golf app is the client;
    we are the simulator it thinks it's talking to.

    Callbacks (all optional):
      on_send(dict)              -- every outbound message, after it's sent
      on_receive(dict)           -- every complete inbound message, before dispatch
      on_shot(dict)              -- BallData from a SendShot message
      on_connection_change(bool) -- client connect/disconnect
    """

    def __init__(
        self,
        port: int = DEFAULT_PORT,
        on_send: Optional[Callable[[dict], None]] = None,
        on_receive: Optional[Callable[[dict], None]] = None,
        on_shot: Optional[Callable[[dict], None]] = None,
        on_connection_change: Optional[Callable[[bool], None]] = None,
    ):
        self.port = port
        self.on_send = on_send
        self.on_receive = on_receive
        self.on_shot = on_shot
        self.on_connection_change = on_connection_change

        self._server: Optional[socket.socket] = None
        self._client: Optional[socket.socket] = None
        self._connected = False
        self._running = False
        self._accept_thread: Optional[threading.Thread] = None
        self._recv_thread: Optional[threading.Thread] = None
        self._ping_thread: Optional[threading.Thread] = None
        # Incremented on every accepted connection. Each ping thread captures the
        # generation it was born into and stops as soon as a newer connection is
        # accepted, even if self._connected is still True for the new one -- without
        # this, a reconnect leaves the previous ping thread running forever, and every
        # subsequent reconnect piles on another one, all pinging self._client at once.
        self._generation = 0
        # Timestamp of the most recently received message, used by the disconnect
        # watchdog to tell "nothing else arrived" apart from "a Handshake followed."
        self._last_message_time = 0.0
        # Most recently received BallData/ClubData, bundled into the ShotComplete
        # message sent as part of the post-shot arm sequence (see _handle_message's
        # "sendshot" branch and _complete_shot_sequence).
        self._pending_ball_data: dict = {}
        self._pending_club_data: dict = {}

    def is_connected(self) -> bool:
        return self._connected

    def start(self) -> None:
        self._running = True
        self._server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server.bind(("0.0.0.0", self.port))
        self.port = self._server.getsockname()[1]  # resolves port=0 to the real one
        self._server.listen(1)
        self._server.settimeout(1.0)
        self._accept_thread = threading.Thread(target=self._accept_loop, daemon=True)
        self._accept_thread.start()

    def stop(self) -> None:
        self._running = False
        self._connected = False
        if self._client is not None:
            try:
                self._client.close()
            except OSError:
                pass
        if self._server is not None:
            try:
                self._server.close()
            except OSError:
                pass

    # --- internals ----------------------------------------------------------

    def _accept_loop(self) -> None:
        while self._running:
            try:
                client, _addr = self._server.accept()
            except socket.timeout:
                continue
            except OSError:
                if self._running:
                    raise
                return
            if self._client is not None:
                try:
                    self._client.close()
                except OSError:
                    pass
            self._client = client
            self._connected = True
            self._generation += 1
            generation = self._generation
            if self.on_connection_change is not None:
                self.on_connection_change(True)
            self._recv_thread = threading.Thread(
                target=self._recv_loop, args=(client, generation), daemon=True
            )
            self._recv_thread.start()
            self._ping_thread = threading.Thread(
                target=self._ping_loop, args=(generation,), daemon=True
            )
            self._ping_thread.start()

    def _recv_loop(self, client: socket.socket, generation: int) -> None:
        buf = ""
        while self._running and self._generation == generation:
            try:
                chunk = client.recv(4096).decode("utf-8", errors="replace")
            except OSError:
                break
            if not chunk:
                break
            buf += chunk
            while True:
                obj, buf = extract_json(buf)
                if obj is None:
                    break
                self._last_message_time = time.time()
                if self.on_receive is not None:
                    self.on_receive(obj)
                self._handle_message(client, obj, generation)
        # Only the still-current generation's disconnect should flip shared state --
        # an old, already-superseded recv loop noticing its stale socket died must not
        # clobber the new connection's self._connected.
        if self._generation == generation:
            self._connected = False
            if self.on_connection_change is not None:
                self.on_connection_change(False)

    def _handle_message(self, client: socket.socket, msg: dict, generation: int) -> None:
        msg_type = str(msg.get("Type", msg.get("type", ""))).lower()

        if msg_type == "disconnect":
            marker = self._last_message_time
            threading.Thread(
                target=self._disconnect_watchdog,
                args=(client, generation, marker),
                daemon=True,
            ).start()
        elif msg_type == "handshake":
            self._send(client, build_handshake_response())
        elif msg_type == "challenge":
            self._send(client, build_auth_response())
        elif msg_type == "setclubtype":
            self._send(client, build_ack("SetClubType"))
        elif msg_type == "setclubdata":
            self._pending_club_data = msg.get("ClubData", {})
            self._send(client, build_ack("SetClubData"))
        elif msg_type == "setballdata":
            # The current Garmin Golf app signals a completed shot via SetBallData,
            # not SendShot (V1's assumption -- SendShot never appeared in a live
            # session; see docs/E6_CONNECT.md). Fire on_shot here too so a real shot
            # is actually captured.
            self._pending_ball_data = msg.get("BallData", {})
            self._send(client, build_ack("SetBallData"))
            if self.on_shot is not None:
                self.on_shot(self._pending_ball_data)
        elif msg_type == "sendshot":
            # Live traffic shows SendShot never carries BallData -- it's just a
            # terminal "shot done" marker, sent after SetBallData/SetClubData have
            # already delivered the real data. Firing on_shot here too double-counts
            # every real shot with a second, empty-dict "shot". ACK only.
            self._send(client, build_ack("SendShot"))
            # Without this, the app is left waiting indefinitely (a stuck "waiting"
            # indicator observed live) and rejects the next shot with "Shot Not
            # Saved" -- it needs Disarm -> ShotComplete -> Arm to consider the shot
            # cycle closed and itself ready for another swing. See
            # gsp-r10-adapter's R10Session.CompleteShot for the reference sequence
            # this is ported from, and docs/E6_CONNECT.md.
            ball_data, club_data = self._pending_ball_data, self._pending_club_data
            self._pending_ball_data, self._pending_club_data = {}, {}
            threading.Thread(
                target=self._complete_shot_sequence,
                args=(client, generation, ball_data, club_data),
                daemon=True,
            ).start()
        elif msg_type in ("heartbeat", "ping"):
            self._send(client, build_ping())
        # else: unhandled message type -- leave unacked, matching V1's behavior, so
        # an unexpected type is visible in the transcript rather than silently faked.

    def _disconnect_watchdog(self, client: socket.socket, generation: int, marker: float) -> None:
        """Waits out DISCONNECT_GRACE_SECONDS after a Disconnect message. If nothing
        else arrived on this connection in that window (self._last_message_time is
        still what it was right after the Disconnect), the app really ended the
        session -- close our end so _recv_loop notices and on_connection_change(False)
        fires, instead of leaving the harness reporting CONNECTED forever. If a
        Handshake (or anything else) came in during the window, _last_message_time
        will have moved past marker and we do nothing."""
        time.sleep(DISCONNECT_GRACE_SECONDS)
        if self._generation == generation and self._last_message_time == marker:
            try:
                client.close()
            except OSError:
                pass

    def _complete_shot_sequence(
        self, client: socket.socket, generation: int, ball_data: dict, club_data: dict
    ) -> None:
        """Sends Disarm, then ShotComplete (with the shot's BallData/ClubData), then
        Arm, pausing ARM_SEQUENCE_DELAY_SECONDS between each -- the sequence the app
        needs to leave its "waiting" state and accept another shot. Bails if this
        connection has been superseded (generation changed) between steps."""
        if self._generation != generation:
            return
        self._send(client, build_disarm())
        time.sleep(ARM_SEQUENCE_DELAY_SECONDS)
        if self._generation != generation:
            return
        self._send(client, build_shot_complete(ball_data, club_data))
        time.sleep(ARM_SEQUENCE_DELAY_SECONDS)
        if self._generation != generation:
            return
        self._send(client, build_arm())

    def _ping_loop(self, generation: int) -> None:
        # Must also check self._connected, not just generation -- otherwise, once the
        # disconnect watchdog closes a dead connection's socket (no new connection
        # accepted yet, so generation hasn't changed), this loop keeps waking up every
        # PING_INTERVAL_SECONDS, tries to send on the closed socket, hits the OSError
        # path in _send(), and re-fires on_connection_change(False) forever -- observed
        # live as the harness printing "[status] DISCONNECTED" every ~10s after a real
        # end-of-session disconnect.
        while self._running and self._generation == generation and self._connected:
            time.sleep(PING_INTERVAL_SECONDS)
            if self._generation == generation and self._connected and self._client is not None:
                self._send(self._client, build_ping())

    def _send(self, client: socket.socket, data: dict) -> None:
        try:
            client.sendall((json.dumps(data) + "\n").encode("utf-8"))
        except OSError:
            self._connected = False
            if self.on_connection_change is not None:
                self.on_connection_change(False)
            return
        if self.on_send is not None:
            self.on_send(data)
