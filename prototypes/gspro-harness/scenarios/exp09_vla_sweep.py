"""Experiment 9 -- VLA sweep."""

from . import connect_and_wait, make_codec

QUESTION = "Which VLA value (0, 0.5, 1.0, 2.0) makes a putt look correct on screen?"


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    for vla in (0, 0.5, 1.0, 2.0):
        client.send(codec.build_putt(speed=5, hla=0, vla=vla, spin=0))
        print(f"sent putt VLA={vla}")
        input("Observe on screen, then press Enter for the next VLA... ")
