# R5 evidence: checker fixes for GPT's WP-09-R3 and WP-11B reviews, TC-UNITY-08 map with result links

- Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_R5_Report_Claude_v1.0.md`
- Claude self-check artifacts, not GPT verdicts.
- Tools: `bc2bbaa` (copies in `tools/`). No game code changed, so there are no new Play runs or EditMode runs. The checks re-read the existing runs: `Evidence/WP-09-R1/runs` and `Evidence/WP-11B/runs`.

## WP-09 checker (GPT WP-09-R3 review section 3)

| Rule | Change |
|---|---|
| Ring release | The checker now uses `RingConfig.cursorDeadzone` from the run's data, not `input.stickDeadzone`. As in `Dir8Util.FromStick`, a stick is neutral only when its length is below the deadzone. It needs exactly one release per registering session for the fixed target. Tilting the stick again after the release no longer undoes it, matching the Core's `ReleasedSinceRegister`. |
| NextTick wait | A non-empty `waitedFor` must equal the last `item.wait` state the core logged for that request, between the request and the use. |
| Discard snapshot | The snapshot must be an inventory string; `null` is rejected. If the next snapshot is itself a change to that item, it must start from the discard's `countAfter`. |
| Slide input | The Jump that starts each slide must have `deliveredTick` equal to the slide's start tick. The step limit uses the run's own fixed step. |
| Run data | `<run>.data.json` is used only if its runId equals the log's. If a manifest sits beside it, its SHA-256 must also equal `data_snapshot_sha256`. Otherwise the config-dependent rules fail. |

Results:
- All four runs still pass: `checks/wp09_checks_{main,plane,dev,slide}.json`.
- Self-tests: 60 of 60 rejected.
- GPT's R3 probes (`checks/gpt_wp09r3_probes_replay.json`): all 8 cases now match the Core's verdict. The 5 earlier false passes are rejected and the 3 earlier wrong rejections pass. In the 8 data-file cases, a missing file, a file with another runId, or a file edited after the manifest are all rejected; the run's own file passes.
- Earlier GPT replays: 21 of 21 (R1) and 11 of 11 (R2) give the expected result.

## WP-11B checker (GPT WP-11B review section 6)

| Rule | Change |
|---|---|
| Ids | Ids that count across the whole run are renumbered by first appearance within each zone, instead of being dropped. This covers attack instance, chain, hit event (`instance:n`) and input ids. A hit must therefore point to the same attack as in the baseline. |
| Combo window (v5) | In zones outside the combo zone, only Timeout ends may move, and only by the window difference (10 ticks). The tick combo count may differ only between the two Timeout ticks. Every other combo record must match. |
| Air unlocks | v1 must be refused with `Locked:ExtraJump`, and v2 with `Locked:AirDash` in both its zones. |
| Twins | ta and tb must each appear exactly once on every tick. Every hit must land on ta and lower its HP. |
| Routing vs delivery | Each delivered input has exactly one route with the same id, owned by the game. Device, action, edge and requestTime must match. The delivery tick equals the record's tick and is not earlier than the route. Latency equals real time minus request time. The attack in the delayed frame must carry that frame's own Attack press. |
| Frames | Frames must be consecutive, and each frame's ticks must equal its simTick advance. The fixed step must match in every phase. Costs must be finite and not negative. Inside the pause, every frame after the first runs 0 ticks at time scale 0. |

Results:
- The original runs still pass: OPS-01, UNITY-03, UNITY-05 (observation) and UNITY-06 (`checks/wp11b_checks_{ops,time}.json`).
- Self-tests: 27 of 27 rejected.
- GPT's probes (`checks/gpt_wp11b_probes_replay.json`): 12 of 12 rejected. That is its 11 false passes plus the unrelated-id case it already found rejected.

## TC-UNITY-08 map (`map/p0_play_map.{md,json}`, GPT WP-11B review section 5)

Every one of the 108 TCs is now placed by how a PlayMode result links to it. A tag alone is not enough.

| Category | What it means | TCs |
|---|---:|---:|
| Checked (`CHECKED`) | A committed checker result has this TC as a key with pass. The map names the check file, the runs, scene, revision, clean or dirty, and the videos. | 65 |
| Observed (`OBSERVED`) | Early checker output (WP-01 to WP-04) records observations under the TC key but gives no pass. The GPT review named in the ledger column made the judgment. | 9 |
| Cross evidence (`CROSS`) | Another TC's checker key covers it: TC-04-10, through TC-06-18 and TC-INT-01. | 1 |
| Run, expected result not confirmed (`TAGGED_ONLY`) | Tagged runs, with no checker result. | 19 |
| No tag found (`NO_TAG`) | No run carries the tag; judged from EditMode or documents, or not run. | 14 |

- **Legacy manifests:** where a manifest says `<packet>` (WP-01-R1, WP-02), the revision and scene come from that packet's README. The map marks these "(packet README)".
- **GPT column:** status and basis are copied from GPT's latest ledger, `WP-11B_GPT_v1.0/P0_status.md`.
- **Tag-only control** (`checks/p0_map_marker_only_control.json`): a run that is a single TC-OPS-01 marker, with no manifest and no check result, is classed as tag-only.
