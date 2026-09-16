# With no round active (at GSPro's menu), is a shot accepted, ignored, or does it error?

**Answer (confirmed, connection-registration confound removed): ignored, silently.**
Connection registered via one heartbeat at 15:59:26 (per the connect_and_announce fix),
then the putt sent 5s later. Zero response of any kind came back -- no 200, no error
code -- even with the 2s shutdown grace period. Same result as the original
(unconfirmed) exp17 run, now with a properly-recognized connection confirmed via the
heartbeat, so this is a reliable answer: GSPro accepts the shot at the TCP/JSON level
(no error, no dropped connection) but does nothing with it and sends nothing back when
no round is active.

