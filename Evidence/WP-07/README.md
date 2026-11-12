# WP-07 evidence: P0-06 hit / reaction TCs TC-06-01 to TC-06-10

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-07_Report_Claude_v1.0.md`
- These are Claude self-check artifacts, not GPT verdicts.

## Source

- The run source is `e0adba5`:
  - the WP07_HitReaction scene and Data/WP07, built by `Wp07ContentBuilder`;
  - the checker.
- No game code changed after `09389c5` (WP-06).
- **Medium and large size classes** (test data):

  | Class | Stagger resist | Physical knockback scale | Launch |
  |---|---|---|---|
  | Medium | 0.5 | 0.5 | Resist |
  | Large | 0.8 | 0.2 | Resist |

- **`CombatConfig_WP07`** is a copy of the WP-02 data. Its ground4 is an explicit 3-event test attack (TEST-MULTI-01): `hitEvents` 3, active 0.24 s. This is not magic content.
- **`f4a7723`** changes only:
  - the checker: the TC-06-10 "wakeup start" tolerance is now 5 ticks, which is ground1's 3-tick startup plus 2;
  - the build-settings scene list, which Unity wrote after the content commit.

  The copy in `tools/` is this version.
- **main run:** clean tree. The validator matches 355 rows.
- **hs run:** deliberately dirty. The Inspector field `CombatConfig_WP07.groundNormals[0].hitstop` was changed from 0.04 to 0.10 and saved. Its patch has two parts:
  - that change;
  - the scene-list entry for WP07_HitReaction in `ProjectSettings/EditorBuildSettings.asset`, which was not yet committed at the time.

  The validator rebuilds the dirty run from commit + patch, and both manifests are valid. The asset was restored from git afterwards.
- **EditMode:** 278/278 on `e0adba5` (`EV-WP07-A01_*`). There are no new tests, because there is no new game logic.

## Runs

| Run | What | Video |
|---|---|---|
| main `RUN-20261112-142628` | All TC-06-01 to 06-10 segments, virtual keyboard | `video/main-…mp4`, 2902 frames |
| hs `RUN-20261112-142906` | TC-06-03 with ground1 hitstop 0.10 (Inspector) | `video/hs-…mp4` |

## Results

| TC | Result (`checks/wp07_checks_main.json`, `checks/wp07_checks_TC-06-03.json`) |
|---|---|
| TC-06-01 | ground1 overlaps `o1` for its active ticks and hits it once (combo 1). The next motion, ground2 (a new `attackInstanceId`), hits it once more (combo 2). |
| TC-06-02 | ground1, ground2 and ground3 each hit m1, m2 and m3 once. The 3-event test attack has 3 hit events × 3 targets = 9 hits, with 9 unique (event, target) pairs. |
| TC-06-03 | Main run, hitstop 0.04: 1 target and 10 targets give the same motion length (15 ticks) and one stop record each. Inspector run, hitstop 0.10: both cases are 18 ticks, so the stop does not add up with the number of targets, and the edited value took effect (+3 ticks). |
| TC-06-04 | Six hits each (ground1–3 plus the 3-event ground4). Small, medium and large are staggered by every hit; the timer is refreshed on each hit, using `staggerTime × (1 − resistance)`. The boss (resistance 1) gets `StaggerResisted` 6 times and never enters Stagger. |
| TC-06-05 | Physical knockback velocity: small 1.5 / 0.5, medium 0.75 / 0.25, large 0.3 / 0.1, boss 0. The 4th (multi-event) attack is not a long knockback. The boss does not move. |
| TC-06-06 | The fireball command (magic) hits all four, including the boss with physical knockback 0, with knockback velocity 5 (> 0, `MagicStagger+Knockback`). |
| TC-06-07 | **Four air hits:** while being hit, the target falls 0.04 m/tick (2 m/s, juggle fall speed). It does not stop or float, and it lands at tick 1591. **Two hits, then stop:** the same slow fall, then a normal fall of up to 0.12 m/tick, and it lands at tick 2025. |
| TC-06-08 | The same launcher: launchable peak 3.13 m, resist 0.75 m, immune 0. All three take 12 damage and combo 1. Immune shows `LaunchImmune:DamageAndCount`. |
| TC-06-09 | ↘+K from the left (facing +1) and ↙+K from the right (facing −1) each hit the downed `d1` once (`DownHit`, still Down, not launched). The dive hits the downed `d2` for 20. |
| TC-06-10 | **Down:** ground1 is skipped (`DownedNotHittable…`). **Wakeup:** no damage at the start (tick 2666, 4 ticks into wakeup) or in the middle (2682); wakeup runs 2662–2691. **Magic:** the fireball during wakeup is skipped (`TargetInvincible:Wakeup`). **First idle tick 2692:** an attack started there hits at 2696 (ground1 startup). |

**Self-test:**
- The main log rejects all 10 counterexample mutations.
- The TC-06-03 run comparison rejects "the Inspector change has no effect".

## Limits

- **Numbers:** all are test data (EXP-ATK-00, TEST-DMG-01). "Small / medium / large" are test size classes. The original game's per-enemy values are not decided here.
- **TC-06-02:** the multi-event attack is a test attack. Real 3-hit magic content is not decided.
- **TC-06-09:** "no extension" of the down time when a downed enemy is hit is CF-10 (EXP-DOWN-01/02).
- **TC-06-10:** "first actionable moment" means the first Idle tick. The attack started there and hit 4 ticks later because of its startup.
- **Input:** virtual keyboard only. No physical device or Steam Deck was used.
