# Sending 3 shots in rapid succession, how many 200 responses come back, and is there any way to correlate a response to a specific shot?

**Only 2 of 3 sends got a response, and correlation is not possible.** Sent 3 putts
(ShotNumber 1789600606/607/608, speeds 5/6/7mph) within 1ms of each other (no delay
between `client.send()` calls, per `exp15_ack_correlation.py`). Got back exactly 2
responses: `501 "Bad format"` then `200 "Ball Data received"` -- no third response
arrived at all.

**No way to tell which response belongs to which shot** -- `GSProResponse` carries no
ShotNumber or other correlating field (confirms the question's premise). Given the 1ms
gap between sends, it's plausible GSPro's inbound parser doesn't reliably handle 3
JSON objects arriving back-to-back with no pacing at all -- one apparently failed
parsing (`501`) and the fate of the third send is simply unknown (no ack, no error).

**Real implication for the production app, not just a Phase-0 curiosity:** shots sent
with zero pacing between them can silently vanish or get misparsed by GSPro. The real
GolfSimVision app should not fire multiple shot payloads back-to-back with no gap --
worth a minimum inter-shot delay or waiting for the prior `200`/`202 GSPro ready` before
sending the next one.
