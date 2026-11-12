# WP-06 evidence: P0-04 / P0-05 residual TCs

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-06_Report_Claude_v1.0.md`
- Scope: the TCs that the WP-02 reviews left without Play/V evidence:
  - P0-04: TC-04-02, 04-03 (including the enemy-guard cancel), 04-04 (10 × 10 = 100), 04-06, 04-07, 04-08, 04-09
  - P0-05: TC-05-01, 05-03, 05-06
- These are Claude self-check artifacts, not GPT verdicts.

## Source

- Game code: `09389c5`
  - The enemy guard cancels the attack and ends the chain (EXP-COMBO-05).
  - WP06_ComboState scene and Data/WP06, built by `Wp06ContentBuilder`.
  - `Wp06Tests`.
- Checker: `Tools/wp06_checks.py` at `a68a421`. That commit changes only the checker; a copy is in `tools/`.
- The main run uses the clean `09389c5` tree.
- The two TC-04-09 runs are deliberately dirty. Before each run, the Inspector field `CombatConfig.comboWindow` was changed with SerializedObject and saved. The patch is in `runs/*.dirty.patch`. Afterwards the asset was restored from git, and the tree is clean again.
- The validator accepts all three manifests (`validation/validate_results.ndjson`):
  - main: 338 rows match git.
  - dirty runs: commit + patch rebuild 338/338.
- EditMode on `09389c5` passes 278/278: 275 existing tests plus 3 `Wp06Tests` (`EV-WP06-A01_EditMode_*`).

## Runs

| Run | What | Video |
|---|---|---|
| main `RUN-20261112-140343` | All P0-04/05 segments, zones 30 m apart, virtual keyboard | `video/main-RUN-20261112-140343.mp4`, 2351 frames from tick 196 |
| cw02 `RUN-20261112-140614` | TC-04-09 with `comboWindow` = 0.2 (Inspector) | `video/cw02-…mp4` |
| cw06 `RUN-20261112-140726` | TC-04-09 with `comboWindow` = 0.6 (Inspector) | `video/cw06-…mp4` |

Keys: J attack, K special (launcher on the ground; direction + K in the air = dive), L guard (RB), Space jump, A/D left/right. In the air, direction + J is the dash and neutral J is the air normal attack.

## Results (`checks/`)

| TC | Main log result |
|---|---|
| TC-04-04 | One chain of 10 attacks: ground1–4, launcher, air1–4, dive. Each attack hits the same 10 targets once (10 distinct targets, 10 hits). `combo.end`: 10 steps, `comboHits` 100. One jump, not counted as a step. Dive area unlock is on. |
| TC-04-02 | Launcher on `la` (Launched). The player never leaves the ground: every tick `grounded`, max y 0, no jump. Two ground attacks then hit the other enemy `lb`. |
| TC-04-06 | Initial dash: `p1` only, combo +1, dash ends the tick after the hit. Pierce unlock: `p1`, `p2`, `p3`, combo +3. |
| TC-04-08 | Initial dive: `d1` only, the closest target (U-05 / EXP-ATK-05). Area unlock: `d1` on contact, then `d2` and `d3` by the landing area, 3 targets. |
| TC-04-03 | Each case ends the chain, and the next attack starts a new chain. **Enemy guard:** the guarded hit has `comboAfter` 0, attack end and chain end are `EnemyGuardCancel(R-04-03, EXP-COMBO-05)`, combo 0, the next attack is a new chain. **Miss:** `Miss`. **Timeout:** 0.50 s after the attack ends (window 0.5). **RB:** `GuardCancel`, `hitboxRemoved=true`, no later hits from that attack. |
| TC-05-01 | Right held during 28 of 45 attack ticks. x does not change within any attack motion. Walking starts after the motion ends. Jump during the attack is rejected (`ActionLock:GroundAttack`); the jump after the motion end is accepted. |
| TC-05-06 | A press during the motion is buffered at tick 1637 and runs at the motion end, tick 1647, with separate press and exec times. A press after the window gives `Timeout`, then a new chain. The dash reservation is received at tick 1879 and runs at the landing, tick 1911 (`reservedGameTime` < `execGameTime`). |
| TC-04-07 | Launcher, air1, then the dash branch, all in the same chain. The attack pressed during the dash is reserved (pierce on, so the dash lasts its full time). The next press in the air is rejected (`ReservationExists`), and no air normal follows the dash. On landing (tick 1911), `ground1` runs as step 4 of the same chain. |
| TC-05-03 | RB is rejected in all 5 cases (`ActionLock:AirDash` / `ActionLock:Dive`), with no guard cancel. Press phase: dash 0.06 / 0.31 / 0.69 of its motion; dive 0.00 / 0.25. |
| TC-04-09 | The same input gaps after the attack end (0.3 s and 0.6 s) are measured. With window 0.2 the chain times out and both second attacks are step 1 of a new chain. With window 0.6 both are step 2 of the same chain. The gap equal to the window (0.6) is still inside, within tick quantization. `data.json` records the window value of each run. |

**Self-test:** the main log rejects all 11 counterexample mutations. The TC-04-09 check rejects its counterexample.

## Limits

- **EXP-COMBO-05 is an experimental policy, not a decided rule.** It covers three choices:
  - An enemy guard cancels the player's timed attack and ends the chain.
  - A guarded hit does not count toward the combo.
  - When a guarded hit and an effective hit happen in the same tick, the guard still ends the chain (CF-06 is pending).

  Whether a simple guard or a guard counter interrupts the attack is open in R-04-03.
- **TC-05-03 recovery:** dash and dive have no separate recovery phase in the current data. The dive ends on landing, and the CF-02 landing lock is not applied. The latest RB press was at 0.69 of the dash.
- **TC-05-06:** the general buffer policy (EXP-COMBO-01: one pending press for the whole motion) is experimental.
- **TC-04-08:** target selection is U-05.
- **Input:** virtual keyboard only. No physical device or Steam Deck was used.
