"""Experiment 19 -- malformed input."""

import json

from . import connect_and_wait

QUESTION = (
    "What error code (if any) comes back for a payload missing a required "
    "BallData field, and for outright malformed JSON? Does GSPro drop the "
    "connection either way?"
)


def run(client) -> None:
    connect_and_wait(client)

    missing_field = {
        "DeviceID": client.device_id,
        "Units": client.units,
        "ShotNumber": client.next_shot_number(),
        "APIversion": "1",
        "BallData": {"Speed": 5.0},  # missing SpinAxis, TotalSpin, HLA, VLA
        "ShotDataOptions": {
            "ContainsBallData": True,
            "ContainsClubData": False,
            "LaunchMonitorIsReady": True,
            "LaunchMonitorBallDetected": True,
            "IsHeartBeat": False,
        },
    }
    client.send_raw(json.dumps(missing_field, separators=(",", ":")).encode("utf-8"))
    print("sent BallData missing required fields")
    input("Check the response, then press Enter to send malformed JSON... ")

    client.send_raw(b'{"DeviceID":"GolfSimVision", not valid json}')
    print(f"sent malformed JSON -- state={client.state.name}, check transcript.log")
