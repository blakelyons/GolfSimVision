# What Player object does each club produce in GSPro's 201 response? Does 'Club' reliably read 'PT' when the putter is selected?

**Answer: Yes, confirmed.** Cycled the full bag (I9, DR, W3, I2, I4, I5, I6, I7, I8, I9,
PW, GW, SW, LW, PT) with a properly-registered connection (connect_and_announce fix).
`Club` read the expected 2-letter code for every selection, `"PT"` for the putter at
16:12:46-47.

**Observations for docs/GSPRO_OPENCONNECT.md:**
- Every club change fires **two** `201`s, ~0.2-0.25s apart, not one -- consistent across
  every single change in this transcript. Dedupe on this when writing the parser/consumer.
- `DistanceToTarget` moves as clubs change (136.4 -> 313.7 -> 370.3 -> 392.0 while
  cycling through driver/woods, then holds steady through the irons/wedges/putter) --
  looks like an estimated carry/total distance for the selected club from the current
  lie, not a fixed hole distance. Worth confirming properly in exp06.
- **New, previously undocumented response code:** `{"Code":202,"Message":"GSPro ready","Player":null}`
  appeared once, mid-sequence (16:12:23.971, right after selecting driver). Not in the
  harness spec's known code list (200/201/>=500). Logged verbatim per the spec's "other ->
  log, don't discard" rule -- worth investigating what triggers it specifically.

