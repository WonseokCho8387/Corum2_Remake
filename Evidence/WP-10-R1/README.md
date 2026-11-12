# WP-10-R1 evidence: GPT WP-10 review

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R2_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Runs on `7a9ca55`. That build includes the knockback fix, the double-lethal fix and the R2 diagnostics.
- The checker in `tools/` is from `eff42f7`.
- EditMode: 307/307.
- Manifests: 412/412 rows each, all clean.

| Run | Scene | What |
|---|---|---|
| main `RUN-20261113-054149` | WP10_Integration | INT-01, 03, 04 (+ R1 weak magic), 05, 06 (+ R1 sentinel s2) |
| int07 `RUN-20261113-054514` | WP03_Ring | INT-07 |
| int08 `RUN-20261113-054756` | WP05_MagicDeath | INT-08 |

## Data (`7a9ca55`)

- `Enemy_WardenWeakP2`: a copy of the slow-p2 warden with `magicKnockbackScale` 0.1 (test data), placed at zone I4ArmorD.
- Spawn `I6SnRange`: 7 m left of the sentinel's start.

## Results (`checks/wp10_checks.json`)

**INT-04**, all in the boss's super-armor phase (p2):
- **Physical:** SuperArmor:DamageOnly, and the boss's attack continues.
- **Magic (wb3, knockback scale 1):** MagicBreaksArmor and Stagger. Knockback 5 m/s moves it 0.468 m (SingleSource).
- **R1 weak magic (wb5, knockback scale 0.1):** MagicBreaksArmor and Stagger. Knockback 0.5 m/s moves it 0.010 m (SingleSource), on the current build.
- **Simultaneous:** Dodge with wb4, which was attacking at the time. Damage 0, armor true.

**INT-06.** Each phase transition is followed by its presentation end, then a pattern of the new phase that actually attacks:

| Boss, phase | Transition | Presentation end | Pattern |
|---|---|---|---|
| wb2 p2 | 1959 | 2009 | shot2 |
| **sn s2** | **2059** | **2109** | **bolt2, fired** |
| wb2 p3 | 2178 | 2228 | rush |

**INT-01, 03, 05, 07** pass with the links GPT asked for:
- INT-01: every counted hit does damage, and the 10 attacks are the declared chain.
- INT-03: the reservation's input id is the press that starts the landing attack.
- INT-05: at least one RB press resolves as a guard or parry (here, 1 parry).
- INT-07: the registered item is the item fixed by Y, and a discard removes exactly one.

**INT-08:**
- `status` is BLOCKED_SPEC (U-08, TC-06-19b).
- `observedPartsPass` is true:
  - the wakeup refusals name the enemy that is waking;
  - the feather was owned (1 → 0);
  - after the revive, the revive invulnerability refuses an enemy hit.

Self-test: 25 mutations, all rejected. These are 7 existing cases, 14 from GPT and 4 R1 cases. For INT-08, the self-test checks `observedPartsPass`.

The INT-05 lock time (0.62 s) and INT-06 readability are observation items for the user.
