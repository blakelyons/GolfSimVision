# Alternating LaunchMonitorIsReady every 10s for 2 minutes, does the indicator track live, or latch?

**Answer:** Indicator still did not respond to the flag (same as exp02/03/exp04-no-round).
This run was with a round active. Confirms the readiness indicator is not driven by
`LaunchMonitorIsReady` at all under any condition tested so far.

**Anomaly during this run (likely unrelated to our traffic):** the readiness indicator
turned green when the round started (before this scenario sent anything), a .NET
error popup appeared, and putt shots fired one after another until the GSPro Connect
window was closed. Transcript shows this was **not** caused by our harness: zero `200`
(shot-received) responses appear anywhere in this log, only automatic `201` Player Info
messages (club I6->I7, DistanceToTarget 118.9). The Garmin Golf app was concurrently
attempting an E6 Connect connection (reported "connection failed" on the app side) —
E6 Connect is a separate GSPro subsystem from this harness's TCP 921 OpenConnect
socket, and a botched E6 handshake attempt is the more likely cause of the .NET error
and stray shots. **Lesson for future sessions: don't run the Garmin app or any other
GSPro client alongside these experiments** — added to RUNBOOK.md's prerequisites.

