"""Experiment 13 -- ShotNumber abuse."""

import time

from . import connect_and_wait, make_codec

QUESTION = (
    "Does GSPro reject or accept non-monotonic/zero/negative/overflowing "
    "ShotNumber values? Confirm 501 on int32 overflow; find the real rules."
)

# (label, ShotNumber to force before sending)
CASES = [
    ("non-monotonic (small value after a large epoch-seconds counter)", 1),
    ("zero", 0),
    ("negative", -1),
    ("int32 overflow", 2**31),
    ("int32 overflow + 1", 2**31 + 1),
]


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    for label, value in CASES:
        client.override_shot_number(value)
        client.send(codec.build_putt(speed=5, hla=0, vla=0, spin=0))
        print(f"sent putt with ShotNumber={value} ({label}) -- check response in transcript.log")
        time.sleep(1)
