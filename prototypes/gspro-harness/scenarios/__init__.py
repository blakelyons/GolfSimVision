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


# Matches ../../../example-apps/openflight-.../src/openflight/sim/transport.py's
# default heartbeat_interval_s (5.0) -- see connect_and_announce's docstring for why.
_ANNOUNCE_SETTLE_SECONDS = 5.0


def connect_and_announce(client: GSProDirect, ready: bool = True, timeout: float = 10.0) -> None:
    """connect_and_wait(), then send one heartbeat, then a pause.

    Discovered live (2026-09-16, exp05 retest): a bare TCP connect isn't enough for
    GSPro to register a client -- its own Connect window stayed "Not Connected" and
    produced no 201s on club changes until a heartbeat went out.

    The settle delay's actual justification turned out NOT to be "GSPro needs N
    seconds after connecting" -- a controlled before/after (2026-09-16, exp07 retest,
    see RUNBOOK.md's Troubleshooting section) showed identical code/delay/payload
    failing, then succeeding right after a GSPro-side "Reset GSPro Connect", with no
    other change. The real culprit looks like GSPro's OpenAPI connection handling
    degrading under the connect/disconnect churn this harness does on every
    `--scenario` run, not insufficient delay. The delay is kept anyway (matching
    openflight/sim/transport.py's TcpSimClient default `heartbeat_interval_s`, 5.0) as
    reasonable hygiene, but if a scenario's shot gets no response, try a GSPro-side
    reset before assuming this needs tuning further.
    """
    connect_and_wait(client, timeout=timeout)
    client.send_heartbeat(ready=ready)
    time.sleep(_ANNOUNCE_SETTLE_SECONDS)
