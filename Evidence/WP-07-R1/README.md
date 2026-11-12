# WP-07-R1 evidence: fixes for the GPT WP-07 review

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-06to08-R1_Report_Claude_v1.0.md`
- Review being answered: `CORUM2_WP-07_Review_GPT_v1.0.md`
  - F01: weak positive knockback moved 0 m
  - M01: wakeup boundaries
  - M02: checker passed 12 counterexamples
- These are Claude self-check artifacts, not GPT verdicts.

## Source

| Commit | What |
|---|---|
| `88b4832` | WP07-F01 knockback fix and 15 EditMode regressions (`Wp06to08R1Tests`). |
| `742b476` | Diagnostics, scene zones and data. |

WP07-F01 fix (`88b4832`):
- The enemy now moves with this tick's knockback velocity, then decelerates. Before, it decelerated first.
- Any positive knockback now moves, including one at or below one tick of deceleration (30 m/s² × 0.02 s = 0.6 m/s).
- 0 still means no movement.

Diagnostics (`742b476`):
- `enemy.knockback` record, full precision: start tick, start x, end x, displacement, start velocity.
- Diagnostics only; no gameplay change.
- The checker uses it because per-tick positions are rounded to 2 decimals.

Scene zones and data (`742b476`):
- `magicWeak` (magic scale 0.1 → fireball 0.5 m/s).
- `magicEqual` (0.12 → 0.6 m/s).
- `physEqual` (physical scale 0.4 → 1.5 × 0.4 = 0.6 m/s).
- Bags `w3` / `w4` for the wakeup boundaries.

Runs:
- main `RUN-20261112-194039` uses the clean tree.
- A first main recording (`RUN-20261112-192422`) was discarded. It started while untracked WP-06-R1 evidence files were in the tree, so its manifest was invalid.
- hs `RUN-20261112-192815` is deliberately dirty. Its patch is only `groundNormals[0].hitstop` 0.04 → 0.10 in `CombatConfig_WP07.asset`, restored from git afterwards.

Manifest validation: main 404/404 rows match git; hs 403 match, plus 1 file rebuilt from its patch.

EditMode on `742b476`: 293 / 293.

## Results (`checks/wp07_checks_main.json`, `checks/wp07_checks_TC-06-03.json`)

| TC | Result |
|---|---|
| TC-06-05 | First-hit displacement from the record: small 1.5 m/s → 0.054 m; medium 0.75 → 0.018 m; **large 0.3 → 0.006 m** (was 0); boss 0 → no knockback record, maximum displacement 0. **Equal to one tick, `pe` 0.6 m/s → 0.012 m.** No large 4th-hit push. |
| TC-06-06 | Each magic hit is linked cast → effect → hit with the same instance id. Fireball 5 m/s on small / medium / large / boss → 0.468 m each. **`mw` 0.5 m/s → 0.010 m** (was 0). **`me` 0.6 m/s → 0.012 m.** Scale 0 is still rejected by validation (EditMode). |
| TC-06-10 start | Ground attack 54 starts at 3466, before the wakeup. Its hitbox skips `w3` as downed at 3468. `w3` changes Down → Wakeup at 3471, and the same attack is refused `TargetInvincible:Wakeup` on 3471 exactly. No hit. |
| TC-06-10 end | Ground attack 56 starts at 3695 and is refused as Wakeup at 3697. The last Wakeup tick is 3699. **It hits `w4` on 3700, the first Idle tick** (stateBefore Idle, HP 988 → 978). No refusal after the wakeup ends. |
| TC-06-10 original | `w1` wakeup 2653–2682, first Idle 2683. The fresh attack (input after the change) hits at 2687. The +4 is the input and startup delay, as GPT found. It is not used as the boundary proof; the end-boundary row above is. |
| TC-06-01..04, 07..09 | Pass with the added links. The hits are effective. ground4 events are the declared `inst:0/1/2`, in time order. Exactly one stop in both the 1- and 10-target cases. The reaction state matches the per-tick state. The four air hits are on four registered air1–4 instances. Every launch target has samples. Each down attack has its own special press. |
| TC-06-03 | Hitstop 0.04 → 0.10 adds 3 ticks to the motion, one stop for 1 and for 10 targets. |

Self-test:
- Main: 26 mutations, all rejected. These are the 10 from WP-07, the 12 GPT counterexamples, and 4 R1 cases.
- hs: 1 mutation.

## Correction

The WP-07 report and README said the attack started on the first Idle tick. That was wrong. In the original run, the first Idle is 2692, and the next attack starts at 2694 and hits at 2696. The R1 end-boundary row above is the boundary evidence.
