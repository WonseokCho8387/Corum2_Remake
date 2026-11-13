# WP-09-R2 evidence: GPT WP-09-R1 review (checker only)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-09-R2_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Checker: `08fad53` (`tools/wp09_checks.py`). Game code is unchanged, so there are no new Play runs and no new EditMode run.
- Logs: the four WP-09-R1 runs in `Evidence/WP-09-R1/runs` (main 234229, plane 234529, dev 235402, slide 235043), plus the earlier WP-09 main 152613.

## Checker changes (WP09-M02, 9 GPT counterexamples)

| TC | New rule |
|---|---|
| TC-02-01 main | Each result's tick must be at or after its own press tick, in addition to the input id. (Ring results are logged just before their route record on the same tick, so the event order is not used.) |
| TC-02-01 main / dev | The executed item must equal the request's target. |
| TC-01-01 plane | Air frames ≤ all frames. Boss moving samples ≤ ground-moving + airborne + teleport samples. Propelled samples > 0 and ≤ non-first samples. |
| TC-02-01 dev | A real `ring.release` must sit between the target fix and the assignment. The discard's selected item = its target = the discarded item. |
| TC-03-04 slide | The slide start's `inputEventId` must be a Jump press received on or before the start. Per-tick x from the tick before the start to the end must move only in the facing direction, end at the end event's position (±1e-4), and that tick's state must equal the end state. |

## Results

- All four R1 logs still pass every TC; the earlier WP-09 main log also passes.
- Self-tests: main 18, plane 14, dev 11, slide 6 = 49, all rejected (the 40 earlier cases plus the 9 GPT cases).
- `checks/gpt_wp09r1_counterexamples_replay.json` runs GPT's own mutation code (`evidence/WP-09-R1_GPT_v1.0/review.py` and the WP-09 v1.0 snippet) against this checker: 12 earlier + 9 new cases, all rejected.
