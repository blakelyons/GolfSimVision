"""Experiment 16 -- reconnect."""

import time

from . import connect_and_announce, make_codec

QUESTION = (
    "After a dropped connection (RST, not a clean close) and reconnect, does "
    "GSPro recover? What ShotNumber does it expect next?"
)


def run(client) -> None:
    connect_and_announce(client)
    codec = make_codec(client)

    client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
    print("sent a putt before the drop")
    time.sleep(1)

    client.drop()
    print("dropped the raw socket (RST) -- reconnect/backoff loop should take over")
    # Announce again after reconnecting too -- otherwise we can't tell whether GSPro
    # failing to respond to the post-reconnect putt means "GSPro didn't recover" or
    # just "we're unregistered again," the same registration issue found live in exp05.
    connect_and_announce(client, timeout=30.0)
    print("reconnected")

    client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
    print("sent a putt after reconnecting -- check transcript.log and GSPro")
