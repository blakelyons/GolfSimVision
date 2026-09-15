# GolfSimVision — Claude Co-Work Discussion Prompt

Please read these project files completely before proposing implementation work:

1. `GolfSimVision-Project-Kickoff.md` — authoritative requirements and project direction.
2. `GolfSimVision-Prototype-Appendix.md` — exploratory ideas/code only; not verified architecture.
3. If I provide GolfSimVision V1, use it primarily as a visual/UI reference. Preserve its desktop look and feel, but treat the new application as a clean-slate engineering effort.

Also follow the OpenFlight research requirements in the kickoff document. OpenFlight may provide useful camera/YOLO, ball/club detection, simulator connector, and GSPro OpenConnect lessons, but it should inform—not dictate—the architecture.

For now, do NOT start building the complete application.

First produce a Project Readiness Review. Explain GolfSimVision back to me, map the major components/data flow, distinguish REQUIREMENTS vs VERIFIED FACTS vs ASSUMPTIONS vs EXPERIMENTS vs DECISIONS, identify the five most important technical questions, review the iPhone→Windows camera concept and Garmin R10→GolfSimVision→GSPro concept, identify what we should learn from OpenFlight, and explain how V1's visual design can be preserved without inheriting V1 engineering.

Then recommend the Phase 0 validation experiments and what repository/docs structure should eventually be packaged for Claude Code.

Challenge questionable assumptions and explain tradeoffs on important architectural decisions.

At the end, tell me what we should investigate or decide first, then STOP so we can discuss the architecture together before you package anything for Claude Code.
