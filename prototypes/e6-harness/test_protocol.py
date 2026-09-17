import socket
import time
import unittest

import protocol
from protocol import (
    E6Server,
    build_ack,
    build_auth_response,
    build_handshake_response,
    build_ping,
    extract_json,
)


class TestExtractJson(unittest.TestCase):
    def test_single_complete_object(self):
        obj, rest = extract_json('{"Type":"Handshake"}')
        self.assertEqual(obj, {"Type": "Handshake"})
        self.assertEqual(rest, "")

    def test_two_objects_in_one_buffer_returns_first(self):
        obj, rest = extract_json('{"Type":"A"}{"Type":"B"}')
        self.assertEqual(obj, {"Type": "A"})
        self.assertEqual(rest, '{"Type":"B"}')
        obj2, rest2 = extract_json(rest)
        self.assertEqual(obj2, {"Type": "B"})
        self.assertEqual(rest2, "")

    def test_incomplete_object_returns_none_and_full_buffer(self):
        obj, rest = extract_json('{"Type":"Handsh')
        self.assertIsNone(obj)
        self.assertEqual(rest, '{"Type":"Handsh')

    def test_brace_inside_string_does_not_affect_depth(self):
        obj, rest = extract_json('{"Type":"a{b}c"}')
        self.assertEqual(obj, {"Type": "a{b}c"})
        self.assertEqual(rest, "")

    def test_escaped_quote_inside_string(self):
        obj, rest = extract_json('{"Type":"a\\"b"}')
        self.assertEqual(obj, {"Type": 'a"b'})
        self.assertEqual(rest, "")

    def test_malformed_complete_object_drops_and_continues(self):
        # Balanced braces but invalid JSON inside -- should not raise, just drop it.
        obj, rest = extract_json('{not valid json}{"Type":"B"}')
        self.assertIsNone(obj)
        self.assertEqual(rest, '{"Type":"B"}')

    def test_empty_buffer(self):
        obj, rest = extract_json("")
        self.assertIsNone(obj)
        self.assertEqual(rest, "")


class TestMessageBuilders(unittest.TestCase):
    def test_handshake_response_shape(self):
        resp = build_handshake_response()
        self.assertEqual(resp["Type"], "Handshake")
        self.assertIn("Challenge", resp)
        self.assertIn("E6Version", resp)
        self.assertIn("ProtocolVersion", resp)
        self.assertIn("RequiredProtocolVersion", resp)

    def test_auth_response_shape(self):
        self.assertEqual(build_auth_response(), {"Type": "Authentication", "Success": "true"})

    def test_ack_shape(self):
        self.assertEqual(
            build_ack("SendShot"),
            {"Type": "ACK", "SubType": "SendShot", "Details": "Success."},
        )

    def test_ping_shape(self):
        self.assertEqual(build_ping(), {"Type": "SimCommand", "SubType": "Ping"})


class TestReconnectDoesNotDuplicatePings(unittest.TestCase):
    """Regression test for the bug found live during P0-4 2026-09-17: a reconnect left
    the previous connection's ping thread running, so after N reconnects, N ping
    threads all fired SimCommand/Ping at the (single, current) client at once --
    the Garmin Golf app's connection got confused by the duplicate traffic."""

    def test_only_one_ping_stream_survives_a_reconnect(self):
        original_interval = protocol.PING_INTERVAL_SECONDS
        protocol.PING_INTERVAL_SECONDS = 0.05
        server = E6Server(port=0)
        try:
            server.start()
            time.sleep(0.05)  # let the accept loop start listening

            # First connection, then abandon it without a clean close -- the server
            # should treat the *next* accept as superseding it.
            first = socket.create_connection(("127.0.0.1", server.port), timeout=2.0)
            first.sendall(b'{"Type":"Handshake"}\n')
            time.sleep(0.1)

            second = socket.create_connection(("127.0.0.1", server.port), timeout=2.0)
            second.sendall(b'{"Type":"Handshake"}\n')

            # Drain whatever arrives on the second connection for a few ping intervals.
            second.settimeout(2.0)
            deadline = time.monotonic() + 0.4
            buf = b""
            while time.monotonic() < deadline:
                try:
                    chunk = second.recv(4096)
                except socket.timeout:
                    break
                if not chunk:
                    break
                buf += chunk

            ping_count = buf.count(b'"SubType":"Ping"')
            # At ~0.05s intervals over ~0.4s we'd expect roughly 8 pings from ONE
            # ping loop. A stacked-duplicate bug produces roughly double (or more)
            # that count from the abandoned first connection's ping loop still firing.
            self.assertLessEqual(
                ping_count,
                12,
                f"got {ping_count} pings in ~0.4s -- looks like more than one ping "
                "loop is running (the reconnect duplicate-ping-thread bug)",
            )

            for sock in (first, second):
                try:
                    sock.close()
                except OSError:
                    pass
        finally:
            server.stop()
            protocol.PING_INTERVAL_SECONDS = original_interval


