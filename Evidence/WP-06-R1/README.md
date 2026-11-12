# WP-06-R1 evidence: fixes for the GPT WP-06 review

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-06to08-R1_Report_Claude_v1.0.md`
- Review being answered: `CORUM2_WP-06_Review_GPT_v1.0.md`
  - F01: pierce dash after a guard
  - M01: checker passed 8 counterexamples
  - M02: EXP-COMBO-05 missing from the manifest
  - 5.3: +5 display; TC-05-01 start / middle / end matrix
- These are Claude self-check artifacts, not GPT verdicts.

## Source

| Commit | What |
|---|---|
| `88b4832` | Game fixes (`CombatSystem`) |
| `d37cc8b` | WP-06 scene zones and checker. The main run uses this clean tree. |
| `785368b` | Changes only the WP-07 and WP-08 checkers. The two TC-04-09 runs use this tree. |

Game fixes in `88b4832`:
- WP06-F01: an enemy guard still ends the chain on its own tick (EXP-COMBO-05). A dash or dive keeps moving after that. If the same motion makes a later effective hit, a new chain opens (`combo.reopen`) and the hit counts (EXP-COMBO-06, R-04-05/06, CF-06).
- M02: EXP-COMBO-05 and EXP-COMBO-06 are in `Simulation.ExperimentalPolicies`. The manifest `experimental_policies` field now lists both.
- 15 EditMode regressions in `Wp06to08R1Tests`.

TC-04-09 runs:
- They are deliberately dirty. `CombatConfig.comboWindow` was set with SerializedObject (0.2 / 0.6), and the asset was restored from git after each run.
- Each patch touches only `Data/WP02/CombatConfig.asset`: the window change, plus the three serialized death/revive default fields already reported in the WP-06 review.
- These are re-recordings. The first pair was discarded because my uncommitted WP-07/08 checker edits were in the tree when it was recorded.

Manifest validation (`validation/validate_results.ndjson`):
- main: 396 rows match git.
- dirty runs: 395 match, plus 1 file rebuilt from the patch.

EditMode on `785368b` (clean): 293 / 293 (`EV-WP06R1-A01_EditMode_TestResults.xml`).

## Runs

| Run | What | Video |
|---|---|---|
| main `RUN-20261112-185318` | All WP-06 segments plus the R1 segments (below) | `video/r1main-…mp4`, 4208 frames from tick 187 |
| cw02 `RUN-20261112-190144` | TC-04-09, `comboWindow` 0.2 | `video/r1cw02-…mp4` |
| cw06 `RUN-20261112-190326` | TC-04-09, `comboWindow` 0.6 | `video/r1cw06-…mp4` |

R1 segments, all in main:
- **TC-05-01 matrix:**
  - Ground normal (J) and launcher (K).
  - Move pressed at +1 / +6 / +11 ticks of a 12-tick motion, held through the end.
  - Move pressed mid-motion, released before the end, pressed again after.
  - Jump at +1 / +6 / +11, then again after the end.
- **WP06-F01:**
  - `GuardThenPierce`: pierce dash at guard-1 `gp`, then the bag `gq`.
  - `NoGuardPierce`: the same layout with the guard-0 copy `np`, then `nq`.
- **TC-04-04 +5:** one ground attack on 5 bags at one point (`FiveTargetsOneAttack`).

## Results (`checks/`)

| TC | Main log result |
|---|---|
| TC-04-06 F01 | **Guard 1:** tick 3982, `gp` guarded, combo 0→0, `combo.end` EnemyGuardCancel on the same tick. Tick 3994, same dash instance: `gq` takes 12 damage and Stagger. `combo.reopen` on the same tick, combo 0→1, the new chain ends by Timeout with 1. **Guard 0 control:** `np` 0→1 at 4132, `nq` 1→2 at 4144, no reopen, no guard cancel. |
| TC-04-04 | One chain of 10 attacks, each on the same 10 targets. Every hit is effective (unguarded, HP loss, +1). Counts run 1…100 in order, HUD 100 as in WP-06. **+5:** one ground1 hits f0–f4 on tick 4281, counts 1–5, combo end 5 (video frame ≈ 4094). |
| TC-04-03 | The four chain ends. The next attack, traced through the whole log (not cut at the marker), is a new chain, step 1, `newChain` true each time. |
| TC-05-01 | All 14 matrix rows pass. During the motion x is constant and the state stays GroundAttack. Held move: Walk starts the tick after the end. Re-press: Walk starts on the re-press tick (13 ticks after the end). Jump during the motion: rejected `ActionLock:GroundAttack`, linked by input id. The jump after the end runs. The original WP-06 rows also pass. |
| TC-04-02/07/08, TC-05-03/06 | Pass with the stricter links: hits inside their own motion, after the launcher; dive area hits on the landing tick, one contact; RB reject linked to its press inside the motion; `execGameTime` present and equal to the start of the same input. |
| TC-04-09 | Gaps after the motion end are 0.30 / 0.60 s in both runs: 0.2 → Timeout and new chain; 0.6 → same chain, step 2. The checker now requires equal gaps in both runs and a largest gap that reaches the large window. |

Self-test:
- Main: 24 mutations, all rejected. These are the 11 from WP-06, the 8 GPT counterexamples, the miss-next-chain counterexample, and 5 R1 cases.
- TC-04-09: 2 mutations, including the GPT 0.40 / 0.50 gap counterexample.

## Not claimed

- The initial single dive's 91 is not claimed as Play evidence (GPT 5.3).
- U-05 (initial target pick, reservation length), CF-02, CF-03 and CF-06 stay open. EXP-COMBO-05/06 are experimental choices.
