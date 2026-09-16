# After one heartbeat registers the connection, what does GSPro send unprompted over the next 60s with no further heartbeat or shot traffic?

**Answer:** Nothing. One heartbeat sent at connect (confirmed in transcript.log,
registering with GSPro per the connect_and_announce fix), then genuinely no traffic for
the full 60s. Unlike the original exp01 run (`logs/session_20260916_124128`), this
connection is known to have been properly recognized by GSPro (per the exp05/REPL
discovery), so this result is now reliable: GSPro does not send anything unprompted once
connected, as long as heartbeats stop.

