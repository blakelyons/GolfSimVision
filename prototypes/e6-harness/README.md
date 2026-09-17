# E6 Connect Ingress Spike (P0-4)

Verifies V1's E6 Connect handshake (`SimLinkPutting/backend/monitors/r10.py`, ported here
as `protocol.py`'s `E6Server`) still works against the **current** Garmin Golf app —
V1's code was written against an older version and has never been re-tested.

**Ships nothing.** Phase 0 prototype (ADR-0006). Stdlib only, nothing to `pip install`.

## Prerequisites

- [ ] Your R10 is paired to your phone via Bluetooth in the Garmin Golf app.
- [ ] Your phone is on the **same WiFi network** as this PC. This matters more than it
      sounds — this PC currently has **two different LAN IPs on two different
      subnets** (check with `Get-NetIPAddress`, or ask Claude to re-check — they can
      change between sessions). Using the wrong one was V1's single worst field
      failure (ADR-0002). Confirm which network your phone is actually joined to before
      picking an IP.
- [ ] Windows Firewall allows inbound TCP on port 2483 (add a rule if this is the first
      run — the app will otherwise connect-then-silently-fail with no useful error on
      either side).
- [ ] `gspro-r10.exe` is **not** running (this spike doesn't need it, and per ADR-0001,
      GolfSimVision owns 921 directly — no conflict either way since this uses 2483, but
      keep the setup clean).

## Run

```
python harness.py [--port 2483]
```

Then, on your phone: open the Garmin Golf app, and point it at a simulator connection
using this PC's LAN IP (the one matching your phone's network) and the port above.
Watch the console and `logs/session_<ts>/transcript.log`.

## What to actually check (P0-4's measurements)

- **Handshake success** — does `Handshake` -> `Challenge` -> `Authentication` complete,
  or does the current app send something different / in a different order than V1
  assumed?
- **Shot payload shape** — hit some shots (or use the app's test/practice mode if it has
  one). Does `SendShot`'s `BallData` still have the fields V1's code expects, with the
  same names/casing? This is the part most likely to have drifted.
- **Reconnect behaviour** — close and reopen the Garmin Golf app's connection. Does a
  second `Handshake` come in cleanly, or does the app expect the old session torn down
  first?
- **Sleep / WiFi drop behaviour** — lock the phone or walk out of WiFi range mid-session.
  Does the socket close cleanly (so `on_connection_change(False)` fires), or does it hang
  until some OS-level timeout?

Fill in what you observe in each `logs/session_<ts>/NOTES.md` by hand (this harness
doesn't pre-seed one — there's no fixed experiment list here yet, just "does the real
thing work").

## Testing

`python -m unittest discover` — stdlib only, covers `extract_json`'s framing (ported from
V1, same edge cases as `gspro-harness/test_protocol.py`) and the outbound message
builders. No phone or R10 needed.
