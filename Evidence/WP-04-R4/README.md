# WP-04-R4 evidence: WP04-F04 / F05 / M04 / M05 (boss transition presentation)

- **Report:** `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-04-R4_Report_Claude_v1.0.md`
- **Review answered:** `CORUM2_WP-04-R3_Review_GPT_v1.0.md`
- **Earlier evidence:** `Evidence/WP-04` and `Evidence/WP-04-R1` to `-R3` are unchanged.

## Source

- **Run source:** `292284c` for all three runs, each with a clean tree. Every manifest is valid, and its 300 rows match git at `292284c`.
- **Commits:**
  - `d486088` holds the F04/F05/M04/M05 code, tests and checker.
  - `292284c` keys the VFX pause to the boss's hitstop-skipped step.
- **Checker update after the runs:** `9ad4bb8` changed only `Tools/wp04_presentation_checks.py`. It applies the "no VFX movement" rule to single-tick frames only, and stops counting self-test cases that do not apply to a log as failures.
  - The checks in `checks/` were produced with that version, which is also copied in `tools/`.
  - The checker only reads logs.
- **Content:** `wp04-3` (unchanged since R3).

## Runs

| Run | Render | Video | Purpose |
|---|---|---|---|
| `RUN-20261110-193306` (main) | Recorder lock `captureFramerate = 50`: 1 tick per rendered frame | `video/main-RUN-20261110-193306.mp4`, 4393 frames, frame = tick − 185, PNG stills every 50 frames | Presentation, VFX clock and hitstop |
| `RUN-20261110-193707` (free) | No recorder, no frame cap. Mostly 1 tick per frame, 2 ticks occasionally | none | Real host path |
| `RUN-20261110-193935` (slow) | No recorder, `Application.targetFrameRate = 17`. About 3 ticks per frame (gaps: 3 ticks ×68, 2 ×10, 4 ×5) | none | F04 on the real host path |

- The same input script drives all three runs (`evidence_steps_wp04r4_main.sh`, `evidence_steps_wp04r4_freerun.sh slow 17`).
- Without the frame lock, the free and slow runs play in real time. Their tick numbers differ from the main run.

## Checks

- `checks/presentation_checks_{main,free,slow}.json`: `Tools/wp04_presentation_checks.py --selftest`.
  - Each judged transition is matched by actor, judged start tick and phase.
  - It must have exactly one shown start or one skipped record, and exactly one end or cancel with the same serial.
  - `judgedEndTick` must equal the transition end, and `observedTick` must be at or after the judged end.
  - No cue may be missing, and there may be no orphan, dropped or duplicated record.
  - VFX clock rules:
    - On every frame, `vfxTime` grows by the same amount as `elapsedGame`.
    - On a single-tick frame where the boss's step was paused by hitstop (`bossPausedThisTick`), `vfxTime` does not move.
    - When the transition ends, the VFX time equals the transition time.
  - The self-test must reject each of these 8 mutations:
    - a duplicated end;
    - all ends removed;
    - `judgedEndTick` shifted by 1 tick;
    - a skipped record added;
    - a transition that was only skipped;
    - a missing cue;
    - an orphan start;
    - VFX advancing during hitstop (only on logs that have such frames).
- `checks/transition_checks_main.json` and `checks/run_checks_main.json`: the earlier checks on the main run.
- `validation/validate_results.ndjson`: all three manifests.
- `EV-WP04R4-A01_EditMode_TestResults.xml` and `_summary.txt`: EditMode 240/240, which is 230 earlier plus 10 `AiR4PresentationTests` cases.

## Results

All three runs have `all_pass` true and `selftest_all_pass` true.

| Run | Transition (judged start→end) | Shown tick (delay) | End observed tick |
|---|---|---|---|
| main | p1→p2 2774→2833 | 2774 (0) | 2833 |
| main | p2→p3 2958→3008 | 2958 (0) | 3008 |
| main | p3→p1 3199→3200 | 3199 (0) | 3200 |
| main | p1→p2 3200→3250 | 3200 (0) | 3250 |
| main | p2→p3 3349→3399 | 3349 (0) | 3399 |
| main | s1→s2 3774→3824 | 3774 (0) | 3824 |
| slow | p1→p2 2740→2799 | 2743 (3) | 2801 |
| slow | p2→p3 2963→3013 | 2965 (2) | 3015 |
| slow | p3→p1 3233→3234 | 3233 (0) | 3236 |
| slow | p1→p2 3234→3284 | 3236 (2) | 3286 |
| slow | p2→p3 3383→3433 | 3383 (0) | 3433 |
| slow | s1→s2 3795→3845 | 3797 (2) | 3845 |

- **Slow run:** the judged ticks in the presentation records equal the judged transition ticks. The render delay is recorded separately (`displayDelayTicks`, `observedTick`). No transition was skipped in these runs.
  - Skipped transitions are covered by EditMode `F04_TransitionEndingBetweenRenders_IsSkippedWithJudgedTicks` and `F04_SameTickChain_WithSlowRender`.
- **VFX clock:**
  - main: 260 frames checked, 9 single-tick hitstop frames, all frozen.
  - free: 343 frames checked, 6 frozen.
  - slow: 88 frames checked, 0 single-tick hitstop frames. The exact VFX = elapsed rule holds on all frames.
- **Main run, other results:**
  - Transitions: all complete, no action before the end, self-test passes.
  - Ten chasers start together at tick 347.
  - The player dies at tick 4324, with no attack starts after it.

## Not shown in Play

- **Death during a transition (cancel):** EditMode only, `F04_DeathBetweenRenders_CancelKeepsJudgedTick`.
- **Skipped transitions:** EditMode only.
- **Several bosses' transitions at once (EXP-PRES-02):** not run.

Claude self-check artifacts, not GPT verdicts.
