# WP-04-R3 evidence: boss transition presentation (WP04-M01 remainder, R-08-11)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-04-R3_Report_Claude_v1.0.md`. Review answered: `CORUM2_WP-04-R2_Review_GPT_v1.0.md`, section 6.
Earlier evidence (`Evidence/WP-04`, `-R1`, `-R2`) is kept unchanged.

**Source.** The run source is `3a5429d`, with a clean tree. The manifest is valid, and its 297 source rows match git at `3a5429d`.

**Tools.** `Tools/wp04_presentation_checks.py` was written after the run and is committed with this evidence. It reads the log only. A copy is in `tools/`.

**Scene and content.** Content is `wp04-3`. Two things changed from `wp04-2`:
- `Data/WP04/PresentationLibrary.asset` was added.
- The data version strings were bumped.

Boss numbers are unchanged. The Play script is the same as in R1 and R2.

## What is implemented

- **Cue library.** `PresentationLibrary` holds temporary procedural cues (TEST-PRES-01) for each id used by the boss phases:

  | Id | Kind | Cue |
  |---|---|---|
  | `warden.roar` | animation | pulse |
  | `warden.rage` | animation | shake |
  | `sentinel.shift` | animation | flicker |
  | `vfx.armorGlow` | VFX | aura |
  | `vfx.redAura` | VFX | burst |
  | `vfx.blink` | VFX | burst |
  | `cam.shakeSmall` | camera | shake |
  | `cam.zoomIn` | camera | zoom |
  | `cam.none` | camera | explicit "no change" |

- **Validation.** Every phase id must resolve, and a phase that has ids needs a transition time above 0. Bad cue values are rejected. A failed check stops `GameHost`.
- **Tracker.** `TransitionPresentationTracker` follows the judged transition:
  - It starts on the tick `EnemyActor.TransitionSerial` changes.
  - Progress is `(duration − TransitionTimer) / duration`, so it pauses with the boss's hitstop.
  - It ends on the tick the timer reaches 0, and cancels when the boss dies.
  - It logs `presentation.start`, `presentation.end` and `presentation.cancel`, plus `presentation.skipped` if transitions happen without a rendered frame.
- **View.** `BossPresentationView` plays the cues:
  - Body cue on the block-out body.
  - Particle VFX with a fixed random seed.
  - Camera offset and FOV change on the follow camera. The combat plane z is unchanged.
  - Every cue is scaled by `sin(π·progress)`, so it is 0 at the transition start and end.
- **VFX after the end.** On the end tick, particles stop emitting. Particles already emitted fade out within their lifetime: 0.4–0.6 s, or cleared at once on cancel.

## Contents

- `runs/RUN-20261110-185524.*`: the Play run (log, data snapshot including the library, manifest, source list, commands, `report.txt`).
- `video/main-RUN-20261110-185524.mp4`: 4381 frames, 1280×800, 50 fps game time.
  - Recording starts at the tick-207 marker.
  - The HUD tick in a still is `frame + 208`.
  - PNG stills every 50 frames.
- `checks/presentation_checks.json`: `Tools/wp04_presentation_checks.py`. Each judged transition is matched to its playback.
- `checks/transition_checks.json`: `Tools/wp04_transition_checks.py --selftest`. Transitions are complete with no action before the end. Defense has 8 decisions and 1 skip. The self-test passes.
- `checks/run_checks.json`: the WP-04 per-TC checks on this log.
- `validation/validate_results.ndjson`
- `EV-WP04R3-A01_EditMode_TestResults.xml` and `_summary.txt`: EditMode 230/230, which is 223 earlier tests plus 7 `AiR3PresentationTests`.

## Presentation vs judged transitions (`checks/presentation_checks.json`, `all_pass` true)

| Boss | Transition (start→end tick, frames) | Presentation start / stop | Cues | Stills |
|---|---|---|---|---|
| warden | p1→p2 2796→2855 (f2589–2648) | 2796 / 2855 end | roar pulse, armorGlow aura, shakeSmall | f02600 |
| warden | p2→p3 2980→3030 (f2773–2823) | 2980 / 3030 end | rage shake, redAura burst, zoomIn | f02800 |
| warden | p3→p1 3221→3222 | 3221 / 3222 end | none (p1 has no ids, time 0) | — |
| warden | p1→p2 3222→3272 (f3015–3065) | 3222 / 3272 end (previous ended the same tick) | roar, armorGlow, shakeSmall | f03050 |
| warden | p2→p3 3371→3421 (f3164–3214) | 3371 / 3421 end | rage, redAura, zoomIn | f03200 |
| sentinel | s1→s2 3796→3846 (f3589–3639) | 3796 / 3846 end | shift flicker, blink burst, cam.none | f03600 |

In the first row (p1→p2), the warden is hit three times during the transition. The presentation keeps playing through the hits (EXP-BOSS-03) and ends with the judged `TransitionDoneDuringReaction`.

## Not shown in this Play run

These are EditMode only:
- **Death during a transition:** the presentation is cancelled.
- **Progress during hitstop:** the pause is checked tick by tick.
- **Missing or bad cue data:** rejected by validation.

## Limits

- Final models, rigs, animation lengths, particle textures and camera tuning are U-11 and U-06.
- The cues are temporary resources: they verify function and timing, not the final look.
- Playing the cue only once under re-entry or contention is CF-09.

Claude self-check artifacts, not GPT verdicts.
