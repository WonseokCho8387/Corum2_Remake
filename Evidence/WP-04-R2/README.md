# WP-04-R2 evidence (WP04-M02 defense eligibility, WP04-M03 transition checker)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-04-R2_Report_Claude_v1.0.md`. Review answered: `CORUM2_WP-04-R1_Review_GPT_v1.0.md`.
`Evidence/WP-04/` and `Evidence/WP-04-R1/` are kept unchanged.

Source: run source `d99bfee`, clean tree. The manifest has the per-file source list, and validation matches all 287 rows to `d99bfee`.
Scene and content `wp04-2` and the Play script are the same as WP-04-R1 (no content change in R2).

## Contents

- `runs/RUN-20261110-180516.*` — Play run (Input System virtual gamepad): NDJSON log, data snapshot, manifest, source list, commands, and `report.txt` from `Tools/wp04_log_report.py`.
- `video/main-RUN-20261110-180516.mp4`: 4385 frames, 1280x800, 50 fps game time.
  - Recorded from tick 202 to 4587, so frame = tick − 202.
  - PNG stills every 50 frames.
- `checks/transition_checks.json` — **new ordered transition checker** `Tools/wp04_transition_checks.py --selftest` on this run: transitions, defense decisions and `boss.defenseSkip`, mutation self-test.
- `checks/corrected_WP-04-R1_transition_checks.json` — the same checker on the **WP-04-R1** log (`Evidence/WP-04-R1/runs/RUN-20261110-173813.ndjson`). This corrects the transition part of `Evidence/WP-04-R1/checks/r1_checks.json` (3222→3272, not 3222→3222). The R1 file itself is not modified.
- `checks/run_checks.json`, `checks/r1_checks.json`: the earlier checkers on this log, kept for continuity. **Do not use the `F03_transitions` part of `r1_checks.json`**: that is the defective linking found in M03. Use `transition_checks.json` instead.
- `validation/validate_results.ndjson` — manifest valid, 287 rows match git at `d99bfee`.
- `EV-WP04R2-A01_EditMode_TestResults.xml` and `_summary.txt`: EditMode 223/223 (217 earlier + 6 `AiR2Tests`).
- `evidence_steps_wp04r2.sh`, `evidence_helpers.sh` — the run script (same steps as R1; output folder name unchanged).

## What the logs show

### Transitions, linked in log order (`transition_checks.json`)

All transitions are `ended`. The end always comes after the start in event order, and nothing happens before the end. Totals: unmatched end events 0; `all_complete` true; `all_no_action_before_end` true.

| Boss | Transition | Start → end tick |
|---|---|---|
| warden | p1→p2 | 2791→2850 (hits 2814/2830/2846) |
| warden | p2→p3 | 2975→3025 |
| warden | p3→p1 | 3216→3217 (transition time 0) |
| warden | p1→p2 | **3217→3267** (same-tick chain) |
| warden | p2→p3 | 3366→3416 |
| sentinel | s1→s2 | 3791→3841 |

The self-test (`selftest_all_pass` true) checks that each mutation changes the result:

- **Original log:** all transitions complete with no action before their end.
- **Every end and cancel removed:** each transition becomes `incomplete` or `superseded`, and `no_action_before_end` is null, never true. The overall value is false.
- **Attack start inserted before an end:** that transition's `no_action_before_end` becomes false.
- **The transition's own end carries another phase:** the end is not linked and is reported as unmatched.
- **Duplicated end:** the copy is reported as an unmatched end.
- **Same-tick chain:** it links to its own later end.

In the corrected WP-04-R1 result, the second p1→p2 transition is linked 3222→3272.

### Defense (`transition_checks.json` → `defense`)

- **s1 decisions:** ticks 3542 / 3582 / 3622 / 3662 / 3742, picks None / Guard / None / Guard / None.
- **s2 decisions:** ticks 3867 / 4047 / 4107, all Evade. Same as R1, with ticks shifted by 5.
- **One skip** (`boss.defenseSkip`) at tick 3987:
  - Reason `Busy:Idle:Airborne`: a player attack started while the sentinel was in its phase jump (jump at 3966).
  - No guard or evade answered that attack later.

Behind-the-boss starts and starts during hitstop or stagger do not occur in this Play run. They are covered by EditMode `AiR2Tests`.

Other facts from this log:

- Ten chasers start together at tick 364.
- `jq` air attack is blocked by the re-attack interval at tick 4248, with 2.38 s left.
- Player death at tick 4341, with 0 attack starts after it.

Claude self-check artifacts, not GPT verdicts.
