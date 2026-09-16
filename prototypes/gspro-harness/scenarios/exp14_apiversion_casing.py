"""Experiment 14 -- APIversion casing."""

import json

from . import connect_and_wait

QUESTION = (
    "Is APIversion casing checked? gspro-r10.exe sends the capital-V form "
    "'APIVersion' and works -- is GSPro actually case-insensitive here?"
)


def _shot_json(client, key: str) -> bytes:
    payload = {
        "DeviceID": client.device_id,
        "Units": client.units,
        "ShotNumber": client.next_shot_number(),
        key: "1",
        "BallData": {"Speed": 5.0, "SpinAxis": 0.0, "TotalSpin": 0.0, "HLA": 0.0, "VLA": 0.0},
        "ShotDataOptions": {
            "ContainsBallData": True,
            "ContainsClubData": False,
            "LaunchMonitorIsReady": True,
            "LaunchMonitorBallDetected": True,
            "IsHeartBeat": False,
        },
    }
    return json.dumps(payload, separators=(",", ":")).encode("utf-8")


def run(client) -> None:
    connect_and_wait(client)

    client.send_raw(_shot_json(client, key="APIversion"))
    print("sent with lowercase-v key 'APIversion' (spec-correct)")
    input("Check the response, then press Enter to send the capital-V variant... ")

    client.send_raw(_shot_json(client, key="APIVersion"))
    print("sent with capital-V key 'APIVersion' (what gspro-r10.exe actually sends)")
    input("Check the response, then press Enter to finish... ")
