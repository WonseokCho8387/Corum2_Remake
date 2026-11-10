#!/usr/bin/env python3
"""WP-04-R3 presentation checks: every judged boss transition has exactly one presentation playback that starts on the
transition-start tick and ends on its boss.transitionEnd tick (or stops on boss.transitionCancel). EXP-PRES-01.

python Tools/wp04_presentation_checks.py RUN.ndjson
Uses the ordered linking of Tools/wp04_transition_checks.py. Frames = tick - (tick of the VideoRecordingStarted marker).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wp04_transition_checks as tc  # noqa: E402


def main(path):
    ev = sorted(tc.load(path), key=lambda e: e["eventId"])
    ts, unmatched = tc.transitions(ev)
    vstart = next((e["simulationTick"] for e in ev if e["type"] == "marker" and e["reason"] == "VideoRecordingStarted"), None)
    starts = [e for e in ev if e["type"] == "presentation.start"]
    stops = [e for e in ev if e["type"] in ("presentation.end", "presentation.cancel")]
    rows = []
    used = set()
    for t in ts:
        s = next((e for e in starts if e["actorId"] == t["actor"] and e["eventId"] > t["start_eventId"] and e["eventId"] not in used
                  and e.get("phase") == t["to"]), None)
        if s:
            used.add(s["eventId"])
        stop = None
        if s:
            stop = next((e for e in stops if e["actorId"] == t["actor"] and e.get("transitionSerial") == s.get("transitionSerial")), None)
        expected_stop = "presentation.cancel" if t["status"] == "cancelled" else "presentation.end"
        row = {
            "actor": t["actor"], "from": t["from"], "to": t["to"], "transition": [t["start_tick"], t["end_tick"], t["status"]],
            "presentation": [s["simulationTick"] if s else None, stop["simulationTick"] if stop else None, stop["type"] if stop else None],
            "ids": {k: (s.get(k), s.get(k + "Cue"), s.get(k + "Kind")) for k in ("animation", "vfx", "camera")} if s else None,
            "frames": [t["start_tick"] - vstart, t["end_tick"] - vstart] if vstart is not None and t["end_tick"] is not None else None,
        }
        row["start_on_transition_tick"] = bool(s) and s["simulationTick"] == t["start_tick"]
        row["stop_on_end_tick"] = bool(stop) and t["end_tick"] is not None and stop["simulationTick"] == t["end_tick"] and stop["type"] == expected_stop
        row["no_missing_cue"] = bool(s) and all(s.get(k + "Cue") != "missing" for k in ("animation", "vfx", "camera"))
        row["pass"] = t["complete"] and row["start_on_transition_tick"] and row["stop_on_end_tick"] and row["no_missing_cue"]
        rows.append(row)
    extra = [e["eventId"] for e in starts if e["eventId"] not in used]
    out = {"log": path.replace("\\", "/"), "video_start_tick": vstart, "transitions": rows,
           "presentation_starts_without_transition": extra, "unmatched_end_events": unmatched,
           "skipped": [(e["simulationTick"], e["actorId"], e.get("count")) for e in ev if e["type"] == "presentation.skipped"],
           "all_pass": bool(rows) and all(r["pass"] for r in rows) and not extra and not unmatched}
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main(sys.argv[1])
