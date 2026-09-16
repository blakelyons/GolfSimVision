# With no round active (at GSPro's menu), is a shot accepted, ignored, or does it error?

**Answer:** Ignored, silently. GSPro sent back no response at all (no 200, no error
code, nothing) — `transcript.log` has only the outbound putt, even with a 2s grace
period after send before the harness disconnected. Confirmed with the original run too
(`logs/session_20260916_124600`, also no response, though that run predates the grace-
period fix so is less conclusive on its own).

