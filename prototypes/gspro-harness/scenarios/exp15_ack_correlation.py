"""Experiment 15 -- ack correlation."""

from . import connect_and_wait, make_codec

QUESTION = (
    "Sending 3 shots in rapid succession, how many 200 responses come back, "
    "and is there any way to correlate a response to a specific shot?"
)


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    for i in range(3):
        payload = codec.build_putt(speed=5 + i, hla=0, vla=0, spin=0)
        client.send(payload)
        print(f"sent shot {i + 1}/3, ShotNumber={payload.ShotNumber}")
    print("check transcript.log for how many 200s came back and their timing")
