"""Experiment 5 -- club change."""

from . import connect_and_wait

QUESTION = (
    "What Player object does each club produce in GSPro's 201 response? "
    "Does 'Club' reliably read 'PT' when the putter is selected?"
)


def run(client) -> None:
    connect_and_wait(client)
    print("Change clubs in GSPro through the full bag now.")
    print("Every change should produce a 201 -- transcript.log captures each one.")
    input("Press Enter once you've cycled the whole bag... ")
