# What does GSPro send unprompted, with no heartbeat or shot traffic, over 60s?

**Answer:** Nothing. `transcript.log`/`transcript.jsonl` are both empty (0 bytes) for
this session. Consistent with an earlier run (`logs/session_20260916_121504`, also
empty) — GSPro does not proactively send any traffic (no keepalive, no status ping, no
unsolicited Player info) while idle. The connection appears silent until the client
sends something.

TODO (fill in from what you observed on-screen): did GSPro's own Connect window still
show "connected"/no error the whole 60s?

