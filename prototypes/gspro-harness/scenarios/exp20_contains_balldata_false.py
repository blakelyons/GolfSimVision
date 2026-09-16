"""Experiment 20 -- ContainsBallData false + BallData present."""

from . import connect_and_wait, make_codec

QUESTION = "When ContainsBallData is false but a full BallData object is present anyway, which does GSPro honor?"


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    payload = codec.build_putt(speed=5, hla=0, vla=0, spin=0)
    payload.ShotDataOptions.ContainsBallData = False
    client.send(payload)
    print("sent putt with real BallData but ContainsBallData=false -- check GSPro and transcript.log")
