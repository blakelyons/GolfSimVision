"""Experiment 2 -- readiness = false."""

import time

from . import connect_and_wait

QUESTION = (
    "With LaunchMonitorIsReady=false sent every 5s for 30s, "
    "what does GSPro's readiness indicator show?"
)


def run(client) -> None:
    connect_and_wait(client)
    input("Screenshot GSPro's readiness indicator now (before), then press Enter... ")
    end = time.monotonic() + 30
    while time.monotonic() < end:
        client.send_heartbeat(ready=False)
        print("sent heartbeat ready=false")
        time.sleep(5)
    input("Screenshot the indicator again (after), then press Enter to finish... ")
