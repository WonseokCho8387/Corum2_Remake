# WP-05-R1 evidence (WP05-F01/F02/F03, M01, M02)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-05-R1_Report_Claude_v1.0.md`. Review answered: `CORUM2_WP-05_Review_GPT_v1.0.md`.
`Evidence/WP-05/` is kept unchanged.

## Source

- The run source is `44709f6`, with a clean tree, for both runs. The manifests are valid: 323 rows match git.
- The checker `Tools/wp05_checks.py` was revised after the runs, in `9fddda4`. That commit changes only the checker, which only reads logs. It now takes the normal action from the press outcome, so a slide or jump also counts. The version used is copied in `tools/`.
- Content is `wp05-1` (unchanged).

## Runs

| Run | Script | Video |
|---|---|---|
| `RUN-20261112-084821` (main) | WP-05 main script plus three new segments: F01 at script 12.2, F02 at 13.38, F03 at 15.0. The death section moved 4.5 s later. | `video/main-RUN-20261112-084821.mp4`: 1842 frames, recording from tick 243 |
| `RUN-20261112-085058` (nores) | No revive item, straight to game over | `video/nores-...mp4`: 432 frames, from tick 244 |

## What the new segments show (`checks/wp05_checks_main.json`)

| Fix | Tick | What happened |
|---|---|---|
| **F01** | 886 | The pad stick goes from (0,−1) to (1,0) in one change. The history is `Down@0.20 Right@0.10`, with no DownRight. A normal attack runs (`commandMiss`, press outcome `attack:GroundNormal`). |
| **F02** | 934–935 | After a real ↓↘→, X and LB come in the same frame. X is routed to the game (`Forward`), then the ring opens at tick 934. The game casts the fireball at 935 (`magic:fireball`). The ring closes at 963. EditMode checks that putting a tick between X and LB gives the same result, and that LB then X lets the ring own X (no cast, no attack). |
| **F03** | 1017–1035 | ↓+A starts a slide (press outcome `slide`; a `commandMiss` is logged because A has commands). The D-pad scroll is requested at 1018: `Reserved:Motion:Slide(EXP-ITEM-07)`, not consumed. After the slide, the cast starts at 1035, and only then is the scroll consumed (2 → 1). |

## Other results (all pass)

- **TC-02-02:** 5 commands, each with a cast, an effect, a hit for attack magic, and the press outcome `magic:<id>`. 4 misses, each with a normal outcome: attack, guard, attack, slide.
- **TC-02-10:**
  - Command fireball at 710: power 30, cast 0.2, 9 ticks to the effect, D 30.
  - Scroll at 801: the same values, plus `item.use` with the count and cooldown.
- **TC-06-19a:**
  - Deaths at 1129 (revive used) and 1466 (declined, then game over).
  - Selections Lobby, LoadSave, CheckpointRestart, all with `returnPerformed=false`.
  - No enemy attack starts while dead.
  - The revive invulnerability skip at 1380 is now `PlayerInvincible:Revive(EXP-DEATH-03)`.
- **nores run:** TC-06-19a passes. Selections CheckpointRestart, LoadSave.

## Checker self-test

The self-test passes on both runs. Each mutation must make its section fail:

- all fire effects and hits removed
- the first command fire effect removed
- all menu selections removed
- an enemy attack start one tick after a death
- the scroll `item.use` removed

`checks/WP-05_main_log_under_R1_checker.json` is the original WP-05 main log re-checked with this checker. It still passes, and its self-test passes.

## Other files

- `validation/validate_results.ndjson`
- `EV-WP05R1-A01_EditMode_TestResults.xml` and `_summary.txt`: EditMode 269/269 (259 + 10 `Wp05R1Tests`).

## Not covered

- **TC-06-19b:** BLOCKED_SPEC (U-08).
- **CF-01 cases** (TC-CF-01a–d): BLOCKED_SPEC.
- **Roll + scroll, LB→X, and X→tick→LB:** EditMode only.
- **"Cast refused at execution, nothing consumed":** a defensive branch. No player state reaches it now, because slide and roll wait.

Claude self-check artifacts, not GPT verdicts.
