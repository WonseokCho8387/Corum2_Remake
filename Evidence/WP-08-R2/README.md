# WP-08-R2 evidence: GPT WP-06~08 R1 review (WP-08 part)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R2_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Runs on `7a9ca55`. EditMode: 307/307.
- Manifests:
  - main: 412/412 rows match git.
  - pw10 and pw20 are deliberately dirty (`parryWindow` 0.10 / 0.20): each has 411 matching rows plus 1 rebuilt file.

| Run | What |
|---|---|
| main `RUN-20261113-053139` | All WP-08 and R1 segments, plus R2 `DoubleLethalGuard` and `DoubleLethalHit` |
| pw10 `RUN-20261113-053700`, pw20 `RUN-20261113-053926` | TC-07-04, checked together with main (window 0.15) |

## Game fix (`7a9ca55`, WP08-F02-R1)

`ApplyAttackToPlayer` now first checks whether the player is already dead. Two enemy hits can be collected for the same tick; if the first one kills, the second now only logs `hit.player.ignored` (`TargetAlreadyDead`). It causes no damage, no reaction and no second death. The order of hits on a living player is unchanged (CF-05/06).

`R2DeathTests` (9 tests) cover:
- two attackers at HP 1, with guard, guard break, parry and no guard;
- single-attacker controls;
- HP 2 with two guarded hits: both are real hits, the second kills, one death;
- HP 100 with two unguarded hits: both are real hits.

## R2 results

**WP08-F02-R1 in Play.** In each case the player presses attack twice after death, and no attack starts.

| Case | Tick | Events, in order |
|---|---|---|
| Guard | 5132 | `guard.block` from dg1 → `player.death` (dg1, `DefendedHit:Guard`) → `hit.player.ignored` from dg2 (stateBefore Dead). Death motion once. |
| No guard | 5382 | `hit.player` from dn1 → `player.death` (dn1) → `hit.player.ignored` from dn2 (stateBefore Dead). Death motion once. |

**WP08-F02** (WP08-M01-R1): each lethal death names the defended attacker. `death.flow → Motion` follows on the same tick.

**TC-06-12** (WP08-M01-R1):
- aw, weak magic on armor: 0.010 m. The record is SingleSource and consistent with the hit and the ticks (same rule as WP-07).
- a2, normal magic: 0.468 m.

**TC-07-04:** the parry window was 0.15 in main and 0.10 / 0.20 in the two Inspector runs. Results: G G G P / G G P P / G P P P, as before.

Self-test, all rejected:
- main: 46 mutations, including 4 from GPT and 6 new R2 cases;
- TC-07-04: 2.
