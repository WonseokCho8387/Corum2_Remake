# WP-09-R3 evidence: GPT WP-09-R2 review (checker only)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-09-R3_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Checker: `63ddeac` (`tools/wp09_checks.py`). Game code is unchanged, so there are no new Play or EditMode runs.
- Logs: the four WP-09-R1 runs in `Evidence/WP-09-R1/runs` (main 234229, plane 234529, dev 235402, slide 235043) and the earlier WP-09 main 152613.
- The checker now reads the run's own `<run>.data.json` beside the log (`input.stickDeadzone` 0.35, `movement.slideSpeed` 9). Without it, the dev release and slide step rules fail closed.

## Checker changes (GPT WP-09-R2 section 2)

| # | TC | New rule |
|---|---|---|
| 1 | TC-01-01 plane | Boss moving samples <= ground-moving + airborne (teleport is not counted by `CombatView.Sync`). |
| - | TC-01-01 plane | Propelled samples bound is now all samples (counted before the first-sample class), not samples minus first. |
| 2 | TC-02-01 dev | The release between fix and assignment is Held -> Released in Registering, for the fixed target, between their ticks, with the logged stick inside the run's stickDeadzone; no later cursor stick before the assignment leaves it. |
| 3 | TC-02-01 main / dev | The quickslot use's tick = its `execTick`, its `requestTick` = the request's tick, it comes after the request, `execGameTime` = its game time; an `Accepted:NextTick` request that waited for nothing executes exactly one tick later. |
| 4 | TC-02-01 dev | The discard's inventory snapshot, and the next later snapshot in the run, show the item at `countAfter`. |
| 5 | TC-03-04 slide | Each slide start has its own Jump press, delivered and consumed by the game (`slide`) on the start tick, claimed by no other start. |
| 6 | TC-03-04 slide | Every tick from the start to the end moves at most slideSpeed x that tick's step (<= 0.02 s) + 1 mm. Logged maximum is 0.180002 m against 0.18 m. |

## Results

- All five logs still pass every TC.
- Self-tests: main 18, plane 16, dev 14, slide 8 = 56, all rejected (49 earlier + 7 new: GPT's six variants and propelled > samples).
- `checks/gpt_wp09r2_controls_replay.json`: GPT's own probe functions from `audit.py` copied verbatim. 6/6 variants rejected; the aggregate control `propelled = samples` is now accepted, as GPT expected.
- `checks/gpt_wp09r1_counterexamples_replay.json`: the earlier 12 + 9 GPT cases, all rejected, with the config loaded and the originals passing.
