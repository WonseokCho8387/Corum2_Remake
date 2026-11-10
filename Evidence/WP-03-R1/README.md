# WP-03-R1 evidence (GPT WP-03 review: WP03-F01, F02, F03, M01)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-03-R1_Report_Claude_v1.0.md`.
- Code: `0ff03b6`, which is also the run source (clean tree; per-file source list in the manifest).
- The WP-03 evidence in `Evidence/WP-03` is unchanged.

## Contents

- `runs/RUN-20261110-131240.*` — Play run on `WP03_Ring` through the real Unity input path. Input comes from the Input System virtual gamepad, routed by `GameHost.RouteUi` → `Simulation.RouteFrame`. Files:
  - NDJSON log
  - data snapshot
  - manifest
  - source list
  - commands
  - `report.txt` (made with `Tools/wp03_log_report.py`)
- How "same frame" works in this run: commands with the same script time run in one `Update` and are queued to the device in the written order. The log shows the stick sample and the press with the same device time, and the arrival order decides between them.
- `video/main-RUN-20261110-131240.mp4` — 1081 frames, 1280x800, recorded at 50 fps in game time; JPEG stills every 50 frames.
- `checks/case_outcomes.json` — `tools/case_outcomes.py`: the events after each `F02-*`, `F01-*`, `M01-*` marker, with player state, grounded flag, height and HP.
- `checks/run_checks.json` — the WP-03 `run_checks.py` on this run:
  - recorder frames: 1082 expected, 1081 logged
  - inputs that reached the game while the ring was open: 0
- `validation/validate_results.ndjson` — manifest valid; source rows match `0ff03b6`.
- `EV-WP03R1-A01_*` — EditMode 179/179: 167 earlier tests plus 12 `RingR1Tests`. Two existing `RingTests` now release the stick before the D-pad.

## Cases in the run

| Marker | Inputs in one frame (unless noted) | Result in the log |
|---|---|---|
| F02-neutral-then-A | Potion selected earlier; stick → (0,0), then A | cursor → neutral, then A `NoSelection`; nothing used; ring stays open |
| F02-newitem-then-A | stick → right, then A | cursor → Herb, then the Herb request; Herb 2→1 |
| F02-A-then-neutral | Potion selected; A, then stick → (0,0) | Potion used (the selection at the press) |
| F02-A-then-newitem | Potion selected; A, then stick → right | Potion used; Herb unchanged |
| F02-stick-open-A-one-frame | stick (0.7,0.7), LB, A | ring opens with the cursor on Hi-Potion; Hi-Potion used |
| F02-inside-deadzone-then-A | stick (0.2,0.2) (reads 0.14,0.14), then A | neutral; nothing used |
| F01-dpad-before-release | Y on Potion, D-pad↑ (stick still up) | `Registering:StickNotReleased`; nothing assigned |
| (F01) | stick moved right (not a release), D-pad↑ | ignored again |
| F01-release / F01-dpad-after-release | stick (0,0), then tilted left, D-pad↑ (separate frames) | Potion assigned ↑; no item consumed; ring closes |
| F01-same-frame-Y-dpad-release | Y, D-pad↓, stick (0,0) | D-pad ignored (came before the release); a later D-pad↓ assigns Herb |
| F01-same-frame-Y-release-dpad | Y, stick (0,0), D-pad→ | Fire Scroll assigned → |
| M01-air-attack-ring (TC-02-11) | jump, X in the air, LB, A on Potion | ring opens with the player in `AirAttack`; use `Reserved:Motion:AirAttack`, runs when the attack ends |
| M01-air-hit-ring (TC-INT-02) | jump just before the hitter's hit; LB, A on Herb | hit at tick 767 while airborne (y=1.31); ring opens in airborne HitStun; `Reserved:HitReaction`; runs at tick 789 after HitStun, still airborne; HP 90→95 |

Claude self-check artifacts, not GPT verdicts.
