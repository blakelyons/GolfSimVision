"""Stdlib unittest coverage for protocol.py's wire format, framing, and the
GSProDirect connection (against a local fake TCP server — no real GSPro needed).

Run: python -m unittest (from prototypes/gspro-harness/, either machine).
"""

import json
import socket
import threading
import time
import unittest

from protocol import (
    BallData,
    ClubData,
    ConnectionState,
    GSProCodec,
    GSProDirect,
    GSProResponse,
    ShotDataOptions,
    ShotPayload,
    _MAX_FRAME_BYTES,
    find_json_end,
    parse_response,
    rolling_spin_rpm,
    serialize_payload,
)


def wait_until(predicate, timeout=2.0, interval=0.02):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(interval)
    return predicate()


class FakeGSProServer:
    """A minimal TCP listener standing in for GSPro, for connection tests."""

    def __init__(self):
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._sock.bind(("127.0.0.1", 0))
        self._sock.listen(1)
        self.port = self._sock.getsockname()[1]
        self._conns = []
        self._stop = threading.Event()
        self._accepted = threading.Event()
        self._thread = threading.Thread(target=self._accept_loop, daemon=True)
        self._thread.start()

    def _accept_loop(self):
        self._sock.settimeout(0.2)
        while not self._stop.is_set():
            try:
                conn, _ = self._sock.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            self._conns.append(conn)
            self._accepted.set()

    def wait_for_connection(self, timeout=2.0):
        return self._accepted.wait(timeout=timeout)

    def recv_frame(self, timeout=2.0):
        """Blocks for one complete JSON frame from the most recent connection."""
        assert self._conns, "no client has connected yet"
        conn = self._conns[-1]
        conn.settimeout(timeout)
        buffer = bytearray()
        while True:
            end = find_json_end(bytes(buffer))
            if end is not None:
                return bytes(buffer[:end])
            data = conn.recv(4096)
            if not data:
                raise ConnectionError("fake server: peer closed before a full frame arrived")
            buffer.extend(data)

    def send_to_client(self, raw: bytes):
        self._conns[-1].sendall(raw)

    def close(self):
        self._stop.set()
        self._thread.join(timeout=2.0)
        for conn in self._conns:
            try:
                conn.close()
            except OSError:
                pass
        try:
            self._sock.close()
        except OSError:
            pass


class TestSerializePayload(unittest.TestCase):
    def test_round_trip_shape(self):
        payload = ShotPayload(
            DeviceID="GolfSimVision",
            Units="Yards",
            ShotNumber=1,
            APIversion="1",
            BallData=BallData(Speed=5.0, HLA=-1.5, VLA=0.0),
            ShotDataOptions=ShotDataOptions(
                ContainsBallData=True,
                ContainsClubData=False,
                LaunchMonitorIsReady=True,
                LaunchMonitorBallDetected=True,
                IsHeartBeat=False,
            ),
        )
        raw = serialize_payload(payload)
        obj = __import__("json").loads(raw.decode("utf-8"))

        self.assertEqual(obj["DeviceID"], "GolfSimVision")
        self.assertEqual(obj["APIversion"], "1")
        self.assertIsInstance(obj["APIversion"], str)
        self.assertEqual(obj["BallData"]["Speed"], 5.0)
        self.assertEqual(obj["ShotDataOptions"]["LaunchMonitorBallDetected"], True)

    def test_no_trailing_newline_and_compact_separators(self):
        payload = ShotPayload(DeviceID="d", Units="Yards", ShotNumber=1, APIversion="1")
        raw = serialize_payload(payload)
        self.assertFalse(raw.endswith(b"\n"))
        self.assertNotIn(b", ", raw)
        self.assertNotIn(b": ", raw)


class TestParseResponse(unittest.TestCase):
    def test_parses_well_formed_response(self):
        raw = b'{"Code":200,"Message":"OK","Player":{"Handed":"RH"}}'
        resp = parse_response(raw)
        self.assertEqual(resp, GSProResponse(Code=200, Message="OK", Player={"Handed": "RH"}))

    def test_defaults_missing_fields(self):
        raw = b"{}"
        resp = parse_response(raw)
        self.assertEqual(resp.Code, 0)
        self.assertEqual(resp.Message, "")
        self.assertIsNone(resp.Player)

    def test_raises_value_error_on_malformed_json(self):
        with self.assertRaises(ValueError):
            parse_response(b"{not json")


