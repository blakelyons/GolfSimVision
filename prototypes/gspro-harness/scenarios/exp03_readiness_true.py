"""Experiment 3 -- readiness = true. The headline question: does GSPro's
readiness indicator actually move when fed LaunchMonitorIsReady=true?"""

import time

from . import connect_and_wait

QUESTION = (
    "With LaunchMonitorIsReady=true sent every 5s for 30s, does GSPro's "
    "readiness indicator change? (Every heartbeat gspro-r10.exe sends carries "
    "false -- this settles whether the indicator is simply never fed.)"
)


def run(client) -> None:
    connect_and_wait(client)
    input("Screenshot GSPro's readiness indicator now (before), then press Enter... ")
    end = time.monotonic() + 30
    while time.monotonic() < end:
        client.send_heartbeat(ready=True)
        print("sent heartbeat ready=true")
        time.sleep(5)
    input("Screenshot the indicator again (after), then press Enter to finish... ")
