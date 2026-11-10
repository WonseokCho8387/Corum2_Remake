#!/usr/bin/env python3
"""WP-03-R1: outcome of each marked case in the R1 run (run from the repository root).

python Evidence/WP-03-R1/tools/case_outcomes.py Evidence/WP-03-R1/runs/RUN-20261110-131240.ndjson
For every script marker (F02-*, F01-*, M01-*) lists the ring/item/quickslot events until the next marker, plus the
player state, grounded flag, height and HP at the ticks of hits, ring opens and item uses (M01 airborne checks).
"""
import json
import sys

KEEP = {"ring.cursor", "ring.ignore", "ring.use", "ring.mode", "ring.release", "ring.close", "ring.open",
        "quickslot.assign", "item.request", "item.use", "hit.player", "motor.jump"}


def main(path):
    ev = [json.loads(l) for l in open(path, encoding="utf-8")]
    ticks = {e["simulationTick"]: e for e in ev if e["type"] == "tick"}
    cases, cur = [], None
    for e in ev:
        if e["type"] == "marker" and e["reason"].split("-")[0] in ("F01", "F02", "M01"):
            cur = {"case": e["reason"], "tick": e["simulationTick"], "events": []}
            cases.append(cur)
            continue
        if cur is None or e["type"] not in KEEP:
            continue
        if e["type"] == "marker":
            continue
        t = ticks.get(e["simulationTick"], {})
        cur["events"].append({
            "tick": e["simulationTick"], "type": e["type"], "reason": e["reason"],
            "item": e.get("item") or e.get("selectedItem") or e.get("targetId") or "",
            "stick": e.get("stick"), "stickReleasedSinceY": e.get("stickReleasedSinceY"),
            "player": t.get("stateAfter"), "grounded": t.get("grounded"), "y": t.get("y"), "hp": t.get("hp"),
        })
    json.dump({"log": path, "cases": cases}, sys.stdout, ensure_ascii=False, indent=1)
    print()


if __name__ == "__main__":
    main(sys.argv[1])
