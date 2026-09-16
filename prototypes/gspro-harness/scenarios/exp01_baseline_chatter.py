"""Experiment 1 -- baseline chatter."""

import time

from . import connect_and_announce

QUESTION = (
    "After one heartbeat registers the connection, what does GSPro send unprompted "
    "over the next 60s with no further heartbeat or shot traffic?"
)


def run(client) -> None:
    # A bare TCP connect isn't enough for GSPro to recognize the client (discovered
    # live during exp05) -- one heartbeat first, then genuinely nothing for 60s.
    connect_and_announce(client)
    input("Connected and announced. Press Enter to start 60s of passive listening... ")
    time.sleep(60)
    print("done -- check transcript.log for anything GSPro sent unprompted")
