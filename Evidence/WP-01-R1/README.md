# WP-01-R1 evidence

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-01-R1_Report_Claude_v1.0.md`. Run source: `1559584` (clean tree).

- `runs/` — five runs (main, locked, multitick, inspector42, inspector35): NDJSON log (L), data snapshot (D), manifest (M); `runs.txt` maps names to run ids.
- `video/` — MP4 (1280x800, 50 fps, one frame = one 50 Hz tick of game time) for main and locked runs, plus one JPEG still per second.
- `EV-WP01R1-A01_*` — EditMode results 47/47.
- `evidence_steps.sh`, `evidence_helpers.sh` — exact timed input scripts (virtual keyboard/gamepad at Input System device level) and Editor driving.

Input is device-level synthetic (virtual devices), not physical hardware. Claude self-check artifacts, not GPT verdicts.
