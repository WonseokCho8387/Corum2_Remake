#!/usr/bin/env python3
"""WP-04 boss transition presentation checks (R5: WP04-F04/F05/M05, M05-R1-A/B, F05-R1).

Usage:
  python Tools/wp04_presentation_checks.py RUN.ndjson [--selftest]

Judged transitions come from the ordered linking of Tools/wp04_transition_checks.py. Each is matched to presentation
records by actor + judgedStartTick + phase. Per transition:

Lifecycle (M05)
  - exactly one presentation.start, or exactly one presentation.skipped (never shown) - skipped is reported, not a pass;
  - a shown playback has exactly one presentation.end (judged end) or presentation.cancel (judged cancel), same serial;
  - judgedEndTick == judged transition end tick; no cue reported "missing".
Order and timing consistency (M05-R1-A)
  - the stop's eventId is after the start's; the start's eventId is after the judged boss.phase event;
  - start: shownTick == its simulationTick, judgedStartTick <= shownTick, displayDelayTicks == shownTick - judgedStartTick;
  - stop: observedTick == its simulationTick, observedTick >= judgedEndTick, observeDelayTicks == observedTick -
    judgedEndTick, shownTick equals the start's shownTick, judgedStartTick equals the start's;
  - skipped: simulationTick >= judgedEndTick and shownTick == -1.
VFX completeness and clock (M05-R1-B, F05-R1)
  - vfx cue found and shown: at least one presentation.frame with vfxTime >= 0 for that actor+serial, exactly one
    presentation.vfxBegin; ended: exactly one presentation.vfxTail whose elapsedGame and vfxTimeAtEnd equal the
    duration of the judged transition (from the start record), emissionEnabled false and loop false, and exactly one
    presentation.vfxTailEnd with bornAfterEnd == 0; cancelled: no vfxTail / vfxTailEnd;
  - a Burst cue's vfxBegin must hold at least burstCount particles (the start burst fired);
  - no vfxTime >= 0 frame, vfxBegin, vfxTail or vfxTailEnd for a playback without a found VFX cue, and none that belongs
    to no shown playback; no duplicates;
  - between consecutive frames of a playback vfxTime grows exactly as elapsedGame; on a frame one tick after the previous
    one with bossPausedThisTick, vfxTime does not move;
  - every number read must be present and finite.
all_pass is true only if every transition passes and there are no orphans, duplicates, orphanEnd / dropped events,
unmatched transition ends or VFX issues.
"""
import collections
import copy
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wp04_transition_checks as tc  # noqa: E402

EPS = 1e-3
LIFE = ("presentation.start", "presentation.skipped", "presentation.end", "presentation.cancel")
VFX = ("presentation.vfxBegin", "presentation.vfxTail", "presentation.vfxTailEnd")


def num(e, k):
    v = e.get(k)
    if isinstance(v, bool) or not isinstance(v, (int, float)) or (isinstance(v, float) and not math.isfinite(v)):
        return None
    return v


