"""Experiment 1 -- baseline chatter."""

import time

from . import connect_and_wait

QUESTION = "What does GSPro send unprompted, with no heartbeat or shot traffic, over 60s?"


def run(client) -> None:
    connect_and_wait(client)
    input("Connected. Press Enter to start 60s of passive listening... ")
    time.sleep(60)
    print("done -- check transcript.log for anything GSPro sent unprompted")
