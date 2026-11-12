# WP-06-R2 evidence: GPT WP-06~08 R1 review (WP-06 part)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R2_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Runs on `7a9ca55` (game, data, tests, checkers).
- Checkers in `tools/` are from `eff42f7`. After the recording, only two things changed: two WP-07 self-test predicates and the WP-10 INT-04 startup lookup.
- EditMode on `7a9ca55` (clean): 307/307.
- Manifests:
  - main: 412/412 rows match git.
  - cw02 and cw06 are deliberately dirty (`comboWindow` 0.2 / 0.6). Each has 411 matching rows plus 1 file rebuilt from the patch, and was restored from git after its run.

| Run | What |
|---|---|
| main `RUN-20261113-051441` | All WP-06 and R1 segments, plus the R2 TC-05-01 air matrix |
| cw02 `RUN-20261113-052020`, cw06 `RUN-20261113-052239` | TC-04-09 |

## R2 results (`checks/`)

**TC-05-01 air attacks** (GPT: air normal, dash and dive were missing).

Each attack has a control run with no extra input, plus six cases:
- **Move:** Left is pressed at the start, middle or end of the motion and held until 6 ticks after it ends.
- **Jump:** Jump is pressed at the same three points, then once more after the motion ends.

Results:
- In all 18 cases, x relative to the attack start equals the control on every tick of the motion.
- Steering starts 1 tick after the end.
- Each jump during the motion is refused with that motion's lock (`ActionLock:AirAttack`, `AirDash` or `Dive`) and is linked to its press.
- The press after the end runs: an ExtraJump in the air, or a ground jump after the dive lands.

| Attack | Motion ticks | Move press offsets | Jump press offsets |
|---|---|---|---|
| Air normal | 12 | 1 / 6 / 10 | 1 / 6 / 10 |
| Air dash | 12 | 2 / 6 / 10 | 1 / 6 / 10 |
| Dive | 9 | 2 / 4 / 7 | 1 / 4 / 7 |

**TC-04-06** (WP06-M01-R1):
- The reopen's `guardEndTick` (3977) equals both this dash's guard tick and the `EnemyGuardCancel` tick.
- `combo.begin PostGuardEffectiveHit` comes on the later hit tick (3989), before the reopen, in the new chain.

**TC-05-01 ground jumps** (WP06-M01-R1): the jump after the motion ends is linked to the second Jump press by `inputEventId`.

All other WP-06 sections still pass.

Self-test, all rejected:
- main: 32 mutations, including 3 from GPT and 5 new R2 cases;
- TC-04-09: 2.