def check(ev):
    ev = sorted(ev, key=lambda e: e["eventId"])
    ts, unmatched = tc.transitions(ev)
    vstart = next((e["simulationTick"] for e in ev if e["type"] == "marker" and e["reason"] == "VideoRecordingStarted"), None)
    by_key = collections.defaultdict(list)
    for e in ev:
        if e["type"] in LIFE:
            by_key[(e["actorId"], e.get("judgedStartTick"), e.get("phase"))].append(e)
    frames = collections.defaultdict(list)
    vfxrec = collections.defaultdict(list)
    for e in ev:
        if e["type"] == "presentation.frame":
            frames[(e["actorId"], e.get("transitionSerial"))].append(e)
        elif e["type"] in VFX:
            vfxrec[(e["actorId"], e.get("transitionSerial"))].append(e)
    used, used_serial = set(), set()
    rows = []
    vfx_issues = []
    hitstop_frames = 0
    vfx_frames = 0

    for t in ts:
        key = (t["actor"], t["start_tick"], t["to"])
        recs = by_key.get(key, [])
        used.add(key)
        starts = [e for e in recs if e["type"] == "presentation.start"]
        skips = [e for e in recs if e["type"] == "presentation.skipped"]
        stops = [e for e in recs if e["type"] in ("presentation.end", "presentation.cancel")]
        p = []
        if len(starts) + len(skips) != 1:
            p.append("start/skipped count %d/%d (expected exactly one)" % (len(starts), len(skips)))
        if len(stops) > 1:
            p.append("duplicate end/cancel x%d" % len(stops))
        serials = {e.get("transitionSerial") for e in recs}
        if len(serials) > 1:
            p.append("mixed transitionSerial %s" % sorted(map(str, serials)))
        serial = next(iter(serials)) if len(serials) == 1 else None
        if not t["complete"]:
            p.append("transition not complete (%s)" % t["status"])
        status = "shown" if starts else "skipped" if skips else "absent"
        dur = None
        if skips:
            p.append("never rendered (skipped)")
            s = skips[0]
            if num(s, "judgedEndTick") is None or s["simulationTick"] < s["judgedEndTick"]:
                p.append("skipped record before the judged end")
            if s.get("shownTick") != -1:
                p.append("skipped record has a shownTick")
        if starts:
            s = starts[0]
            dur = num(s, "duration")
            if dur is None:
                p.append("start duration missing")
            if s["eventId"] <= t["start_eventId"]:
                p.append("start before the judged transition start event")
            sh, js, dd = num(s, "shownTick"), num(s, "judgedStartTick"), num(s, "displayDelayTicks")
            if sh is None or js is None or dd is None:
                p.append("start timing fields missing")
            else:
                if sh != s["simulationTick"]:
                    p.append("shownTick %s != start event tick %s" % (sh, s["simulationTick"]))
                if js > sh:
                    p.append("judgedStartTick after shownTick")
                if dd != sh - js or dd < 0:
                    p.append("displayDelayTicks %s != shownTick - judgedStartTick" % dd)
            for k in ("animation", "vfx", "camera"):
                if s.get(k + "Cue") == "missing":
                    p.append(k + " cue missing")
            if not stops and t["complete"]:
                p.append("no presentation end for a completed transition")
            if stops:
                st = stops[0]
                want = "presentation.cancel" if t["status"] == "cancelled" else "presentation.end"
                if st["type"] != want:
                    p.append("stop type %s, expected %s" % (st["type"], want))
                if st["eventId"] <= s["eventId"]:
                    p.append("stop recorded before the start")
                je, ob, od = num(st, "judgedEndTick"), num(st, "observedTick"), num(st, "observeDelayTicks")
                if je is None or ob is None or od is None:
                    p.append("stop timing fields missing")
                else:
                    if je != t["end_tick"]:
                        p.append("judgedEndTick %s != transition end %s" % (je, t["end_tick"]))
                    if ob != st["simulationTick"]:
                        p.append("observedTick %s != stop event tick %s" % (ob, st["simulationTick"]))
                    if ob < je:
                        p.append("observedTick before the judged end")
                    if od != ob - je:
                        p.append("observeDelayTicks %s != observedTick - judgedEndTick" % od)
                if st.get("shownTick") != s.get("shownTick") or st.get("judgedStartTick") != s.get("judgedStartTick"):
                    p.append("stop and start disagree on shownTick / judgedStartTick")

        # VFX completeness for this playback
        vkey = (t["actor"], serial)
        if serial is not None:
            used_serial.add(vkey)
        fs = [f for f in frames.get(vkey, []) if num(f, "vfxTime") is not None and f["vfxTime"] >= 0]
        vr = vfxrec.get(vkey, [])
        begins = [e for e in vr if e["type"] == "presentation.vfxBegin"]
        tails = [e for e in vr if e["type"] == "presentation.vfxTail"]
        tailends = [e for e in vr if e["type"] == "presentation.vfxTailEnd"]
        has_vfx = bool(starts) and starts[0].get("vfxCue") == "found"
        vfx_summary = None
        if has_vfx:
            ended = bool(stops) and stops[0]["type"] == "presentation.end"
            if not fs:
                p.append("VFX cue but no VFX frame")
            if len(begins) != 1:
                p.append("vfxBegin count %d (expected 1)" % len(begins))
            elif starts[0].get("vfxKind") == "Burst":
                b = begins[0]
                if num(b, "particles") is None or num(b, "burstCount") is None or b["particles"] < b["burstCount"]:
                    p.append("start burst not emitted (particles %s < burstCount %s)" % (b.get("particles"), b.get("burstCount")))
            if ended:
                if len(tails) != 1:
                    p.append("vfxTail count %d (expected 1)" % len(tails))
                else:
                    tl = tails[0]
                    el, vt = num(tl, "elapsedGame"), num(tl, "vfxTimeAtEnd")
                    if el is None or vt is None or dur is None or abs(el - dur) > EPS or abs(vt - dur) > EPS:
                        p.append("vfx tail time %s / elapsed %s != judged duration %s" % (vt, el, dur))
                    if tl.get("emissionEnabled") is not False or tl.get("loop") is not False:
                        p.append("emission still enabled or looping after the judged end")
                if len(tailends) != 1:
                    p.append("vfxTailEnd count %d (expected 1)" % len(tailends))
                elif num(tailends[0], "bornAfterEnd") is None or tailends[0]["bornAfterEnd"] != 0:
                    p.append("particles born after the judged end: %s" % tailends[0].get("bornAfterEnd"))
            elif tails or tailends:
                p.append("vfx tail records for a playback that did not end normally")
            vfx_summary = {"frames": len(fs), "begin_particles": begins[0].get("particles") if begins else None,
                           "tail": [tails[0].get("vfxTimeAtEnd"), tails[0].get("particlesAtEnd")] if tails else None,
                           "bornAfterEnd": tailends[0].get("bornAfterEnd") if tailends else None}
        elif fs or vr:
            p.append("VFX records for a playback without a found VFX cue")
        # clock between consecutive frames of this playback
        prev = None
        for f in frames.get(vkey, []):
            vt, el = num(f, "vfxTime"), num(f, "elapsedGame")
            if vt is None or el is None:
                vfx_issues.append({"tick": f["simulationTick"], "key": list(vkey), "why": "frame numbers missing"})
                prev = None
                continue
            if vt < 0:
                prev = None
                continue
            vfx_frames += 1
            if prev is not None:
                dv, de = vt - prev["vfxTime"], el - prev["elapsedGame"]
                if abs(dv - de) > EPS:
                    vfx_issues.append({"tick": f["simulationTick"], "key": list(vkey), "why": "vfx step %.4f != elapsed step %.4f" % (dv, de)})
                # bossPausedThisTick covers the last tick only, so the no-movement rule applies to single-tick frames
                if f.get("bossPausedThisTick") and f["simulationTick"] - prev["simulationTick"] == 1:
                    hitstop_frames += 1
                    if abs(dv) > EPS:
                        vfx_issues.append({"tick": f["simulationTick"], "key": list(vkey), "why": "vfx advanced %.4f during boss hitstop" % dv})
            prev = f

        rows.append({"actor": t["actor"], "from": t["from"], "to": t["to"], "judged": [t["start_tick"], t["end_tick"], t["status"]],
                     "serial": serial, "presentation": status,
                     "shownTick": starts[0].get("shownTick") if starts else None,
                     "displayDelayTicks": starts[0].get("displayDelayTicks") if starts else None,
                     "stop": [stops[0]["type"], stops[0].get("judgedEndTick"), stops[0].get("observedTick")] if stops else None,
                     "cues": {k: (starts[0].get(k), starts[0].get(k + "Cue"), starts[0].get(k + "Kind")) for k in ("animation", "vfx", "camera")} if starts else None,
                     "vfx": vfx_summary,
                     "frames": [t["start_tick"] - vstart, t["end_tick"] - vstart] if vstart is not None and t["end_tick"] is not None else None,
                     "problems": p, "pass": not p})

    orphans = [{"eventId": e["eventId"], "type": e["type"], "actor": e["actorId"], "judgedStartTick": e.get("judgedStartTick"), "phase": e.get("phase")}
               for k, v in by_key.items() if k not in used for e in v]
    orphans += [{"eventId": e["eventId"], "type": e["type"], "actor": e["actorId"], "transitionSerial": e.get("transitionSerial")}
                for k, v in list(frames.items()) + list(vfxrec.items()) if k not in used_serial for e in v]
    bad = [{"eventId": e["eventId"], "type": e["type"], "actor": e["actorId"]} for e in ev if e["type"] in ("presentation.orphanEnd", "presentation.dropped")]
    return {"video_start_tick": vstart, "transitions": rows, "orphan_presentation_records": orphans, "orphan_or_dropped_events": bad,
            "unmatched_transition_end_events": unmatched, "skipped_count": sum(1 for r in rows if r["presentation"] == "skipped"),
            "vfx_clock": {"frames_total": sum(len(v) for v in frames.values()), "vfx_frames": vfx_frames,
                          "single_tick_hitstop_frames": hitstop_frames, "issues": vfx_issues},
            "all_pass": bool(rows) and all(r["pass"] for r in rows) and not orphans and not bad and not unmatched and not vfx_issues}