class TestFindJsonEnd(unittest.TestCase):
    def test_two_complete_objects_in_one_buffer(self):
        buf = b'{"a":1}{"b":2}'
        end = find_json_end(buf)
        self.assertEqual(end, 7)
        self.assertEqual(buf[:end], b'{"a":1}')
        # Second object is independently framed from the remainder.
        remainder = buf[end:]
        end2 = find_json_end(remainder)
        self.assertEqual(remainder[:end2], b'{"b":2}')

    def test_object_split_across_two_extends(self):
        first_half = b'{"a":1,"b"'
        second_half = b':2}'
        self.assertIsNone(find_json_end(first_half))
        buf = first_half + second_half
        end = find_json_end(buf)
        self.assertEqual(end, len(buf))

    def test_brace_inside_string_does_not_affect_depth(self):
        buf = b'{"Message":"a { b } c"}'
        end = find_json_end(buf)
        self.assertEqual(end, len(buf))

    def test_escaped_quote_inside_string_does_not_end_string_early(self):
        buf = b'{"Message":"say \\"hi\\" now"}'
        end = find_json_end(buf)
        self.assertEqual(end, len(buf))

    def test_no_complete_frame_returns_none(self):
        buf = b"{" * 100 + b'"a":1'
        self.assertIsNone(find_json_end(buf))

    def test_over_64kb_with_no_complete_frame_returns_none(self):
        buf = b'{"a":"' + (b"x" * (65 * 1024)) + b'"'  # unterminated string, unclosed brace
        self.assertGreater(len(buf), 64 * 1024)
        self.assertIsNone(find_json_end(buf))


class TestGSProDirectConnection(unittest.TestCase):
    def setUp(self):
        self.server = FakeGSProServer()
        self.client = GSProDirect(
            host="127.0.0.1",
            port=self.server.port,
            device_id="GolfSimVision",
            backoff_seconds=(0.05, 0.05, 0.1),
        )

    def tearDown(self):
        self.client.disconnect()
        self.server.close()

    def test_connect_transitions_disabled_to_connected(self):
        self.assertEqual(self.client.state, ConnectionState.DISABLED)
        self.client.connect()
        self.assertTrue(wait_until(lambda: self.client.state == ConnectionState.CONNECTED))
        self.assertTrue(self.server.wait_for_connection(timeout=0.1))

    def test_disconnect_stops_the_connection_thread(self):
        self.client.connect()
        wait_until(lambda: self.client.state == ConnectionState.CONNECTED)
        self.client.disconnect()
        self.assertEqual(self.client.state, ConnectionState.STOPPED)

    def test_send_raises_connection_error_when_not_connected(self):
        payload = ShotPayload(DeviceID="d", Units="Yards", ShotNumber=1, APIversion="1")
        with self.assertRaises(ConnectionError):
            self.client.send(payload)

    def test_send_heartbeat_delivers_expected_fields(self):
        self.client.connect()
        wait_until(lambda: self.client.state == ConnectionState.CONNECTED)
        self.client.send_heartbeat(ready=True, ball_detected=False)
        frame = self.server.recv_frame()
        obj = json.loads(frame)
        self.assertEqual(obj["DeviceID"], "GolfSimVision")
        self.assertEqual(obj["APIversion"], "1")
        self.assertIsInstance(obj["ShotNumber"], int)
        self.assertEqual(obj["ShotDataOptions"]["IsHeartBeat"], True)
        self.assertEqual(obj["ShotDataOptions"]["LaunchMonitorIsReady"], True)
        self.assertEqual(obj["ShotDataOptions"]["LaunchMonitorBallDetected"], False)

    def test_shot_number_seeded_from_epoch_seconds_and_increments(self):
        now = int(time.time())
        self.assertAlmostEqual(self.client.last_shot_number, now, delta=5)
        first = self.client.next_shot_number()
        second = self.client.next_shot_number()
        self.assertEqual(second, first + 1)

    def test_override_shot_number(self):
        self.client.override_shot_number(42)
        self.assertEqual(self.client.next_shot_number(), 42)
        self.assertEqual(self.client.next_shot_number(), 43)

    def test_on_receive_and_on_response_fire_for_inbound_frames(self):
        received = threading.Event()
        raw_frames = []
        responses = []
        self.client.on_receive = raw_frames.append
        self.client.on_response = lambda r: (responses.append(r), received.set())

        self.client.connect()
        wait_until(lambda: self.client.state == ConnectionState.CONNECTED)
        self.server.send_to_client(b'{"Code":200,"Message":"OK"}')

        self.assertTrue(received.wait(timeout=2.0))
        self.assertEqual(raw_frames, [b'{"Code":200,"Message":"OK"}'])
        self.assertEqual(responses, [GSProResponse(Code=200, Message="OK", Player=None)])

    def test_on_status_fires_on_state_transitions(self):
        states = []
        self.client.on_status = states.append
        self.client.connect()
        wait_until(lambda: self.client.state == ConnectionState.CONNECTED)
        self.assertIn(ConnectionState.CONNECTING, states)
        self.assertIn(ConnectionState.CONNECTED, states)

    def test_drop_forces_reconnect_and_reconnecting_state(self):
        self.client.connect()
        wait_until(lambda: self.client.state == ConnectionState.CONNECTED)
        self.client.drop()
        self.assertTrue(
            wait_until(lambda: self.client.state == ConnectionState.RECONNECTING, timeout=2.0)
        )
        # The connection thread keeps running and reconnects against the same server.
        self.assertTrue(
            wait_until(lambda: self.client.state == ConnectionState.CONNECTED, timeout=2.0)
        )

    def test_recv_loop_resyncs_after_over_64kb_with_no_complete_frame(self):
        # find_json_end() itself is covered directly in TestFindJsonEnd; this
        # exercises the caller's side of the same case (SPEC.md's "caller's
        # resync path is exercised") -- feed the real _recv_loop more than
        # _MAX_FRAME_BYTES of an unterminated frame and confirm it clears the
        # buffer and keeps parsing, rather than getting stuck or crashing.
        received = threading.Event()
        responses = []
        self.client.on_response = lambda r: (responses.append(r), received.set())

        self.client.connect()
        wait_until(lambda: self.client.state == ConnectionState.CONNECTED)
        self.server.send_to_client(b'{"a":"' + (b"x" * (_MAX_FRAME_BYTES + 1)))
        # Give the recv loop a moment to drain and clear the oversized buffer
        # before the valid frame arrives -- otherwise both could land in the
        # same recv() and the clear would wipe the valid frame out too.
        time.sleep(0.3)
        self.server.send_to_client(b'{"Code":200,"Message":"OK"}')

        self.assertTrue(received.wait(timeout=2.0))
        self.assertEqual(responses, [GSProResponse(Code=200, Message="OK", Player=None)])

    def test_connecting_state_used_before_first_ever_connection(self):
        # Point at a port nothing is listening on, so it never connects.
        dead_client = GSProDirect(
            host="127.0.0.1", port=1, device_id="d", backoff_seconds=(0.05,)
        )
        states = []
        dead_client.on_status = states.append
        dead_client.connect()
        wait_until(lambda: ConnectionState.CONNECTING in states and len(states) >= 2)
        dead_client.disconnect()
        self.assertNotIn(ConnectionState.RECONNECTING, states)


