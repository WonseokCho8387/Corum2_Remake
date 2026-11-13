# R4 evidence: GPT R3 review (WP-06/07/08/10 checkers)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R4_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Checkers: `cf7c37e` (`tools/`). Game code is unchanged; no new Play or EditMode runs.
- Logs: the same R2 logs as `Evidence/R3` (WP-06-R2, WP-07-R2, WP-08-R2, WP-10-R1 runs), plus the earlier WP-07-R1 main 194039 as the legacy control.

## Checker changes

| GPT R3 # | Checker | New rule |
|---|---|---|
| 1 | WP-06 TC-05-01 | The press after the motion must execute as a jump: an `air.use` ExtraJump after the air normal / air dash, a `motor.jump` after the dive lands. |
| 2-4 | WP-07/08/10 | The rule set comes from the run's manifest (`<runId>.manifest.yaml`, next to the log or under Evidence / Diagnostics): a `source_revision` containing 7a9ca55 requires every attribution field. A missing manifest or unknown revision is checked with the fields required. A pre-7a9ca55 run that carries any attribution field is also checked with them required. |
| 5 | WP-08 WP08-F02-R1 | The ignored attacker's own attack instance must be AttackActive in an event before the ignore event. |
| 6 | WP-10 TC-INT-04 | The Dodge's playerHp / enemyHp and the two tick HPs must be present, finite numbers before they are compared. |
| 7-9, boundary | WP-10 TC-INT-06 | Transitions are checked against the run's own data (`<runId>.data.json`, boss definition by the spawn's enemyId) with Core's `TryTransition` semantics: the first transition of the from-phase in list order that holds must target the new phase. HpBelow uses hp <= value x maxHp + 1e-4. PatternUsed counts uses since entering the from-phase (>= count). Event needs a structured `boss.event` of exactly that name from the same boss since it entered the phase. The logged condition must name that transition and value. The phase's pattern list must equal the run data. The selected pattern's startup must fall inside that phase stay. |

## Results

- All R2 logs keep their R3 results (INT-08 still BLOCKED_SPEC with observed parts passing). WP-07-R1 194039 is reported as `legacy build 4f02a15` and fails only TC-06-10, as GPT recorded.
- Self-tests: 173 (R3's 161 + 12), all rejected except the one positive control, which passes as it should: WP-06 36+2, WP-07 40+1, WP-08 53+2, WP-10 39 (38 rejections + HP-threshold positive control).
- `checks/gpt_r3_probes_replay.json`: GPT's R3 `probes.py` transforms copied verbatim. 9/9 counterexamples rejected; the HP-threshold control passes.
- `checks/gpt_r2_controls_replay.json`: the earlier 25 + 9 GPT controls, all rejected.
