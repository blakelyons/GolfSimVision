"""Experiment 18 -- heartbeat absence."""

import time

from . import connect_and_wait

QUESTION = "With no heartbeat or shot traffic for 5 minutes, does GSPro time us out or close the connection?"


def run(client) -> None:
    connect_and_wait(client)
    print("Connected. Sending nothing for 5 minutes...")
    for remaining in range(300, 0, -30):
        print(f"  {remaining}s remaining, state={client.state.name}")
        time.sleep(30)
    print(f"done -- final state={client.state.name}, check transcript.log")
