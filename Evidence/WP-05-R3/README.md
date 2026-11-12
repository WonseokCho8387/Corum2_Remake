# WP-05-R3 evidence (M02 residual: TC-02-10 aggregation)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-05-R3_Report_Claude_v1.0.md`
- Review answered: `CORUM2_WP-05-R2_Review_GPT_v1.0.md`
- `Evidence/WP-05`, `WP-05-R1` and `WP-05-R2` are kept unchanged.

## Change

Only `Tools/wp05_checks.py` changed (commit `6b1e259`). No game code changed, so no new Play or EditMode run was made. The game code is still `5af26f0`, the same source as the R2 runs.

- `TC-02-10.pass` now also requires `scroll_links_all_pass`. This means every scroll cast that started has a valid consumption link: item.request → magic.request → cast → exactly one item.use by `requestId`, with `countAfter` and `cooldown`. A cast that hit nothing counts too, not only the compared hit pair.
- The output lists each scroll cast's link in `TC-02-10.scroll_links`.
- The self-test adds the review's two counterexamples on the **last** scroll cast only, leaving the other scroll intact:
  - (11) its item.use removed
  - (12) its `requestId` +90000

  There are 12 mutations in total.

## Checks (`checks/`, R3 checker on the existing logs)

| Log | TC-02-02 | TC-02-10 | TC-06-19a | Scroll links | Self-test |
|---|---|---|---|---|---|
| R2 main `RUN-20261112-102835` | pass | pass | pass | 786 pass, 1020 pass | 12/12 rejected |
| R2 nores `RUN-20261112-103103` | not run (no magic) | not run | pass | none | 3/3 applicable, 9 N/A |
| R1 main `RUN-20261112-084821` | pass | pass | pass | 801 pass, 1035 pass | 12/12 rejected |
| R1 nores `RUN-20261112-085058` | not run | not run | pass | none | 3/3 applicable, 9 N/A |

## Old vs new control (`control/old_vs_new_TC-02-10.json`)

The same mutations are run on both main logs, under the R2 checker at `5af26f0` (a copy is in `control/`) and under the R3 checker.

| Case | R2 checker | R3 checker |
|---|---|---|
| original | pass | pass |
| last scroll item.use removed (1020 / 1035) | **pass** (the review's defect) | fail |
| last scroll item.use requestId +90000 | **pass** (the review's defect) | fail |

To reproduce:

```
python Evidence/WP-05-R3/tools/old_vs_new_control.py Evidence/WP-05-R3/control/wp05_checks_at_5af26f0.py <R2 main ndjson> <R1 main ndjson>
```

The script uses absolute log paths.

These are Claude self-check artifacts, not GPT verdicts.
