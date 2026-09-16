"""Experiment 11 -- HLA sign. Trivially easy to ship backwards."""

from . import connect_and_wait, make_codec

QUESTION = "Which way does the ball go for HLA=+2 vs HLA=-2? Confirms the sign convention."


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)

    client.send(codec.build_putt(speed=5, hla=2, vla=0, spin=0))
    print("sent putt HLA=+2")
    input("Observe which way the ball goes, then press Enter to send HLA=-2... ")

    client.send(codec.build_putt(speed=5, hla=-2, vla=0, spin=0))
    print("sent putt HLA=-2")
    input("Observe which way the ball goes, then press Enter to finish... ")
