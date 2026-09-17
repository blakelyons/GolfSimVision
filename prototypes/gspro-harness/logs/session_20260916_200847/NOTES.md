# With no heartbeat or shot traffic for 5 minutes, does GSPro time us out or close the connection?

**No -- GSPro neither times out nor closes the connection after 5 minutes of silence.**
Sent one heartbeat (got the normal `201` Player Info x2 + `202 GSPro ready` response),
then sent nothing for 5 minutes. `client.state` stayed `CONNECTED` for the entire wait
(confirmed at every 30s console checkpoint from 300s down to 0s) -- never transitioned
to `RECONNECTING`, and no inbound traffic of any kind arrived from GSPro during the
silence either. GSPro simply waits indefinitely (at least up to 5 minutes) with no
heartbeat-timeout behavior of its own.
