# Does a full-shot payload (driver-range numbers) play correctly in GSPro? What response code comes back?

**Answer: yes.** Driver-range payload (Speed=150, HLA=0, VLA=12, TotalSpin=2500,
SpinAxis=0) played correctly on GSPro's course view (confirmed visually). Response
code: `200 "Ball Data received"`, arriving ~0.2s after the shot.

**Important operational finding, not really about this experiment's own question:**
the immediately preceding attempt (`logs/session_20260916_171013`) sent byte-for-byte
the same payload with the same harness code and got zero response. The only thing that
changed between the two was a GSPro-side Settings > Game > Reset GSPro Connect. See
`RUNBOOK.md`'s new Troubleshooting section -- this harness's connect/disconnect churn
(one cycle per `--scenario` run) appears to degrade GSPro's OpenAPI connection handling
over a session, and a GSPro-side reset is the fix, not tuning `connect_and_announce`'s
delay further.

