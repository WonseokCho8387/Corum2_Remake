# WP-11A evidence: GPT R2 ledger gaps, gameplay and Unity view/lifecycle

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-11A_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Run on `e088c32` (clean). EditMode on that commit: 311/311 (`EV-WP11A-A01_EditMode_TestResults.xml`; new `Wp11Tests` 4).
- Manifest: 440/440 rows match git (`validation/`).
- Scene `WP11_Evidence` (built by `Wp11ContentBuilder`, test data `wp11-1`), virtual keyboard and pad, video at 50 fps (one frame per tick).

| File | What |
|---|---|
| `runs/RUN-20261113-111800.*` | the main run (log, manifest, source list, commands) |
| `video/main-RUN-20261113-111800.mp4` | its video |
| `checks/wp11_checks_main.json` | `tools/wp11_checks.py --selftest` on that log |
| `tools/` | checker, script generator, runner, record steps, the exact input script |
| `control/` | pre-fix trial `RUN-20261113-110356` (commit before `e088c32`'s spawn fix): TC-UNITY-02 fails on it (drawn player 0.27 m inside a solid on the spawn frame) |

## Results (all PASS; self-test 38/38 rejected)

**TC-05-04** (keyboard and pad): after a hitter hit (HitStun 21 ticks), Jump / Attack / Special / Guard pressed during the stun are each refused as `ActionLock:HitStun`, linked by input id, and nothing of that action starts during the stun; the same press after `HitStunEnd` executes. A held Left changes nothing during the stun (x equals the no-input control tick by tick) and walks after it. LB opens the ring during the stun and after it.

**TC-08-02** (still chaser, range 1.6, re-attack interval 1.2 s; the player walks in):
- every attack starts from Idle with the player in range; the first starts on the first tick in range;
- in range during AttackStartup/Active/Recovery, Stagger, Launched, Down, Wakeup and Dead: no attack starts;
- on the first Idle tick after each restriction the enemy re-decides: an attack, or a logged `Cooldown` block followed by an attack when the interval runs out;
- dead: the player leaves and re-enters the range while the body is shown; no attack.

**TC-08-05** (two jumper configurations, D from the data): window [3,5] at 4 m and [5,7] at 6 m jump (`JumpWindow[min,max]`), at the configured horizontal speed (5 / 7 m/s measured); outside each window (6 m / 4 m) no jump. Apex ratio 2.33 vs (9/6)² = 2.25. The near jumper reaches 2.2 m and air-attacks; the far one never comes closer than 3.34 m and does not.

**TC-08-09**: boss A (1 phase) at 10 % HP: no transition. Boss B (2 phases): one transition at `HpBelow:0.5`. Boss C (warden, 3 phases): `HpBelow:0.6`, then `PatternUsed:crushx2` after exactly two crushes. Thresholds differ per boss.

**TC-08-12** (gauntlet test boss; state set by events): 

| | Physical | Magic | Parry | Simultaneous | Launcher |
|---|---|---|---|---|---|
| g1 normal | Stagger, attack cancelled | MagicStagger + knockback | parry, knockback, Stagger | Dodge both, HP kept | Launched |
| g2 super armor | DamageOnly, attack continues | MagicBreaksArmor, cancelled | same | same (armor true) | DamageOnly |
| g3 launch immune | Stagger | MagicStagger + knockback | same | same | LaunchImmune, Stagger |

Knockback records use the R3 attribution rule (SingleSource, `Hit:magic.fire` / `Parry`, the case's instance).

**TC-UNITY-02**: `view.trackCheck` (new, `ViewTrack`) compares every frame's drawn body with the judged body. For the player (walk/run, air, air dash, dive, knockback, …) and enemies (air, knockback, walk, still): penetration into solids 0, size difference 0, distance outside the segment between the last two tick positions 0. Walls: run, air dash and dive stop at the wall (x = 864.6999, contact in that motion); knockback into a wall stops at it; magic knockback moves a bag 0.1 m and stops it at the wall (0.468 m when free), and the record says `blocked: true`, `Ambiguous` (positive control for the R2 flag).

**TC-UNITY-07**: over the whole run, all 25 player hits fall inside their attack's start..end, no (attack, target, hit event) repeats, every `attack.end` counts exactly its hits, every skip is inside its attack, and all 78 enemy hits come from that enemy's active instance. One motion hits four bags once each; a cast hit by the hitter mid-cast ends on that tick (`hitboxRemoved`, `magic.cancel effectApplied false`) and never applies; a bag is skipped as Down, skipped as Wakeup (invincible), then hit when Idle.

## Fix found by this run (in `e088c32`)

The `spawn` test command moved the player outside `FixedUpdate`, so the next tick interpolated between the old and new place and drew the body for one frame on that line (here through a wall). A placement now resets the interpolation, like a map switch. See `control/`.
