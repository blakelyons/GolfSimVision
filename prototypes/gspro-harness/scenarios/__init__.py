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
