# With LaunchMonitorIsReady=false sent every 5s for 30s, what does GSPro's readiness indicator show?

**Answer:** Stayed off / not lit, the whole 30s. 6 heartbeats sent (every 5s), all with
`LaunchMonitorIsReady: false`, `IsHeartBeat: true`. No inbound response logged (expected
— heartbeats aren't acked, per exp01/exp17). Screenshot: [add filename once saved into
this folder].

