# WP-07-R2 evidence: GPT WP-06~08 R1 review (WP-07 part)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R2_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Runs on `7a9ca55`. The checker in `tools/` is from `eff42f7`.
- EditMode: 307/307.
- Manifests:
  - main: 412/412 rows match git.
  - hs is deliberately dirty (`groundNormals[0].hitstop` 0.10): 411 matching rows plus 1 rebuilt file.

| Run | What |
|---|---|
| main `RUN-20261113-052454` | All WP-07 and R1 segments, plus the R2 magic wakeup boundaries |
| hs `RUN-20261113-052923` | TC-06-03 |

## R2 diagnostics (`7a9ca55`)

`enemy.knockback` now records, for its interval:
- which sources set the knockback velocity: `velocitySets`, plus the first and last source and attack instance;
- `aiMoveDuring`: whether AI movement was active;
- `blocked`: whether the enemy ended more than 1e-4 m off the intended move;
- `attribution`: `SingleSource` or `Ambiguous`.

## R2 results

**Knockback rows** (WP07-M02-R1). Every displacement used by TC-06-05 and TC-06-06 meets all of these:
- the record is SingleSource and names the hit's attack instance;
- its start velocity equals the hit's knockback velocity, with the same sign;
- its displacement equals endX − startX;
- each full-precision end agrees with that tick's 2-decimal position (±0.005).

Values:
- magic at 5 m/s: 0.468 m (k1–k4);
- weak magic at 0.5 m/s: 0.010 m;
- equal to one tick of deceleration, 0.6 m/s: 0.012 m.

**TC-06-10, physical** (WP07-M02-R1):
- **Start boundary:** the same attack first touched the downed target (skipped as Downed at 3461), then was refused on the first Wakeup tick, 3464.
- **End boundary:** the hit lands on the first Idle tick, 3693. The attack ends at 3703, after the hit.

**TC-06-10, magic** (GPT asked for Play evidence at the magic boundaries):
- **Start boundary:** cast at 3901. The active fireball skips the downed target at 3910, then is refused as Wakeup at 3912, the first Wakeup tick.
- **End boundary:** refused as Wakeup at 4189. Wakeup ends at 4191, and the same fireball hits on 4191 (stateBefore Idle). The magic motion ends at 4210.

Self-test, all rejected:
- main: 36 mutations, including 4 from GPT and 6 new R2 cases;
- hs: 1.
