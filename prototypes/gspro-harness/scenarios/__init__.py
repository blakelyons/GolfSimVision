"""Shared helpers for scenario modules.

Each exp*.py exposes a module-level QUESTION string and run(client) -> None,
per SPEC.md's scenario contract. harness.py imports the module by name and
calls run(client) directly -- client is the one GSProDirect instance it owns,
already wired to the session's transcript.
"""

import time

from protocol import GSProCodec, GSProDirect


def make_codec(client: GSProDirect) -> GSProCodec:
    return GSProCodec(
        device_id=client.device_id,
        units=client.units,
        next_shot_number=client.next_shot_number,
    )


def connect_and_wait(client: GSProDirect, timeout: float = 10.0) -> None:
    """connect() only starts a background thread, so scenarios that don't want
    a human to eyeball '[status] CONNECTED' before sending anything block here
    until the socket is actually up."""
    client.connect()
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if client.is_connected():
            return
        time.sleep(0.05)
    raise TimeoutError(f"did not connect within {timeout}s")


def connect_and_announce(client: GSProDirect, ready: bool = True, timeout: float = 10.0) -> None:
    """connect_and_wait(), then send one heartbeat.

    Discovered live (2026-09-16, exp05 retest): a bare TCP connect isn't enough for
    GSPro to register a client -- its own Connect window stayed "Not Connected" and
    produced no 201s on club changes until a single heartbeat went out, after which
    both worked immediately. Scenarios that only connect-and-wait for something to
    happen (1, 5, 6, 17) need this instead of connect_and_wait() or GSPro may never
    actually be looking at them.
    """
    connect_and_wait(client, timeout=timeout)
    client.send_heartbeat(ready=ready)
