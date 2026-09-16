"""Experiment 8 -- putt, VLA = 0."""

from . import connect_and_wait, make_codec

QUESTION = "Does a putt with VLA=0 and TotalSpin=0 get accepted, and what does the ball do on screen?"


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
    print("sent putt speed=5 hla=0 vla=0 totalspin=0 -- check GSPro and transcript.log")
