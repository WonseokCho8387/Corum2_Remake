# WP-04-R5 evidence: WP04-F05-R1 (Burst tail), WP04-M05-R1-A/B (presentation checker)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-04-R5_Report_Claude_v1.0.md`. Review answered: `CORUM2_WP-04-R4_Review_GPT_v1.0.md`.
Earlier evidence (`Evidence/WP-04`, `-R1`..`-R4`) is kept unchanged.

Source: run source `82175c6`, clean tree, for both runs. Both manifests are valid, with 302 rows matching git. The checker in `Tools/` is part of that source and is copied in `tools/`. Content is `wp04-3` (unchanged).

## Runs

| Run | Render | Video |
|---|---|---|
| `RUN-20261110-235208` (main) | recorder lock, 1 tick per frame | `video/main-RUN-20261110-235208.mp4`, 4401 frames; recording starts at the tick-193 marker; 0-based still file index `fNNNNN` → HUD tick = index + 194 |
| `RUN-20261110-235521` (slow) | no recorder, `targetFrameRate = 17` (about 3 ticks per frame) | none |

## Checks

- `checks/presentation_checks_{main,slow}.json`: `Tools/wp04_presentation_checks.py --selftest`. Both runs give `all_pass` true and `selftest_all_pass` true.
- `checks/transition_checks_main.json`, `checks/run_checks_main.json`.
- `checks/R4_log_*_under_R5_checker.json`: the three **R4** logs re-checked with the R5 checker. All three are false:
  - The R4 Burst systems looped and left emission on at the end.
  - The R4 logs have no `vfxBegin` or `vfxTailEnd` records.
  - R4's "emission stopped" claim is withdrawn. See the report.
- `validation/validate_results.ndjson`
- `EV-WP04R5-A01_EditMode_TestResults.xml` / `_summary.txt`: EditMode 243/243 (240 + 3 `AiR5PresentationTests`).

## Burst VFX at start and end (main run, `presentation_checks_main.json` → `vfx`)

| Transition (judged) | Cue | Particles at the first VFX step | Particles alive at the judged end | Born after the end |
|---|---|---|---|---|
| warden p1→p2 2781→2840 | armorGlow Aura | 0 (rate × envelope ≈ 0 at the start) | 28 | 0 |
| warden p2→p3 2965→3015 | redAura Burst 35 | **35** | 10 | 0 |
| warden p1→p2 3207→3257 | armorGlow Aura | 0 | 28 | 0 |
| warden p2→p3 3356→3406 | redAura Burst 35 | **35** | 10 | 0 |
| sentinel s1→s2 3781→3831 | blink Burst 30 | **30** | 2 | 0 |

For every ended VFX transition:
- `vfxTimeAtEnd` equals the judged transition duration, 1.0 s.
- The tail record shows `emissionEnabled` false and `loop` false.

In the slow run, the Burst begin counts are 35 / 35 / 30, and no particles are born after the end (0).

## Checker self-test (main run)

All mutations below are rejected. Those marked "new" were added in R5.

- duplicated end
- all ends removed
- `judgedEndTick` + 1
- skipped record added
- missing cue
- orphan start
- **start and end eventIds swapped** (new)
- **shownTick before the judged start, negative delay** (new)
- **all VFX frames and tail records removed** (new)
- **tail values 0.25 for a 1.0 s transition** (new)
- **emission left on after the end** (new)
- **particles born after the end** (new)
- **start burst not emitted** (new)
- VFX advanced during a single-tick hitstop frame

The slow log has no single-tick hitstop frame, so the last mutation is reported as N/A there.

## Not shown in Play

- Death during a transition with VFX (the cancel clears the particles): EditMode only.
- Skipped transitions: EditMode only.
- Several bosses' transitions at the same time (EXP-PRES-02).

Claude self-check artifacts, not GPT verdicts.
