# With LaunchMonitorIsReady=true sent every 5s for 30s, does GSPro's readiness indicator change? (Every heartbeat gspro-r10.exe sends carries false -- this settles whether the indicator is simply never fed.)

**Answer (headline finding): No, the indicator did not change.** Sent `LaunchMonitorIsReady: true`
in 6 heartbeats over ~30s, verified in transcript.log. Screenshots are identical to
exp02's (readiness=false) — the indicator stayed off in both. This means either (a) the
flag genuinely doesn't drive GSPro's UI at all, or (b) it needs some other condition not
present in this test (e.g. an active round, `ContainsBallData: true`, or a real launch
monitor's `DeviceID`/profile). Doesn't fully resolve the question yet — exp04
(readiness toggle) may help distinguish "never works" from "needs a live round."

