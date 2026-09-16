"""Experiment 12 -- speed range."""

from . import connect_and_wait, make_codec

QUESTION = "At what speed (1, 3, 5, 8, 12 mph) does GSPro start to misbehave, at the low or high end?"


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    for speed in (1, 3, 5, 8, 12):
        client.send(codec.build_putt(speed=speed, hla=0, vla=0, spin=0))
        print(f"sent putt speed={speed}mph")
        input("Observe on screen, then press Enter for the next speed... ")
