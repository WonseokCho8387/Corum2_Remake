# WP-08-R1 evidence: fixes for the GPT WP-08 review

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-06to08-R1_Report_Claude_v1.0.md`
- Review being answered: `CORUM2_WP-08_Review_GPT_v1.0.md`
  - F01: weak magic on super armor moved 0 m
  - F02: lethal guard damage did not kill
  - M01: checker passed 17 counterexamples
  - C01 / C02: corrections
- These are Claude self-check artifacts, not GPT verdicts.

## Source

| Commit | What |
|---|---|
| `88b4832` | Game fixes and 15 EditMode regressions (`Wp06to08R1Tests`). |
| `742b476` | `enemy.knockback` diagnostics record, WP-08 scene zones and data. |

Game fixes in `88b4832`:
- WP08-F01: shares the WP07-F01 knockback fix (move, then decelerate).
- WP08-F02: guard, guard-break and parry damage that brings HP to 0 now enters the death flow. Before, it returned while still guarding.
- The fix adds `KillPlayerByDefendedHit`, which does the following:
  - cancels the attack and ends the chain;
  - clears the buffer and enters Dead;
  - writes `player.death` with `cause = DefendedHit:<Guard|GuardBreak|Parry>`.
- Non-lethal guard damage is unchanged.

Scene zones and data in `742b476`:
- `armorWeak`: armored slow attacker, magic scale 0.1.
- Lethal zones: `lg` (hitter), `lk` (guard break) and `lp` (hitter, parry).
- `Z2Near` spawn: the full-strength magic now pushes `a2` 0.47 m (0.37 m before the fix), so the armor-again segment starts 0.5 m closer.

Runs:
- main `RUN-20261112-193002` uses the clean tree.
- pw10 `RUN-20261112-193517` and pw20 `RUN-20261112-193719` are deliberately dirty:
  - `CombatConfig.parryWindow` set to 0.10 / 0.20;
  - plus the same three serialized death/revive default fields as in WP-08.

Manifest validation: main 404/404 rows match git; dirty runs 403 match, plus 1 file rebuilt from the patch.

EditMode on `742b476`: 293 / 293.

## Results (`checks/wp08_checks_main.json`, `checks/wp08_checks_TC-07-04.json`)

| TC | Result |
|---|---|
| TC-06-12 | **Scale 1 `a2`:** MagicBreaksArmor, Stagger, 5 m/s, record 0.468 m. Attack cancelled, no hit from it. The next armor attack is armored again (SuperArmor:DamageOnly on `a2`, effective). **Weak 0.1 `aw` (F01):** MagicBreaksArmor, 0.5 m/s, **record 0.010 m** (was 0). Cancelled. The next `aw` attack is armored again. |
| WP08-F02 | HP 1, D 10 → defended damage 1, HP 1 → 0. Each case: `player.death` on the same tick (front guard 4032, guard break 4282, parry 4532) with its cause. Then two attack presses each and 0 attack starts. A feather revives between cases. |
| TC-06-13 | Roll 0.04 → hit, 0.16 → skip `PlayerInvincible:RollWindow`, 0.34 → hit. Each case has its own inequality (before < 0.05, inside 0.05–0.30, after > 0.30). The skip's `rollElapsed` agrees with the tick count. |
| TC-06-14 | 9 hits from `x1` / `x2` / `x3`, damage 90, locks 21/21/25 ticks, longest 0.50 s, first walk 1293. Observation only. |
| TC-07-04 | 0.10 → G G G P, 0.15 → G G P P, 0.20 → G P P P. Each RB press is linked: (contact − press) equals `rbToHit`. Every parry record in a run carries that run's window. |
| Other TCs | Pass with the added links. These include: the armor target id; the dodge enemy HP from the per-tick field; player and enemy attack ids; attacks during the vanish window; +1 per later hit; the rear attacker really behind; the guard rule field; the air parry tick in the air with RB before it; the counter hit on `pp`; guard-break damage 1; one RB press inside each allowed cancel. |

Self-test:
- Main: 36 mutations, all rejected. These are the 17 from WP-08, the 16 GPT counterexamples on the main log, and 3 R1 cases.
- TC-07-04: 2 mutations, including the GPT 0.01-window counterexample.

## Corrections (GPT C01 / C02)

- **C01:** In the WP-08 report, TC-06-14 named 1314 as the first walk. That came from the superseded main. In the WP-08 main it was walk start 1303, re-hit 1314, last hit 1318, walk again 1340. In this R1 main the first walk is 1293. Lock, walk attempt and escape are reported separately.
- **C02:** TC-07-05's 0.8 s is the configured post-parry stun. In the Play run, the player's follow-up hit refreshes the enemy's stagger 4 ticks later. The full 40-tick stun without a follow-up is shown only by the Core test (and GPT's Core check).
