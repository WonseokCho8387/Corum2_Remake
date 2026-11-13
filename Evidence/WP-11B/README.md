# WP-11B evidence: TC-OPS-01, TC-UNITY-03, TC-UNITY-05 (observation), TC-UNITY-06, TC-UNITY-08 map

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-11B_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Runs on `5de850e` (scene `WP11_Ops`, `Wp11ContentBuilder.BuildOps`, GameHost time hooks). EditMode on that commit (clean): 311/311.
- The checker in `tools/` is from `fccad72`, written after the recording (see "Checker after recording").
- Manifests: base, base2, s50, s60 clean (453/453 rows). v1..v5 each deliberately dirty: 452 rows plus one patched asset (`runs/*.dirty.patch`), restored from git and reimported after the run.
- `runs/RUN_NAMES.txt` maps run names to run ids.

| Run | Change from baseline | Script |
|---|---|---|
| base, base2 | none (baseline and rerun) | `tools/wp11b_main.txt` |
| v1 | `AirUnlocks_Ops.extraJump` 1 → 0 | same |
| v2 | `AirUnlocks_Ops.airDash` 1 → 0 | same |
| v3 | `CombatUnlocks_Ops.dashPierce` 0 → 1 | same |
| v4 | `CombatUnlocks_Ops.diveArea` 0 → 1 | same |
| v5 | `CombatConfig_Ops.comboWindow` 0.5 → 0.3 | same |
| s50 | none (fixed step 0.02, video 50 fps) | `tools/wp11c_50.txt` |
| s60 | test hook `fixeddt=0.0166667` (video 60 fps), restored at the end | `tools/wp11c_60.txt` |

The WP11_Ops fixtures are this scene's own copies, so a variant touches no other scene.

## TC-OPS-01 (`checks/wp11b_checks_ops.json`, PASS; self-test 9/9 rejected)

Every zone of every run is reduced to its simulation records, with ticks relative to the zone marker. Wall-clock fields and run-wide counters (chain / attack ids) are left out.

| Variant | Zones that changed | What changed |
|---|---|---|
| v1 extraJump 0 | O1Jump | the air jump is refused `Locked:ExtraJump` |
| v2 airDash 0 | O2Dash, O3Pierce | both dashes refused `Locked:AirDash`; no dash hits |
| v3 dashPierce 1 | O3Pierce | the dash hits pa, pb, pc (baseline: pa, then stops) |
| v4 diveArea 1 | O4Dive | the dive hits da, db, dc (baseline: da) |
| v5 comboWindow 0.3 | O5Combo | the attack 19 ticks after the first ends starts a new chain (baseline: ground2 in the same chain) |

- Every other zone is identical to the baseline, record by record.
- For v5, three other zones differ only in their chain records (`combo.*`, the tick combo count): the shorter window ends those chains sooner. Their positions, states, attacks and hits are identical.
- The runs report the configured value themselves: `upgrade pierce=True` / `area=True` on the dash / dive, and `combo.window` 0.3.
- Each variant's patch is exactly one line in the expected asset.

## TC-UNITY-03 (PASS)

- **Inspector change → load → D:** each variant's manifest carries its one-line patch (field, old and new value, file), and the run's own records show the new value in use.
- **Runtime isolation:** two instances of one bag definition (O6Twins). Only `ta` is hit; `tb` keeps full HP and Idle on every tick, in all seven runs.
- **Rerun:** base and base2 are identical in every zone.
- These runs are tagged `TC-OPS-01` in the log; TC-UNITY-03 is checked on the same runs.

## TC-UNITY-05 (status OBSERVED; policy BLOCKED_SPEC U-06)

`frame.sample` per rendered frame (test hook `framelog`). Observation only, no policy verdict.

| Phase | 50 Hz | 60 Hz |
|---|---|---|
| Normal | 1 tick per frame | 1 tick per frame |
| Ring slow (×0.2) | 0 or 1 tick per frame | same |
| Stop (`pause=25`, timeScale 0) | 25 frames with 0 ticks; a jump pressed on the stop frame is delivered on the first tick after it | same |
| Delayed frame (`framedt=0.1`) | 5 ticks in one frame | 6 ticks in one frame |

- The same ground attack lasts 15 ticks / 0.26 s at 50 Hz and 18 ticks / 0.2667 s at 60 Hz (timers quantized to ticks).
- The simulation cost per tick is in the JSON (mean about 0.2–0.5 ms in this editor).

## TC-UNITY-06 (PASS; self-test of the time checks 7/7 rejected)

At both rates:
- every routed press / release has one owner (game / ring) and a request time;
- every game-bound edge is delivered exactly once, on or after its routing tick;
- every allocated input id is accounted for (an edge or a stick sample group).

Cases:
- a press and release inside one 5–6-tick frame are both delivered on the same tick, and the attack starts once;
- a key held across frames gives one press and one release.

On the frame the ring opens, an attack pressed at the same time goes to the ring and its release to the game. That contention order is CF-01/07/08, so it is recorded, not judged.

## TC-UNITY-08 map (`map/`)

`tools/p0_play_map.py` lists, for each of the 108 master and appendix TCs, the PlayMode runs whose records carry that test case id. For each run it gives the scene, revision and clean/dirty state, next to the R2 GPT ledger status.

- 91 TCs have PlayMode runs.
- 17 have none. These are document / EditMode / environment / decision items, and TC-UNITY-08 itself: TC-00-01/02/03, 02-04, 04-10, 06-19b, CF-01a–d, OPS-02, REG-01, UI-12, UNITY-01/04/08/09. They stay as they are in the GPT ledger.

## Checker after recording (`fccad72`)

The first check of these runs failed in every zone, on fields that are not behaviour:
- absolute delivery ticks;
- wall-clock `pressTime`;
- run-wide chain and attack ids, which shift when an earlier zone changes.

The checker now drops those fields and makes ticks zone-relative. It checks the config-echo records on their own, and allows the combo window's chain-only effect in other zones. Two self-tests guard that allowance: a hit moved in another zone, and the base window left in the v5 run. Both are rejected. No run was re-recorded.
