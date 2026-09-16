"""Experiment 10 -- TotalSpin sweep (the rolling-spin question, Harness Specs §4)."""

from protocol import rolling_spin_rpm

from . import connect_and_wait, make_codec

QUESTION = (
    "Does a realistic rolling-spin estimate (200 x mph) behave differently -- "
    "and better -- than TotalSpin=0 for a putt?"
)


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    speed = 5.0

    client.send(codec.build_putt(speed=speed, hla=0, vla=0, spin=0))
    print("sent putt with TotalSpin=0")
    input("Observe on screen, then press Enter to send the rolling-spin estimate... ")

    client.send(codec.build_putt(speed=speed, hla=0, vla=0))
    print(f"sent putt with TotalSpin={rolling_spin_rpm(speed)} (rolling estimate)")
    input("Observe on screen, then press Enter to finish... ")