def selftest(ev):
    res = []
    base = check(ev)
    res.append({"case": "original", "all_pass": base["all_pass"], "pass": base["all_pass"] is True})
    mx = max(e["eventId"] for e in ev)

    def mutate(name, fn, applicable=True):
        if not applicable:
            res.append({"case": name, "pass": None, "note": "not applicable to this log"})
            return
        m = copy.deepcopy(ev)
        fn(m)
        r = check(m)
        res.append({"case": name, "all_pass": r["all_pass"], "pass": r["all_pass"] is False})

    def find(m, eid):
        return next(e for e in m if e["eventId"] == eid)

    ends = [e for e in ev if e["type"] == "presentation.end"]
    starts = [e for e in ev if e["type"] == "presentation.start"]
    tails = [e for e in ev if e["type"] == "presentation.vfxTail"]
    tailends = [e for e in ev if e["type"] == "presentation.vfxTailEnd"]
    begins_burst = [e for e in ev if e["type"] == "presentation.vfxBegin" and e.get("vfxKind") == "Burst"]
    hs = []
    fr = [e for e in ev if e["type"] == "presentation.frame" and isinstance(e.get("vfxTime"), (int, float)) and e["vfxTime"] >= 0]
    for a, b in zip(fr, fr[1:]):
        if b.get("bossPausedThisTick") and b["simulationTick"] - a["simulationTick"] == 1 and a["actorId"] == b["actorId"] and a.get("transitionSerial") == b.get("transitionSerial"):
            hs.append(b)
    e0 = ends[0] if ends else None
    s0 = next((s for s in starts if e0 and s["actorId"] == e0["actorId"] and s.get("transitionSerial") == e0.get("transitionSerial")), None)

    def dup(m):
        d = copy.deepcopy(find(m, e0["eventId"]))
        d["eventId"] = mx + 1
        m.append(d)
    mutate("duplicated presentation.end", dup, bool(e0))

    def no_ends(m):
        m[:] = [e for e in m if e["type"] != "presentation.end"]
    mutate("all presentation.end removed", no_ends, bool(e0))

    def wrong_end(m):
        find(m, e0["eventId"])["judgedEndTick"] += 1
    mutate("judgedEndTick shifted by 1", wrong_end, bool(e0))

    def skip_add(m):
        m.append({"type": "presentation.skipped", "eventId": mx + 2, "simulationTick": s0["simulationTick"] + 100, "actorId": s0["actorId"],
                  "targetId": "", "stateBefore": "", "stateAfter": "Skipped", "reason": "MUTATION", "phase": s0.get("phase"),
                  "judgedStartTick": s0.get("judgedStartTick"), "judgedEndTick": e0.get("judgedEndTick"), "shownTick": -1,
                  "transitionSerial": s0.get("transitionSerial")})
    mutate("presentation.skipped added", skip_add, bool(s0))

    def missing(m):
        find(m, s0["eventId"])["animationCue"] = "missing"
    mutate("animationCue missing", missing, bool(s0))

    def orphan(m):
        s = copy.deepcopy(s0)
        s["eventId"] = mx + 3
        s["judgedStartTick"] = -5
        m.append(s)
    mutate("presentation.start with no judged transition", orphan, bool(s0))

    def swap(m):  # M05-R1-A
        a, b = find(m, s0["eventId"]), find(m, e0["eventId"])
        a["eventId"], b["eventId"] = b["eventId"], a["eventId"]
    mutate("start and end eventIds swapped (end before start)", swap, bool(s0))

    def shown_bad(m):  # M05-R1-A
        s = find(m, s0["eventId"])
        s["shownTick"] = s["judgedStartTick"] - 20
        s["displayDelayTicks"] = -20
    mutate("shownTick before the judged start, negative delay", shown_bad, bool(s0))

    def no_vfx(m):  # M05-R1-B
        m[:] = [e for e in m if e["type"] not in ("presentation.frame",) + VFX]
    mutate("all VFX frames and tail records removed", no_vfx, bool(tails))

    def tail_short(m):  # M05-R1-B
        t = find(m, tails[0]["eventId"])
        t["vfxTimeAtEnd"] = 0.25
        t["elapsedGame"] = 0.25
    mutate("vfx tail values 0.25 while the transition is longer", tail_short, bool(tails))

    def tail_emitting(m):  # F05-R1
        find(m, tails[0]["eventId"])["emissionEnabled"] = True
    mutate("emission left on after the judged end", tail_emitting, bool(tails))

    def born(m):  # F05-R1
        find(m, tailends[0]["eventId"])["bornAfterEnd"] = 3
    mutate("particles born after the judged end", born, bool(tailends))

    def no_burst(m):  # start burst
        find(m, begins_burst[0]["eventId"])["particles"] = 0
    mutate("start burst not emitted", no_burst, bool(begins_burst))

    def vfx_move(m):
        find(m, hs[0]["eventId"])["vfxTime"] += 0.02
    mutate("vfx advanced during boss hitstop", vfx_move, bool(hs))
    return res


def main():
    path = sys.argv[1]
    ev = tc.load(path)
    out = {"log": path.replace("\\", "/"), **check(ev)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(ev)
        # pass None = mutation not applicable to this log (reported, not counted as a failure)
        out["selftest_all_pass"] = all(r["pass"] is not False for r in out["selftest"])
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main()
