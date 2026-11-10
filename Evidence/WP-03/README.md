# WP-03 evidence (P0-02 ring inventory and quickslots, R-05-05 ring reservation)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-03_Report_Claude_v1.0.md`.
- Run source: `37929b8`, clean tree (recorded in the manifest with a per-file source list).
- Scene: `Assets/Corum2/Scenes/WP03_Ring.unity`, on the WP-02 arena map C01.
- Data: `ring.config@wp03-1`, `input.profile@wp03-1`.
- All items and numbers are test fixtures (TEST-ITEM-01). Undecided rules are experimental (`EXP-RING-*`, `EXP-ITEM-*`, `EXP-QS-*`).

## Contents

- `runs/RUN-20261110-122933.*` — the main Play run:
  - NDJSON log
  - data snapshot
  - manifest
  - `source_files.tsv`
  - `commands.txt`
  - `report.txt` — every ring / item / quickslot / time-scale event, made with `Tools/wp03_log_report.py`
- How the run was driven:
  - Input goes through the Input System virtual gamepad: stick, A / B / X / Y, LB, LT / RT, D-pad.
  - The last section (TC-02-01) uses the virtual keyboard: Q ring, W cursor, C page, J detail, E discard, Space use.
  - No physical device or Steam Deck was used.
- `video/main-RUN-20261110-122933.mp4` — 1889 frames, 1280x800, recorded at 50 fps in game time.
  - Under `Time.captureFramerate=50`, a rendered frame advances 0.02 s × `Time.timeScale` of game time.
  - So while the ring is open (x0.2) each simulation tick spans 5 frames. The world moves slowly; the ring UI updates every frame.
  - `video/*_stills` holds a JPEG every 50 frames. Examples:
    - f00250 — description panel
    - f00550 — registration
    - f00900 — discard prompt
    - f01650 — pending ring use during HitStun
- `checks/run_checks.json` — `tools/run_checks.py` applied to the log:
  - Slow motion: 1890 frames are expected from the logged time-scale changes; the recorder logged 1889. 155 ticks had the ring open.
  - Player state at every ring open: Walk, Idle, Airborne, GroundAttack, AirDash, Dive, HitStun.
  - Player motion while the ring was open: the game's held direction is `None` throughout. Position changes only during dash, dive and knockback, which were already running.
  - Close reasons, item uses (request and execution tick, wait reason), rejections, quickslot assignments, discards, ignored ring inputs, magic requests.
- `validation/validate_results.ndjson` — `Tools/validate_manifest.py --repo .` on the run manifest: valid, and 241 source rows match the commit.
- `EV-WP03-A01_*` — EditMode 167/167: 150 earlier tests plus 17 `RingTests`.
- `evidence_steps_wp03.sh`, `evidence_helpers.sh` — the driving script. Script times are game seconds.

## Script sections (test case marker in the log)

| TC | What the script does |
|---|---|
| TC-02-03 | Walk right with the stick and open the ring with LB. The world slows to x0.2. Close; speed returns to 1. |
| TC-02-04 | Stick right selects Herb, up selects Potion, neutral clears the selection. A at neutral uses nothing. |
| TC-02-05 | RT, RT (wraps), LT, RT change page. X opens the description, X closes it. Close with the stick still held; walking resumes. |
| TC-02-07 / TC-UI-12 | In browse, the D-pad is ignored. Then select, Y, stick neutral, D-pad. Four registrations: Potion↑, Hi-Potion→, Herb↓, Bread←. No item is consumed. |
| TC-02-09 | Y, then B cancels the registration; the ring stays open. Close with LB. |
| TC-02-06 | Each quickslot is used once. Hi-Potion goes 1→0 and its slot is cleared; pressing it again → EmptySlot. Bread a second time → rejected by cooldown. |
| TC-02-08 | Potion: B, B cancels; the ring stays open. Old Key: B is blocked (quest item); the ring stays open. Water: B, A discards; 1→0 and the ring closes. |
| TC-02-10 | Fire Scroll used twice. `magic.request` uses the shared definition (power 30, cast 0.2). 2→1→0, then the scroll is removed from the ring. |
| TC-02-11 | Ring opened in Airborne, GroundAttack (Apple use waits for the attack's end), AirDash and Dive. |
| TC-05-05 | During HitStun: X is rejected and LB opens the ring. Potion → `Reserved:HitReaction`; the ring closes. Heal 90→100 after the hit reaction ends. |
| TC-CF-08a (EXP) | A quickslot press during HitStun is rejected (EXP-QS-01). A ring use is reserved; a second request → `PendingExists`, and the ring stays open. |
| TC-02-01 (partial) | The same ring actions through the virtual keyboard. |

Claude self-check artifacts, not GPT verdicts.
