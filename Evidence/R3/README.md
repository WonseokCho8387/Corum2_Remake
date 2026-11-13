# R3 evidence: GPT R2 review §4 (checker only)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R3_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Checkers: `94412a5` (copies in `tools/`). Game code is unchanged since `7a9ca55`, so there are no new Play runs and no new EditMode run.
- Logs: the R2 runs in `Evidence/WP-06-R2`, `WP-07-R2`, `WP-08-R2` and `WP-10-R1`, unchanged.
- `tools/run_r3.py` runs every checker the same way the R2 packaging did and writes `checks/*.json`; `checks/summary.txt` is its output.
- `tools/gpt_r2_replay.py` runs GPT's controls (read-only) against these checkers: `checks/gpt_r2_controls_replay.json`.

## Checker changes

| # | Checker · TC | GPT false pass | New rule |
|---|---|---|---|
| 1 | WP-06 · TC-05-01 | dive y +3 m on one tick | Each air case's x **and y**, relative to the attack start, equal the control on every tick of the motion. |
| 2 | WP-07 · TC-06-05/06 | attribution removed, first attack −999 | One rule in WP-07/08/10: on an R2-diagnostics log (any record has attribution) all fields are required: SingleSource, one velocity set, first = last source = `Hit:<attackId of the hit>`, first = last instance = the hit's instance, `aiMoveDuring` false, `blocked` false. A log without these fields is reported as `legacy (pre-7a9ca55)`. |
| 3 | WP-08 · TC-06-12 | blocked = true on a SingleSource record | same rule |
| 4 | WP-08 · F02-R1 | ignored candidate renamed, HitStun | The ignored record's actor has its named attack instance in AttackActive on that tick; stateBefore = stateAfter = Dead; HP 0. |
| 5 | WP-10 · INT-04 | two sets, Parry source | same rule as #2 |
| 6 | WP-10 · INT-04 | Dodge instance 90000 | The Dodge's enemy attack instance is wb4's, with a startup and AttackActive on or before that tick. |
| 7 | WP-10 · INT-04 | player HP −20 on the Dodge tick | Player HP and wb4 HP on the tick before and on the Dodge tick both equal the Dodge record's values. |
| 8 | WP-10 · INT-06 | undeclared pattern | The selected pattern is in the transition record's pattern list, and the same boss starts `Pattern:<that pattern>`. A projectile alone no longer counts. |
| 9 | WP-10 · INT-06 | p3 condition replaced | Each transition's condition is checked against the log: `HpBelow:r` → hpRatio < r; `PatternUsed:NAMExN` → exactly N selections of NAME by that boss in the old phase up to the transition; `Event:NAME` → a `boss.event` of that boss on the tick or the one before. |

## Results

| Checker | Real logs | Self-tests (all rejected) | GPT controls (all rejected) |
|---|---|---|---|
| WP-06 | all pass | main 34, TC-04-09 2 | earlier 3, new 1 |
| WP-07 | all pass | main 39, TC-06-03 1 | earlier 4, new 1 |
| WP-08 | all pass | main 51, TC-07-04 2 | earlier 4, new 2 |
| WP-10 | all pass; INT-08 `BLOCKED_SPEC`, `pass` false, `observedPartsPass` true | 32 | earlier 14, new 5 (3 from audit.py, 2 from dodge_control.py) |

The self-tests add GPT's 9 cases and 9 R3 cases of my own (other attribution fields, inactive or HP-left ignored candidate, a 1 cm air lift, a p2 boss starting a p3 pattern).

Legacy check: on the WP-07-R1 main log (no attribution fields) the checker reports `legacy` and fails only TC-06-10, exactly as the `eff42f7` checker does (that log has no R2 magic boundary segments).