class TestRollingSpinRpm(unittest.TestCase):
    def test_formula(self):
        self.assertEqual(rolling_spin_rpm(5.0), 1000.0)
        self.assertEqual(rolling_spin_rpm(0.0), 0.0)


class TestGSProCodec(unittest.TestCase):
    def setUp(self):
        self._counter = 100

        def next_shot_number():
            n = self._counter
            self._counter += 1
            return n

        self.codec = GSProCodec(device_id="GolfSimVision", next_shot_number=next_shot_number)

    def test_build_putt_defaults_spin_to_rolling_estimate(self):
        payload = self.codec.build_putt(speed=5.0, hla=-1.5, vla=0.0)
        self.assertEqual(payload.BallData.BackSpin, rolling_spin_rpm(5.0))
        self.assertEqual(payload.BallData.TotalSpin, rolling_spin_rpm(5.0))
        self.assertEqual(payload.BallData.Speed, 5.0)
        self.assertEqual(payload.BallData.HLA, -1.5)
        self.assertEqual(payload.BallData.VLA, 0.0)

    def test_build_putt_uses_explicit_spin_when_given(self):
        payload = self.codec.build_putt(speed=5.0, hla=0.0, vla=0.0, spin=250.0)
        self.assertEqual(payload.BallData.BackSpin, 250.0)
        self.assertEqual(payload.BallData.TotalSpin, 250.0)

    def test_build_putt_sets_shape_and_shot_options(self):
        payload = self.codec.build_putt(speed=5.0, hla=0.0, vla=0.0)
        self.assertEqual(payload.DeviceID, "GolfSimVision")
        self.assertEqual(payload.Units, "Yards")
        self.assertEqual(payload.APIversion, "1")
        self.assertTrue(payload.ShotDataOptions.ContainsBallData)
        self.assertFalse(payload.ShotDataOptions.ContainsClubData)
        self.assertFalse(payload.ShotDataOptions.IsHeartBeat)

    def test_build_putt_draws_shot_number_from_provided_callable(self):
        first = self.codec.build_putt(speed=5.0, hla=0.0, vla=0.0)
        second = self.codec.build_putt(speed=5.0, hla=0.0, vla=0.0)
        self.assertEqual(first.ShotNumber, 100)
        self.assertEqual(second.ShotNumber, 101)

    def test_build_shot_sets_full_ball_data(self):
        payload = self.codec.build_shot(
            speed=150.0, hla=1.0, vla=15.0, total_spin=3000.0, spin_axis=-5.0
        )
        self.assertEqual(payload.BallData.Speed, 150.0)
        self.assertEqual(payload.BallData.TotalSpin, 3000.0)
        self.assertEqual(payload.BallData.SpinAxis, -5.0)
        # back_spin/side_spin default when not given explicitly.
        self.assertEqual(payload.BallData.BackSpin, 3000.0)
        self.assertEqual(payload.BallData.SideSpin, 0.0)

    def test_build_shot_accepts_explicit_back_and_side_spin(self):
        payload = self.codec.build_shot(
            speed=150.0,
            hla=1.0,
            vla=15.0,
            total_spin=3000.0,
            spin_axis=-5.0,
            back_spin=2800.0,
            side_spin=400.0,
        )
        self.assertEqual(payload.BallData.BackSpin, 2800.0)
        self.assertEqual(payload.BallData.SideSpin, 400.0)


if __name__ == "__main__":
    unittest.main()
