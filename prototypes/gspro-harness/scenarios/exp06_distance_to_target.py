"""Experiment 6 -- DistanceToTarget."""

from . import connect_and_announce

QUESTION = (
    "Does DistanceToTarget update per shot as a hole is played tee to green? "
    "What units, and what happens once on the green?"
)


def run(client) -> None:
    # See connect_and_announce's docstring -- a bare connect isn't enough for GSPro
    # to recognize the client.
    connect_and_announce(client)
    print("Play a hole normally in GSPro now, tee to green.")
    input("Press Enter once you've holed out... ")
