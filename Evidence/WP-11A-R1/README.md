# WP-11A-R1 evidence: GPT WP-11A review (four TCs left INSUFFICIENT_EVIDENCE, ten checker cases)

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-11A-R1_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Code: `2c3112c` (Core diagnostics, views, GameHost test commands, scene `WP11_R1`, EditMode tests, checkers); `e2d4560` adds `Tools/gpt11a_replay.py` only.
- EditMode on `2c3112c` (clean): 315/315 (`EV-WP11AR1-A01_EditMode_TestResults.xml`; new `Wp11R1Tests` 4).
- Runs on `2c3112c`, both clean (472/472 rows, `checks/manifest_validation.txt`), video 50 fps:
  - `RUN-20261113-231444` main (`tools/wp11r1_main.txt`)
  - `RUN-20261113-231921` fault control (`tools/wp11r1_fault.txt`): the drawn player and enemy bodies are shifted 0.3 m by the test command `bodyfault`. The judged bodies are not moved.

## What changed in the build (diagnostics and test hooks only)

- `hit.candidates`: per hit-resolution tick, the player candidates (`instance:target:hitEvent`) and the enemy candidates, logged before Dodge pairing and application.
- External hit signals (`hit.signal.received` / `hit.signal.resolved`) model a collision callback that reaches the core late. The core judges each one from its own state, in this order:
  - **no instance:** the signal names no attack.
  - **stale:** the attack ended or was cancelled, with how.
  - **not active:** the attack is in startup or recovery.
  - **no overlap.**
  - **same as collected:** accepted.
  - **already hit this event.**
  
  A signal is never applied as a hit (`appliedAsHit=false`). Script command: `hitsignal=current|last:<target>`.
- `view.bodyCheck` reads the rendered `MeshRenderer.bounds` after the view is applied.
  - It compares the player with the judged box (lying when dead). The rendered feet are checked against the segment between the previous and current tick's judged feet.
  - It compares enemies with the judged body, or with the low pose for Down / Dead, on the judged feet.
  - Values are per class, plus the frames over 1 mm, with their ticks.

## Results (`checks/wp11r1_checks.json`, all four PASS; self-test 18/18 rejected)

| TC | What the runs show |
|---|---|
| TC-08-02 | The player enters, leaves, then re-enters range while the chaser is in each state: AttackRecovery (tick 341), Idle within its re-attack wait (616), Stagger (864), Down (1187). No attack starts while restricted. After Recovery and Down, the re-decision is a logged `Cooldown:ReattackInterval`, and the attack starts when it runs out (411, 1366). In the wait case, the attack starts at the logged cooldown end (646). After Stagger, it starts on the first Idle tick (893). |
| TC-08-12 | The gauntlet boss in g1 / g2 (super armor) / g3 (launch immune) is parried with the player airborne. The player is not grounded on the parry tick or the tick before, y is 1.70, and there is no landing since the jump. Each parry names the boss's active attack instance. The boss is in Stagger with a single-source Parry knockback of 0.66 m, and that attack does not recover. |
| TC-UNITY-02 | Main run: every player class (walkRun, air, airDash, dive, knockback, hitStun) and every enemy class (walk, knockback, air, down, dead) is 0 for wall penetration, size and outside-the-tick-segment, with 0 excess frames. Fault run: every class shows the 0.3 m shift (outside 0.30; dive 0.29), and the player's drawn body enters the wall by 0.30 m, while the judged side stays at 0. Plane check: one plane, error 0. |
| TC-UNITY-07 | All 7 player hits link to a candidate collected on their own tick, and no candidate is left unresolved. All 29 late signals have the verdict the log's own state gives, and none was applied. Every verdict kind occurs. Cast 30 gets a startup verdict before the cancel (tick 3490) and a `StaleInstance:Cancelled:PlayerHit` verdict after it (3521). |

## Checker fixes for the WP-11A run (`checks/wp11_checks_WP11A_main.json`; all seven TCs still PASS, 48/48 rejected)

GPT's ten cases are self-tests now, and `checks/gpt_wp11a_probes_replay.json` runs GPT's own `probes.py` edits: 10/10 rejected.

| # | TC | New rule |
|---|---|---|
| 1 | 05-04 | The attack accepted after the stun carries the after-press's input id. |
| 2 | 08-02 | A re-decision block must be `Cooldown:ReattackInterval`, with cooldown > 0 and the player in range. |
| 3 | 08-05 | Each air-attack decision has that enemy's `AirAttack` startup (same or next tick), and that instance becomes active. |
| 4 | 08-09 | The boss's phase at the end equals its last transition's target. |
| 5 | 08-12 | A hit's hpAfter < hpBefore, and the boss shows hpAfter on that tick. |
| 6, 7 | 08-12, U-02 | A knockback record's displacement = endX - startX, and its sign is the start velocity's. startX / endX match the logged positions (±0.0051). |
| 8 | U-02 | Both `view.planeCheck` records must be present with zero depth error on one plane. |
| 9 | U-07 | A timed hit lies in its attack's active window from the run data (motion progress), and its tick count agrees with that progress. |
| 10 | U-07 | The cast cancel is on the hit tick, in the order `attack.end` < `magic.cancel` < that tick's `hit.player`, and names the hit as its cause. |
