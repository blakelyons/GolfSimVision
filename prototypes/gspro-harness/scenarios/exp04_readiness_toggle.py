"""Experiment 4 -- readiness toggle."""

import time

from . import connect_and_wait

QUESTION = "Alternating LaunchMonitorIsReady every 10s for 2 minutes, does the indicator track live, or latch?"


def run(client) -> None:
    connect_and_wait(client)
    input("Screenshot before starting, then press Enter... ")
    end = time.monotonic() + 120
    ready = True
    while time.monotonic() < end:
        client.send_heartbeat(ready=ready)
        print(f"sent heartbeat ready={ready}")
        ready = not ready
        time.sleep(10)
    input("Final screenshot, then press Enter to finish... ")
