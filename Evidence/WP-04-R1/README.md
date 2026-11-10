# WP-04-R1 evidence (fixes for WP04-F01/F02/F03, R-08-11 guard/evade for WP04-M01)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-04-R1_Report_Claude_v1.0.md`.
Review being answered: `CORUM2_WP-04_Review_GPT_v1.0.md`.
The WP-04 evidence (`Evidence/WP-04/`) is kept unchanged.

Source:
- Run source: `406e67d`, clean tree. The manifest has the per-file source list; validation matches all 284 rows to `406e67d`.
- `7266fa6` holds the F01/F02/F03/M01 code, content `wp04-2` and the tests.
- `406e67d` resets the guard/evade counts on each phase entry. It also adds one test.

Scene: `WP04_AI`, map C04. The WP-04 zones are unchanged, with two additions:
- **Z5Quick / Z5QuickB**: `jumperQuick` (`jq`, x 157). Its jump cooldown is 0.3 s and its re-attack interval is 3 s. This exercises F02.
- **Z8Sentinel**: boss `sentinel` (x 251). Its patterns are ranged only, so a player standing next to it gets guard or evade, not attacks.
  - Phase s1: guard 0.5, evade 0.
  - Phase s2: guard 0, evade 1, phase jump every 1.5 s.
  - Event `shift` moves it from s1 to s2.

The `warden` boss has guard and evade frequency 0 in every phase, so its behaviour matches WP-04.

## Contents

- `runs/RUN-20261110-173813.*`: the Play run (Input System virtual gamepad). It includes:
  - the NDJSON log, data snapshot, manifest, source list and commands;
  - `report.txt`, made by `Tools/wp04_log_report.py`.
- `video/main-RUN-20261110-173813.mp4`: 4382 frames, 1280x800, 50 fps game time.
  - Recording runs from tick 208 to tick 4590, so frame = tick − 208.
  - PNG stills are taken every 50 frames.
- `checks/run_checks.json`: the WP-04 per-TC checks (`tools/run_checks.py`, unchanged), re-run on this log.
- `checks/r1_checks.json`: per-finding facts (`tools/r1_checks.py`), listed in the table below.
- `validation/validate_results.ndjson`: the manifest is valid; 284 rows match git at `406e67d`.
- `EV-WP04R1-A01_EditMode_TestResults.xml` and `_summary.txt`: EditMode 217/217 (198 earlier + 19 `AiR1Tests`), run on the `406e67d` source.
- `evidence_steps_wp04r1.sh` and `evidence_helpers.sh`: the run script.

## What the Play run shows (`checks/r1_checks.json`)

| Item | Ticks (frame = tick − 208) | Log |
|---|---|---|
| F01 | 2550, 2724 | `ai.propulsionCut` boss: walking stopped on a Stagger hit. |
| F01 | 3971 → 3994 | Sentinel phase jump (`BossPhaseJump(R-08-11)`). An air hit cuts the jump: `ai.propulsionCut Jump`, `airborne=true`, `moveVelBefore=-4`. |
| F01 | 4054, 4114 | The evade step is cut by a hit (no invulnerability, EXP-BOSS-07). |
| F02 | 4199 | `jq`: first air attack. |
| F02 | 4241 → 4253 | `jq` jumps again 1.2 s later; the air attack is blocked: `Cooldown:ReattackInterval:Air`, 2.38 s left. |
| F02 | 4270 | `jq` lands with no second air attack. |
| F03 | 2796 | Warden p1→p2 (HpBelow 0.6, transition 1 s). |
| F03 | 2819 / 2835 / 2851 | Physical hits during the transition. Each Stagger returns to `PhaseTransition` (`StaggerEnd->TransitionResumes`). |
| F03 | 2855 | `boss.transitionEnd TransitionDoneDuringReaction` (1 s plus the three hits' hitstop). |
| F03 | 2865 | First new attack, after the end. WP-04 attacked 0.74 s into the transition. |
| F03 | other transitions | All end with `boss.transitionEnd` before the next attack: p2→p3, p3→p1→p2, p2→p3 and sentinel s1→s2. |
| M01 | 3547 / 3587 / 3627 / 3667 / 3747 | Sentinel s1, frequency 0.5: `boss.defense` None, Guard, None, Guard, None. |
| M01 | 3589, 3669 | Guarded hits: `BossGuard:GuardRule`, D 10 → 1, no stagger. |
| M01 | 3549, 3629, 3749 | Unguarded hits: normal Stagger. |
| M01 | 3872 → 3887 | Sentinel s2, evade 1: back step from x 245.73 to x 247.97; the player's attack misses. |
| M01 | 4052, 4112 | Evade at distance 0.16 and 0.66: still hit (no invulnerability). |
| M01 | — | Guard/evade counts restart in s2 (`guardAcc` 0). The warden logs 0 `boss.defense` events. |
| C01 | 369 | Ten chasers start together in this run. WP-04's run was tick 367, not the 352 the WP-04 report gave. |
| C02 | 2644 | A boss Dodge occurs in this run's TC-08-10 section too. WP-04's run had one at tick 2642. |
| TC-08-07 | 4346 | Player death; 0 attack starts after it. |

In the s1 section there were 6 X taps and 5 `boss.defense` events. The 5th tap (tick 3707) was rejected by the player's combo limit (`ChainMax:Ground4`), so no attack started.

In s2, one tap (tick 3932) was rejected because the player was in hit stun.

## Not shown in this Play run

- **Magic hits on a boss or jumper:** EditMode only (no in-game magic). Covered by the F01, F03 and M01 tests.
- **Parry of a jumper air attack and the reaction-state table:** EditMode only.
- **Death during a transition:** EditMode only (`F03_DeathDuringTransition_CancelsIt`).
- **Transition presentation:** animation, VFX and camera are still ids that are only logged; no assets are played (R-08-11 presentation part not done).

Claude self-check artifacts, not GPT verdicts.
