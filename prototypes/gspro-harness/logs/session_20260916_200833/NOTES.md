# After a dropped connection (RST, not a clean close) and reconnect, does GSPro recover? What ShotNumber does it expect next?

**Yes, GSPro recovers cleanly.** On the green with `PT` selected: sent a putt before the
drop (`200 "Ball Data received"`), then `client.drop()` forced a TCP RST. Console showed
two `RECONNECTING -> CONNECTING -> CONNECTED` cycles before settling (the first
reconnect attempt didn't stick, the second did) -- all within `connect_and_announce`'s
30s timeout for this scenario. After reconnecting, sent a fresh heartbeat and got the
full normal response set back (`201` Player Info x2, `202 GSPro ready`), then a putt
after reconnecting also got `200 "Ball Data received"`.

**What ShotNumber does it expect next?** No special enforcement observed -- the
post-reconnect putt used the harness's normal next-incrementing ShotNumber (no gap or
reset across the drop) and was accepted without complaint. Combined with exp13's finding
that GSPro doesn't validate ShotNumber continuity at all (accepts non-monotonic/zero/
negative values, only rejects int32 overflow), there's no evidence GSPro tracks or
requires any particular "expected next" value across a reconnect -- it just accepts
whatever valid-range ShotNumber the next request carries.
