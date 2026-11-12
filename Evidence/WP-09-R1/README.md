# WP-09-R1 evidence: fixes for the GPT WP-09 review

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-09-R1_Report_Claude_v1.0.md`
- Review being answered: `CORUM2_WP-09_Review_GPT_v1.0.md`
  - M01: moving-enemy depth samples misclassified; plane evidence for the remaining directions
  - M02: checker passed 12 counterexamples
  - TC-02-01: missing device pairs (quickslots up / left / right, B on a selected item, real page turns)
  - TC-03-04: no left-facing ↓+A Play evidence
- These are Claude self-check artifacts, not GPT verdicts.

## Source

Commit `de757b8`. All four runs use the clean tree. Manifest validation: 406/406 rows match git for each run.

Diagnostics and tests in `de757b8` (no simulation rule changed):
- **`CombatView` (WP09-M01):** each rendered enemy sample falls in exactly one class. The class comes from the rendered root's own x movement since the previous frame:
  - first (no previous frame);
  - airborne (not grounded);
  - teleport (grounded, moved ≥ 1 m: test placement);
  - ground-moving (grounded, 0 < |dx| < 1 m);
  - ground-still (grounded, dx = 0).
- It also counts propelled samples (|MoveVelX| or |VelX| > 0.01) and boss moving samples (boss samples that are airborne or ground-moving), and keeps the largest |z − planeZ| per class.
- **`GameHost`:** player frames and the largest |z − planeZ| for each of the 9 input directions (`Dir8`, including None) and while airborne.
- **`Wp09R1Tests`:**
  - a ground chaser with VelX 0 and MoveVelX −3 counts as ground-moving (the WP-09 predicate missed it);
  - controls: a standing enemy is ground-still, and a 5 m placement is a teleport.
- **`Tools/wp09_checks.py`:** rewritten (details under Self-test).

EditMode on `de757b8` (clean): 295 / 295 (`EV-WP09R1-A01_EditMode_TestResults.xml`).

## Runs

| Run | Scene | What | Video |
|---|---|---|---|
| main `RUN-20261112-234229` | WP06_ComboState | WP-09 main script unchanged: TC-02-01 part 1, TC-03-01, TC-03-06..09 | `video/r1main-…mp4` |
| plane `RUN-20261112-234529` | WP04_AI | TC-01-01: per AI zone, the WP-09 pattern plus all 8 stick directions, jumps holding up / up-left / up-right, keyboard diagonals | `video/r1plane-…mp4` |
| dev `RUN-20261112-235402` | WP03_Ring (2 pages) | TC-02-01 part 2: each virtual device registers and uses quickslots up / right / down / left, turns pages, discards a selected item with B | `video/r1dev-…mp4` |
| slide `RUN-20261112-235043` | WP01_MapMovement | TC-03-04 left: ↓+A facing left into the low passage, keyboard then pad | `video/r1slide-…mp4` |

- A first dev recording (`RUN-20261112-234808`) was discarded. With the corrected quickslot spacing, all four keyboard uses succeeded, and the inventory fell to 6 stacks (one ring page). The pad pass then had no second page and an empty up-left slot.
- The kept run restores the start counts before the pad pass. This is logged as `TestSetItemCount`.

## Results (`checks/`)

| TC | Result |
|---|---|
| TC-01-01 (M01) | **Player:** 2128 rendered frames, max \|z − planeZ\| 0. Every input direction was rendered: None 1128, Up 75, UpRight 150, Right 200, DownRight 75, Down 75, DownLeft 75, Left 200, UpLeft 150 (sum = frames), each with max \|z\| 0. 865 airborne frames, max \|z\| 0, 20 jumps. **Enemies:** 38,304 samples = first 18 + airborne 29 + teleport 0 + ground-moving 498 + ground-still 37,759. Max \|z\| is 0 in every class. Boss: 4,256 samples, 233 moving. Propelled: 531. **Simulation cross-check:** 1996 continuous ticks, all planeZ 0, equal to both view records. Ground x changes per enemy: ch 114, fl 108, jp 41, jq 2, boss 235. jp airborne on 28 ticks; 1 AI jump. |
| TC-02-01 part 1 | Each press is linked to its own result. Jump, attack and special by `inputEventId`. Guard, move, ring open/close, LT, RT and B by the tick window [press, press+1]. The quickslot by request id: one request, one use. No result serves two presses, and keyboard and pad give the same sequence. B is the real `ring.ignore NoSelection` (no fallback). |
| TC-02-01 part 2 | **Keyboard:** Q, then W / D / S+A / A, then K, release, then 1 / 2 / 3 / 4 registers potion / herb / bread / water to up / right / down / left. 1–4 outside the ring use each one (heal 20 / 5 / 10 / 8). C turns page 0→1 (2 pages), Z turns 1→0. W+A then E asks to discard elixir; Space discards it (1→0) and the ring closes. **Pad:** the same with LB, stick, Y, release, D-pad (left = apple), RT/LT 0→1→0, and B then A discards meat. Every request id is unique. |
| TC-03-04 left | **Keyboard:** facing −1, a Space press with Down held slides from x 43.89 to 40.65 and ends with `SlideEnd:CeilingKeepsCrouch` under the ceiling (x 40–42). The next slide ends at 37.41, out of the passage. Facing stays −1 throughout. **Pad:** same ticks and positions with the A button. The right-facing pass is from WP-01-R1. |
| TC-03-01 | Strong tilt: Walk only. Double tap: Run inside armed..release. After release, no Run tick and no speed above walk. |
| TC-03-06 | Each air ability is linked to its own press by `inputEventId`, with the player airborne the tick before. The dash's press holds Right; the dive's press holds a direction (Right). The air double tap is ignored in the air. Air RB enters Guard. |
| TC-03-07 | Uses and AlreadyUsed rejects all fall between the jump and its landing: dash 1482 / jump 1498 / rejects 1507, 1516, landing 1538; then 1583 / 1594 / 1613, landing 1630. The counter chain is continuous. The air normal attack happens before both uses, in the same airtime. |
| TC-03-08 | Jump and attack are refused with `ActionLock:Dive` between the dive start and its landing. Each refusal is linked to its own press. |
| TC-03-09 | Each action starts 1 tick after landing (1904→1905, 1971→1972, 2064→2065). `airBefore` equals the last use's state (`jump=used` after an extra jump; free in the guard case). `airAfter` is free. |

## Self-test

All mutations are rejected:
- **main:** 17. These are 6 WP-09, the 9 GPT main-log counterexamples, and 2 R1 cases.
- **plane:** 11. These include the 3 GPT plane counterexamples (no ticks, enemy plane differs, boss > all).
- **dev:** 8.
- **slide:** 4.

`wp09_checks_WP09main_GPTcounterexamples.json` runs the R1 checker on the original WP-09 main log (`152613`). All sections pass there, and all 17 mutations are rejected.

## Limits

- Virtual devices only. Real keyboard, pad and Steam Deck are BLOCKED_ENV.
- `teleportDx` = 1 m is a diagnostic threshold, not a game rule. No enemy moved ≥ 1 m between frames in the plane run.
- `propelled` (531) and ground-moving (498) count different things. Several frames can share one tick, and only the first of them shows movement.
