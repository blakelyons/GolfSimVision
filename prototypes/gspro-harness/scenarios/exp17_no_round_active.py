"""Experiment 17 -- no round active."""

from . import connect_and_wait, make_codec

QUESTION = "With no round active (at GSPro's menu), is a shot accepted, ignored, or does it error?"


def run(client) -> None:
    connect_and_wait(client)
    input("Make sure GSPro is at the menu with no round in progress, then press Enter... ")
    codec = make_codec(client)
    client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
    print("sent putt with no round active -- check the response in transcript.log")
