# WP-04 evidence (P0-08 general monster AI, boss AI and phases)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-04_Report_Claude_v1.0.md`.

Source:
- The run source is `495b7e1` with a clean tree; the manifest includes the per-file source list.
- AI and boss code commits: `f4c84a9` and `f8cd2ef`.
- Later commits up to `495b7e1` changed only the Editor content builder and the WP-04 data assets.

Scene: `Assets/Corum2/Scenes/WP04_AI.unity`, map C04, 260 m long.
- Each zone holds one kind of enemy. Zones are 30 m or more apart; the tracking range is 8 m (boss 12 m).
- So only the enemies in the player's zone react.
- All enemy and boss numbers are test data (TEST-AI-01).
- Undecided parts are experimental (EXP-AI-01..08, EXP-BOSS-01..04).

## Contents

- `runs/RUN-20261110-142730.*` — the Play run, driven by the Input System virtual gamepad.
  - NDJSON log, data snapshot, manifest, source list and commands.
  - `report.txt` from `Tools/wp04_log_report.py`: AI decisions, attack starts, boss patterns and phases, projectiles, hits, guard and parry, death.
  - `hp=` lines are logged test setup (`TestSetPlayerHp`); they keep the player alive through each zone.
  - `bossevent=boss:calm` raises the R-08-10 event condition.
- `video/main-RUN-20261110-142730.mp4` — 3449 frames, 1280x800, 50 fps game time, with JPEG stills every 50 frames.
- `checks/run_checks.json` — per-TC facts extracted by `tools/run_checks.py` (listed below).
- `validation/validate_results.ndjson` — the manifest is valid and its source rows match `495b7e1`.
- `EV-WP04-A01_*` — EditMode 198/198: 179 earlier tests plus 19 AI, boss and validator tests.

## What the run shows (`checks/run_checks.json`)

| TC | Result in the log |
|---|---|
| TC-08-01 | 10 chasers reach range together; all 10 start `AttackStartup` in tick 352. No waiting for a slot. |
| TC-08-02 | First chance in range: `InRange:FirstChance`. In range during the re-attack interval: `ai.blocked Cooldown`, then an attack when it ends. Hit reactions (Stagger) delay the next decision. |
| TC-08-03 | Chaser: Wait (beyond track range) → Approach → Attack. Hit twice (Stagger) → Approach again → Attack. |
| TC-08-04 | `fl` (reapproach): Flee after the hit reaction, flee ends → None → approaches and attacks again. `fh` (no reapproach): Flee → **Hold**. |
| TC-08-05 | Jumper at distance 4, inside the [3, 5] window: `ai.jump`, air attack (`AirAttack(R-08-05)`), lands, then a ground attack. |
| TC-08-06 | Ranged enemy out of range: Wait in place. At distance 4.5: shot → hits the player → retreat. Second shot: front guard blocks it (damage 0, `not for projectiles (EXP-AI-05)`). |
| TC-08-07 | After the player dies (tick 3394): 0 new attack starts; `PlayerDead:NoNewAttack` decisions. Per-enemy intervals: `ai.blocked` with each enemy's own cooldown. |
| TC-08-08 | Boss patterns, each with its candidate list: `slam` (near, player grounded), `shot` (7.1 m), `antiair` (player airborne, 0.8 m), `crush` (phase 2). |
| TC-08-09/10/11 | `p1`→`p2` HpBelow:0.6 (armor, stagger resistance 0.5, speed 1.5, transition 1 s, presentation ids `warden.roar` / `vfx.armorGlow` / `cam.shakeSmall`); `p2`→`p3` PatternUsed:crush x2; `p3`→`p1` Event:calm. Then, at 35 % HP, `p1`→`p2` again on the next check (one transition per check, EXP-BOSS-04). |
| TC-08-12 | Boss is parried (tick 1947). Physical hits stagger it in p1. A hit during PhaseTransition reacts normally (EXP-BOSS-03). In p2 a hit on the armored crush deals damage only (`SuperArmor:DamageOnly`). |

## Not shown in this Play run

- **Magic hits on a boss** (R-08-12): EditMode only, because there is no in-game magic.
- **Launch immunity:** EditMode only.
- **Dodge with a boss:** not run in this WP. WP-02 covers the general rule.
- **One-phase vs two-phase bosses side by side** (TC-08-09): EditMode only. The Play run has one 3-phase boss.
- **Boss phase jump:** seen in a trial run, but not in this run's p3 window.

Claude self-check artifacts, not GPT verdicts.
