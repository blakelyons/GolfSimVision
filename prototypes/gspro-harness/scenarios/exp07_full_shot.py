"""Experiment 7 -- full shot."""

from . import connect_and_wait, make_codec

QUESTION = "Does a full-shot payload (driver-range numbers) play correctly in GSPro? What response code comes back?"


def run(client) -> None:
    connect_and_wait(client)
    codec = make_codec(client)
    client.send(codec.build_shot(speed=150, hla=0, vla=12, total_spin=2500, spin_axis=0))
    print("sent full shot (speed=150 hla=0 vla=12 totalspin=2500 spinaxis=0)")
    print("check GSPro and transcript.log for the response")
