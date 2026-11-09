# WP-02-R1 evidence (GPT review WP02-F01..F03, M01)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-02-R1_Report_Claude_v1.0.md`. Run source: `7f79776` (clean tree, recorded in each manifest). Scene: `Assets/Corum2/Scenes/WP02_Combat.unity`, data `wp02-r1`.

- `runs/` — `RUN-20261110-075442` (r1: F01 multi Dodge, F02 piercing dash, F03 cap) and `RUN-20261110-075540` (main: WP-02 main script re-run on the new source). Per run: NDJSON log (L), data snapshot (D), manifest (M, now self-filled: spec/appendix sha256, revision, dirty state, tester, commands file, capture, devices), `*.commands.txt` (exact input script), `*.report.txt` (made with `Tools/wp02_log_report.py`).
- `video/` — MP4 1280x800 at 50 fps game time (Time.captureFramerate=50, one frame = one tick; not device latency/FPS evidence) and one JPEG still per second. Frame n ~ tick (recording start marker + 1 + n); match by the HUD tick.
- `EV-WP02R1-A01_*` — EditMode 132/132 (104 earlier + 28 R1 regressions), source 7f79776.
- `supplement_WP-02/` — M01 supplement packets for the earlier runs RUN-20261110-020410 / -020604 (originals in `Evidence/WP-02` unchanged).
- `evidence_steps_r1_main.sh`, `evidence_helpers.sh` — the driving scripts (virtual keyboard at Input System device level).

Damage values come from the test resolver TEST-DMG-01. Claude self-check artifacts, not GPT verdicts.
