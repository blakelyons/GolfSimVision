"""Experiment 16 -- reconnect."""

import time

from . import connect_and_wait, make_codec

QUESTION = (
    "After a dropped connection (RST, not a clean close) and reconnect, does "
    "GSPro recover? What ShotNumber does it expect next?"
)


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)

    client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
    print("sent a putt before the drop")
    time.sleep(1)

    client.drop()
    print("dropped the raw socket (RST) -- reconnect/backoff loop should take over")
    connect_and_wait(client, timeout=30.0)
    print("reconnected")

    client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
    print("sent a putt after reconnecting -- check transcript.log and GSPro")
