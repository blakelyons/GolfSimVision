"""Experiment 6 -- DistanceToTarget."""

from . import connect_and_announce

QUESTION = (
    "Does DistanceToTarget update per shot as a hole is played tee to green? "
    "What units, and what happens once on the green?"
)


def run(client) -> None:
    # This mode only works with a real launch monitor physically hitting shots --
    # --scenario can't take shot/putt commands mid-run, only this one input(). With no
    # physical monitor connected, don't use --scenario for this experiment at all: run
    # the plain REPL (`python harness.py`) instead and drive `shot`/`putt` yourself. See
    # RUNBOOK.md's Phase 2 entry for exp06.
    connect_and_announce(client)
    print("Play a hole normally in GSPro now (real launch monitor swings), tee to green.")
    input("Press Enter once you've holed out... ")
