# WP-05-R2 evidence (WP05-F01 residual, M02 counterexamples, invulnerability reason)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-05-R2_Report_Claude_v1.0.md`. Review answered: `CORUM2_WP-05-R1_Review_GPT_v1.0.md`.
`Evidence/WP-05/` and `Evidence/WP-05-R1/` are kept unchanged.

## Source

- Both runs and the EditMode run use `5af26f0` with a clean tree. The manifests are valid: 325 rows match git (`validation/validate_results.ndjson`).
- The checker `Tools/wp05_checks.py` is part of `5af26f0` (no change after the runs). A copy is in `tools/`.
- Content is `wp05-1` (unchanged).

## Runs

| Run | Script | Video |
|---|---|---|
| `RUN-20261112-102835` (main) | WP-05-R1 main script plus one new segment at script 16.7: ↓, then two separate stick samples (↘ and →) queued with one shared device time (`padsame`), then X. The death section moved 2 s later. | `video/main-RUN-20261112-102835.mp4`: 1929 frames, recording from tick 226 |
| `RUN-20261112-103103` (nores) | No revive item, straight to game over (same script as R1) | `video/nores-RUN-20261112-103103.mp4`: 404 frames, from tick 197 |

## What the new segment shows (main)

| Tick | What happened |
|---|---|
| 1085 | Stick ↓: `MoveDown Press`, input id 95, `sampleGroup` 94. |
| 1086 | `test.padsame`: two samples ↘ then → queued with the same device time. |
| 1088 | `MoveRight Press` (id 97, `sampleGroup` 96, the ↘ sample) and `MoveDown Release` (id 99, `sampleGroup` 98, the → sample). Both have `requestTime` 97.90692959999433. |
| 1091 | History `Down@0.12 DownRight@0.06 Right@0.06`, `magic.command` fireball, cast, press outcome `magic:fireball`. |

Before R2, those two edges were merged because their times were equal, and the ↘ was lost. Now they are separate samples (different `sampleGroup`), so each is recorded.

The R1 segments still behave the same in this run:
- F01: direct ↓→ at 871 → history `Down Right`, `attack:GroundNormal`.
- F02: X→LB in the same frame → ring opens at 919, fireball at 920.
- F03: slide, scroll reserved at 1003 (`EXP-ITEM-07`), cast and consumed at 1020 (2 → 1).

## Checks (`checks/`)

| File | TC-02-02 | TC-02-10 | TC-06-19a | Self-test |
|---|---|---|---|---|
| `wp05_checks_main.json` | pass | pass | pass | 10/10 mutations rejected |
| `wp05_checks_nores.json` | not run (no magic in this script) | not run | pass | 3/3 applicable rejected, 7 N/A |
| `WP-05-R1_main_log_under_R2_checker.json` | pass | pass | pass | 10/10 |
| `WP-05-R1_nores_log_under_R2_checker.json` | not run | not run | pass | 3/3, 7 N/A |

New checker requirements (R2):

- **Scroll consumption is linked by `requestId`:** item.request (Accepted/Reserved) → magic.request → magic.cast → exactly one item.use with the same id, after the cast, same tick, same item. `countAfter` must equal the request's count − 1, and `cooldown` must be present and > 0.
- **Order:** every effect is after its cast, and every hit is after the effect (eventId and tick).
- **Death motion length:** the motion must last `round(motionTime / tick)` ticks. Here that is 1.2 s = 60 ticks, read from the log.
- **Miss outcome:** must be a known normal action: `attack:*`, `jump:*`, guard, buffered, reservedForLanding, extraJump, airDash, dive, slide, roll.

The five new mutations are the review's counterexamples. Each must make its section fail:

1. scroll `item.use.requestId` + 90000
2. `countAfter` and `cooldown` removed
3. fire hits moved before their cast
4. first death's motion end set to the death tick
5. first miss outcome set to `unknown_outcome`

## Other results

- **TC-02-10:** command fireball at 1091 vs scroll at 786: power 30, cast 0.2 s, 9 ticks to the effect, damage D 30.
- **TC-06-19a:**
  - Deaths at 1214 (revived at 1400) and 1551 (declined at 1700, then game over). Each motion is 60 ticks.
  - Selections Lobby, LoadSave and CheckpointRestart, all with `returnPerformed=false`.
  - No enemy attack starts while dead.
  - The revive invulnerability skip at 1465 is `PlayerInvincible:Revive(EXP-DEATH-03)`.
- **EditMode:** 275/275, which is 269 + 6 `Wp05R2Tests` (`EV-WP05R2-A01_EditMode_*`).

## Not covered

- **Invulnerability reason at the roll-window boundary** (revive + early roll): EditMode only. No hit landed on that boundary in Play.
- **TC-06-19b:** BLOCKED_SPEC (U-08).
- **TC-CF-01a–d:** BLOCKED_SPEC.

These are Claude self-check artifacts, not GPT verdicts.