class TestRealShotMessageShape(unittest.TestCase):
    """Regression test for the P0-4 2026-09-17 live-shot finding: the current Garmin
    Golf app sends SetBallData + SetClubData for a real shot, never SendShot (V1's
    assumption). SetClubData had no handler at all, so it went unacked -- which lined
    up with the app's "Disconnected from E6" popup showing up right after a real shot
    in live testing. See docs/E6_CONNECT.md."""

    def _connect_and_authenticate(self, server: E6Server) -> socket.socket:
        client = socket.create_connection(("127.0.0.1", server.port), timeout=2.0)
        client.settimeout(2.0)
        client.sendall(b'{"Type":"Handshake"}\n')
        client.recv(4096)
        client.sendall(b'{"Type":"Challenge","Developer":"x","Hash":"y"}\n')
        client.recv(4096)
        return client

    def test_setballdata_triggers_on_shot(self):
        shots = []
        server = E6Server(port=0, on_shot=shots.append)
        try:
            server.start()
            time.sleep(0.05)
            client = self._connect_and_authenticate(server)
            client.sendall(
                b'{"Type":"SetBallData","BallData":{"BallSpeed":68.65208}}\n'
            )
            resp = client.recv(4096)
            self.assertIn(b'"SubType": "SetBallData"', resp)
            client.close()
        finally:
            server.stop()
        self.assertEqual(shots, [{"BallSpeed": 68.65208}])

    def test_setclubdata_is_acked(self):
        server = E6Server(port=0)
        try:
            server.start()
            time.sleep(0.05)
            client = self._connect_and_authenticate(server)
            client.sendall(
                b'{"Type":"SetClubData","ClubData":{"ClubHeadSpeed":49.02663}}\n'
            )
            resp = client.recv(4096)
            client.close()
        finally:
            server.stop()
        self.assertIn(b'"SubType": "SetClubData"', resp)

    def test_empty_sendshot_does_not_double_fire_on_shot(self):
        """Regression test for the P0-4 2026-09-17 live finding: the harness printed a
        second, empty-dict "shot #2" right after every real shot. Real SendShot never
        carries BallData (it's just a terminal marker after SetBallData already fired
        on_shot) -- so SendShot must not fire on_shot at all, even with a default {}."""
        shots = []
        server = E6Server(port=0, on_shot=shots.append)
        try:
            server.start()
            time.sleep(0.05)
            client = self._connect_and_authenticate(server)
            client.sendall(
                b'{"Type":"SetBallData","BallData":{"BallSpeed":92.87188}}\n'
            )
            client.recv(4096)
            client.sendall(b'{"Type":"SendShot"}\n')
            resp = client.recv(4096)
            client.close()
        finally:
            server.stop()
        self.assertIn(b'"SubType": "SendShot"', resp)
        self.assertEqual(shots, [{"BallSpeed": 92.87188}])

    def test_sendshot_triggers_disarm_shotcomplete_arm_sequence(self):
        """Regression test for the P0-4 2026-09-17 live finding: without this
        sequence, the app hangs in a "waiting" state after a shot and rejects the
        next one with "Shot Not Saved". Ported from gsp-r10-adapter's
        R10Session.CompleteShot, a known-working reference E6 Connect server."""
        original_delay = protocol.ARM_SEQUENCE_DELAY_SECONDS
        protocol.ARM_SEQUENCE_DELAY_SECONDS = 0.05
        server = E6Server(port=0)
        try:
            server.start()
            time.sleep(0.05)
            client = self._connect_and_authenticate(server)
            client.sendall(
                b'{"Type":"SetBallData","BallData":{"BallSpeed":92.87188}}\n'
            )
            client.recv(4096)
            client.sendall(
                b'{"Type":"SetClubData","ClubData":{"ClubHeadSpeed":65.226}}\n'
            )
            client.recv(4096)
            client.sendall(b'{"Type":"SendShot"}\n')

            messages = []
            buf = ""
            deadline = time.monotonic() + 2.0
            while time.monotonic() < deadline and len(messages) < 4:
                buf += client.recv(4096).decode("utf-8")
                while True:
                    obj, buf = extract_json(buf)
                    if obj is None:
                        break
                    messages.append(obj)
            client.close()
        finally:
            server.stop()
            protocol.ARM_SEQUENCE_DELAY_SECONDS = original_delay

        self.assertEqual(
            [m.get("SubType") for m in messages],
            ["SendShot", "Disarm", "ShotComplete", "Arm"],
        )
        shot_complete = messages[2]
        self.assertEqual(shot_complete["Details"]["BallData"], {"BallSpeed": 92.87188})
        self.assertEqual(
            shot_complete["Details"]["ClubData"], {"ClubHeadSpeed": 65.226}
        )


