#!/usr/bin/env python3
"""WP-04 boss transition presentation checks (R4: WP04-F04/F05/M05).

Usage:
  python Tools/wp04_presentation_checks.py RUN.ndjson [--selftest]

Each judged transition (ordered linking from Tools/wp04_transition_checks.py) is matched to presentation records by
actor + judgedStartTick + phase. Per transition:
  - exactly one presentation.start, or one presentation.skipped (never shown) -> "skipped" is reported and is NOT a pass;
  - for a shown playback exactly one presentation.end (judged end) or presentation.cancel (judged cancel), with the
    same transitionSerial, judgedEndTick equal to the judged transition end, and observedTick >= judgedEndTick;
  - no cue reported as "missing";
  - VFX clock (presentation.frame / presentation.vfxTail): vfxTime advances exactly by elapsedGame between rendered
    frames, does not move on frames with bossHitstop=true, and equals the transition time at a judged end.
Records that match no transition, duplicate starts/ends, presentation.orphanEnd and presentation.dropped all fail.
all_pass is true only if every transition passes and none of those exist.
"""
import collections
import copy
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wp04_transition_checks as tc  # noqa: E402

EPS = 1e-3


def check(ev):
    ev = sorted(ev, key=lambda e: e["eventId"])
    ts, unmatched = tc.transitions(ev)
    vstart = next((e["simulationTick"] for e in ev if e["type"] == "marker" and e["reason"] == "VideoRecordingStarted"), None)
    pres = [e for e in ev if e["type"] in ("presentation.start", "presentation.skipped", "presentation.end", "presentation.cancel")]
    by_key = collections.defaultdict(list)
    for e in pres:
        by_key[(e["actorId"], e.get("judgedStartTick"), e.get("phase"))].append(e)
    used = set()
    rows = []
    for t in ts:
        key = (t["actor"], t["start_tick"], t["to"])
        recs = by_key.get(key, [])
        used.add(key)
        starts = [e for e in recs if e["type"] == "presentation.start"]
        skips = [e for e in recs if e["type"] == "presentation.skipped"]
        stops = [e for e in recs if e["type"] in ("presentation.end", "presentation.cancel")]
        problems = []
        if len(starts) + len(skips) != 1:
            problems.append("start/skipped count %d/%d (expected exactly one)" % (len(starts), len(skips)))
        if len(stops) > 1:
            problems.append("duplicate end/cancel x%d" % len(stops))
        serials = {e.get("transitionSerial") for e in recs}
        if len(serials) > 1:
            problems.append("mixed transitionSerial %s" % sorted(serials))
        status = "shown" if starts else "skipped" if skips else "absent"
        if skips:
            problems.append("never rendered (skipped)")
        if starts:
            s = starts[0]
            for k in ("animation", "vfx", "camera"):
                if s.get(k + "Cue") == "missing":
                    problems.append(k + " cue missing")
            if len(stops) == 0 and t["complete"]:
                problems.append("no presentation end for a completed transition")
            if stops:
                st = stops[0]
                want = "presentation.cancel" if t["status"] == "cancelled" else "presentation.end"
                if st["type"] != want:
                    problems.append("stop type %s, expected %s" % (st["type"], want))
                if st.get("judgedEndTick") != t["end_tick"]:
                    problems.append("judgedEndTick %s != transition end %s" % (st.get("judgedEndTick"), t["end_tick"]))
                if st.get("observedTick") is None or t["end_tick"] is None or st["observedTick"] < t["end_tick"]:
                    problems.append("observedTick before the judged end")
        if not t["complete"]:
            problems.append("transition not complete (%s)" % t["status"])
        row = {"actor": t["actor"], "from": t["from"], "to": t["to"], "judged": [t["start_tick"], t["end_tick"], t["status"]],
               "presentation": status,
               "shownTick": starts[0]["simulationTick"] if starts else None,
               "displayDelayTicks": starts[0].get("displayDelayTicks") if starts else None,
               "stop": [stops[0]["type"], stops[0].get("judgedEndTick"), stops[0].get("observedTick")] if stops else None,
               "cues": {k: (starts[0].get(k), starts[0].get(k + "Cue"), starts[0].get(k + "Kind")) for k in ("animation", "vfx", "camera")} if starts else None,
               "frames": [t["start_tick"] - vstart, t["end_tick"] - vstart] if vstart is not None and t["end_tick"] is not None else None,
               "problems": problems}
        row["pass"] = not problems
        rows.append(row)
    orphans = [{"eventId": e["eventId"], "type": e["type"], "actor": e["actorId"], "judgedStartTick": e.get("judgedStartTick"), "phase": e.get("phase")}
               for k, v in by_key.items() if k not in used for e in v]
    bad = [{"eventId": e["eventId"], "type": e["type"], "actor": e["actorId"]} for e in ev if e["type"] in ("presentation.orphanEnd", "presentation.dropped")]

    # VFX clock
    vfx_issues = []
    frames = collections.defaultdict(list)
    for e in ev:
        if e["type"] == "presentation.frame":
            frames[(e["actorId"], e.get("transitionSerial"))].append(e)
    hitstop_frames = 0
    for key, fs in frames.items():
        prev = None
        for f in fs:
            vt = f.get("vfxTime", -1)
            if vt is None or vt < 0:
                prev = None
                continue
            if prev is not None:
                dv = vt - prev.get("vfxTime")
                de = f.get("elapsedGame") - prev.get("elapsedGame")
                if abs(dv - de) > EPS:
                    vfx_issues.append({"tick": f["simulationTick"], "key": list(key), "why": "vfx step %.4f != elapsed step %.4f" % (dv, de)})
                if f.get("bossHitstop"):
                    hitstop_frames += 1
                    if abs(dv) > EPS:
                        vfx_issues.append({"tick": f["simulationTick"], "key": list(key), "why": "vfx advanced %.4f during boss hitstop" % dv})
            prev = f
    for e in ev:
        if e["type"] == "presentation.vfxTail":
            el = e.get("elapsedGame")
            if abs(e.get("vfxTimeAtEnd", -1) - el) > EPS:
                vfx_issues.append({"tick": e["simulationTick"], "key": [e["actorId"], e.get("transitionSerial")], "why": "vfx time at end %.4f != elapsed %.4f" % (e.get("vfxTimeAtEnd"), el)})

    return {"video_start_tick": vstart, "transitions": rows, "orphan_presentation_records": orphans, "orphan_or_dropped_events": bad,
            "unmatched_transition_end_events": unmatched, "skipped_count": sum(1 for r in rows if r["presentation"] == "skipped"),
            "vfx_clock": {"frames_checked": sum(len(v) for v in frames.values()), "hitstop_frames": hitstop_frames, "issues": vfx_issues},
            "all_pass": bool(rows) and all(r["pass"] for r in rows) and not orphans and not bad and not unmatched and not vfx_issues}


