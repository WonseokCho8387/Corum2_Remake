# WP-02 evidence (attack, state, hit, guard)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-02_Report_Claude_v1.0.md`. Run source: `4cf88ba` (clean tree). Scene: `Assets/Corum2/Scenes/WP02_Combat.unity`.

- `runs/` — main run `RUN-20261110-020410` and parry run `RUN-20261110-020604`: NDJSON log (L), data snapshot incl. combat/enemy assets (D), manifest (M), per-TC summaries (`*.report.txt`, made with `Tools/wp02_log_report.py`).
- `video/` — MP4 1280x800 at 50 fps (one frame = one 50 Hz tick of game time) and one JPEG still per second. HUD shows test case, state, HP, combo, chain step and enemy states; red box = player hitbox, orange = enemy hitbox.
- `EV-WP02-A01_*` — EditMode results 104/104.
- `evidence_steps_*.sh`, `evidence_helpers.sh` — exact timed input scripts (virtual keyboard at Input System device level).

Damage values come from the test resolver TEST-DMG-01, not a game formula. Claude self-check artifacts, not GPT verdicts.