class TestDisconnectMessage(unittest.TestCase):
    """Regression test for the P0-4 2026-09-17 finding: ending the session in the app
    left the harness reporting CONNECTED forever, because {"Type":"Disconnect"} had no
    handler at all. But Disconnect is *also* sent mid-session immediately before a
    same-socket Handshake retry (~10ms apart live -- a soft restart, not a real
    disconnect), so a real Disconnect must only close the socket after a short grace
    window with nothing else arriving. See docs/E6_CONNECT.md."""

    def setUp(self):
        original_grace = protocol.DISCONNECT_GRACE_SECONDS
        protocol.DISCONNECT_GRACE_SECONDS = 0.1
        self.addCleanup(setattr, protocol, "DISCONNECT_GRACE_SECONDS", original_grace)

    def test_real_disconnect_eventually_flips_connected_false(self):
        events = []
        server = E6Server(port=0, on_connection_change=events.append)
        try:
            server.start()
            time.sleep(0.05)
            client = socket.create_connection(("127.0.0.1", server.port), timeout=2.0)
            client.sendall(b'{"Type":"Handshake"}\n')
            time.sleep(0.05)
            client.sendall(b'{"Type":"Disconnect"}\n')

            deadline = time.monotonic() + 2.0
            while time.monotonic() < deadline and server.is_connected():
                time.sleep(0.02)

            self.assertFalse(server.is_connected())
            self.assertIn(False, events)
            client.close()
        finally:
            server.stop()

    def test_disconnect_followed_by_handshake_does_not_close_socket(self):
        server = E6Server(port=0)
        try:
            server.start()
            time.sleep(0.05)
            client = socket.create_connection(("127.0.0.1", server.port), timeout=2.0)
            client.settimeout(2.0)
            client.sendall(b'{"Type":"Handshake"}\n')
            client.recv(4096)

            # Same-socket soft restart: Disconnect immediately followed by a fresh
            # Handshake, well within the (shortened) grace window.
            client.sendall(b'{"Type":"Disconnect"}\n')
            client.sendall(b'{"Type":"Handshake"}\n')
            client.recv(4096)  # the second Handshake's own response

            time.sleep(0.3)  # outlast the grace window
            self.assertTrue(server.is_connected())

            # The socket must still be usable -- a real send/recv round trip proves
            # the watchdog did not close it out from under the ongoing session.
            client.sendall(b'{"Type":"Challenge","Developer":"x","Hash":"y"}\n')
            resp = client.recv(4096)
            self.assertIn(b'"Type": "Authentication"', resp)
            client.close()
        finally:
            server.stop()

    def test_real_disconnect_does_not_repeat_false_forever(self):
        """Regression test for the P0-4 2026-09-17 live finding: after a real
        end-of-session Disconnect, the harness printed "[status] DISCONNECTED" every
        ~10s forever. Root cause: _ping_loop only checked generation, not _connected,
        so once the watchdog closed the dead socket it kept waking up on the old
        PING_INTERVAL_SECONDS cadence, hit the OSError path in _send(), and re-fired
        on_connection_change(False) every cycle -- with no new connection, generation
        never changes so the loop never stopped on its own."""
        original_ping_interval = protocol.PING_INTERVAL_SECONDS
        protocol.PING_INTERVAL_SECONDS = 0.05
        events = []
        server = E6Server(port=0, on_connection_change=events.append)
        try:
            server.start()
            time.sleep(0.05)
            client = socket.create_connection(("127.0.0.1", server.port), timeout=2.0)
            client.sendall(b'{"Type":"Handshake"}\n')
            time.sleep(0.05)
            client.sendall(b'{"Type":"Disconnect"}\n')

            deadline = time.monotonic() + 2.0
            while time.monotonic() < deadline and server.is_connected():
                time.sleep(0.02)
            self.assertFalse(server.is_connected())

            # Give the ping loop several chances (at the shortened interval) to have
            # re-fired if the bug were present.
            time.sleep(0.5)
            false_count = events.count(False)
            self.assertEqual(
                false_count,
                1,
                f"on_connection_change(False) fired {false_count} times -- expected "
                "exactly once (the ping loop is re-firing it on a loop)",
            )
            client.close()
        finally:
            server.stop()
            protocol.PING_INTERVAL_SECONDS = original_ping_interval


if __name__ == "__main__":
    unittest.main()