def selftest(ev):
    res = []
    base = check(ev)
    res.append({"case": "original", "all_pass": base["all_pass"], "pass": base["all_pass"] is True})
    mx = max(e["eventId"] for e in ev)

    def mutate(name, fn):
        m = copy.deepcopy(ev)
        fn(m)
        r = check(m)
        res.append({"case": name, "all_pass": r["all_pass"], "pass": r["all_pass"] is False})

    ends = [e for e in ev if e["type"] == "presentation.end"]
    starts = [e for e in ev if e["type"] == "presentation.start"]
    if ends:
        def dup(m):
            d = copy.deepcopy(next(e for e in m if e["eventId"] == ends[0]["eventId"]))
            d["eventId"] = mx + 1
            m.append(d)
        mutate("duplicated presentation.end", dup)
        def no_ends(m):
            m[:] = [e for e in m if e["type"] != "presentation.end"]
        mutate("all presentation.end removed", no_ends)

        def wrong_end(m):
            for e in m:
                if e["eventId"] == ends[0]["eventId"]:
                    e["judgedEndTick"] = e["judgedEndTick"] + 1
        mutate("judgedEndTick shifted by 1", wrong_end)
    if starts:
        def skip_add(m):
            s = starts[0]
            m.append({"type": "presentation.skipped", "eventId": mx + 2, "simulationTick": s["simulationTick"], "actorId": s["actorId"],
                      "targetId": "", "stateBefore": "", "stateAfter": "Skipped", "reason": "MUTATION", "phase": s.get("phase"),
                      "judgedStartTick": s.get("judgedStartTick"), "transitionSerial": s.get("transitionSerial")})
        mutate("presentation.skipped added", skip_add)

        def to_skip(m):
            for e in m:
                if e["eventId"] == starts[0]["eventId"]:
                    e["type"] = "presentation.skipped"
            m[:] = [e for e in m if not (e["type"] == "presentation.end" and e.get("transitionSerial") == starts[0].get("transitionSerial")
                                         and e["actorId"] == starts[0]["actorId"])]
        mutate("a transition only skipped (never shown)", to_skip)

        def missing(m):
            for e in m:
                if e["eventId"] == starts[0]["eventId"]:
                    e["animationCue"] = "missing"
        mutate("animationCue missing", missing)

        def orphan(m):
            s = copy.deepcopy(starts[0])
            s["eventId"] = mx + 3
            s["judgedStartTick"] = -5
            m.append(s)
        mutate("presentation.start with no judged transition", orphan)
    hs = [e for e in ev if e["type"] == "presentation.frame" and e.get("bossHitstop") and e.get("vfxTime", -1) >= 0]
    if hs:
        def vfx_move(m):
            for e in m:
                if e["eventId"] == hs[0]["eventId"]:
                    e["vfxTime"] = e["vfxTime"] + 0.02
        mutate("vfx advanced during boss hitstop", vfx_move)
    else:
        res.append({"case": "vfx advanced during boss hitstop", "pass": None, "note": "no hitstop frame with VFX in this log"})
    return res


def main():
    path = sys.argv[1]
    ev = tc.load(path)
    out = {"log": path.replace("\\", "/"), **check(ev)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(ev)
        out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"])
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main()
