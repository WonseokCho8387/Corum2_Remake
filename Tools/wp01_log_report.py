"""Summarise a WP-01 run log (NDJSON written by GameHost) per test case.

Usage: python Tools/wp01_log_report.py Diagnostics/Runs/RUN-....ndjson

Prints only observed facts from the log; it does not decide PASS/FAIL.
"""
import json
import sys
from collections import defaultdict


def load(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def by_tc(events):
    groups = defaultdict(list)
    for e in events:
        groups[e.get("testCaseId", "")].append(e)
    return groups


def fmt(v, n=3):
    return f"{v:.{n}f}" if isinstance(v, (int, float)) else str(v)


def jumps(evts, all_events):
    """Pair each motor.jump with the next motor.land; report peak height and horizontal distance from tick samples."""
    out = []
    ticks = [e for e in all_events if e["type"] == "tick"]
    for j in (e for e in evts if e["type"] == "motor.jump"):
        land = next((e for e in evts if e["type"] == "motor.land" and e["eventId"] > j["eventId"]), None)
        start_tick = j["simulationTick"]
        before = [t for t in ticks if t["simulationTick"] == start_tick - 1]
        seg = [t for t in ticks if start_tick <= t["simulationTick"] <= (land["simulationTick"] if land else 10**9)]
        if not seg or not before:
            continue
        x0, y0 = before[0]["x"], before[0]["y"]
        peak = max(t["y"] for t in seg) - y0
        x1 = seg[-1]["x"]
        extra = any(e["type"] == "air.use" and start_tick <= e["simulationTick"] <= seg[-1]["simulationTick"] for e in evts)
        out.append((j["kind"], peak, abs(x1 - x0), j["simulationTick"], land["simulationTick"] if land else None, extra))
    return out


def main(path):
    events = load(path)
    run = events[0]
    print(f"run {run['runId']}  events {len(events)}  dataVersion {run['dataVersion']}")
    ticks = [e for e in events if e["type"] == "tick"]
    planes = {e["planeZ"] for e in ticks}
    print(f"ticks {len(ticks)}  planeZ values {sorted(planes)}  maps visited {sorted({e['mapId'] for e in ticks})}")
    inputs = [e for e in events if e["type"] == "input"]
    lat = sorted(e["latency"] for e in inputs if isinstance(e.get("latency"), (int, float)))
    if lat:
        print(f"input events {len(inputs)}  latency raw->tick min {fmt(lat[0])}s median {fmt(lat[len(lat)//2])}s max {fmt(lat[-1])}s")
    ids = [e["inputEventId"] for e in inputs]
    print(f"input event ids unique: {len(ids) == len(set(ids))}  desync events: {sum(1 for e in events if e['type']=='input.desync')}")
    print()

    for tc, evts in by_tc(events).items():
        if not tc or tc == "manual":
            continue
        print(f"== {tc}  ticks {evts[0]['simulationTick']}..{evts[-1]['simulationTick']}")
        for e in evts:
            t = e["type"]
            if t in ("tick", "input"):
                continue
            if t == "motor.state" and e["reason"] in ("Integrate",):
                continue
            extra = []
            for k in ("kind", "ability", "dir", "facing", "tapGap", "fromMapId", "toMapId", "connectionId", "spawnId", "toPosition",
                      "pointId", "direction", "airBefore", "airAfter", "detail", "position", "edge"):
                if k in e:
                    extra.append(f"{k}={e[k]}")
            print(f"  t{e['simulationTick']:>5} {t:<18} {e['stateBefore']} -> {e['stateAfter']}  [{e['reason']}]  " + " ".join(extra))
        if tc == "TC-03-02":
            for kind, peak, dist, t0, t1, extra in jumps(evts, events):
                print(f"  jump {kind:<8} peak {fmt(peak)} m  horizontal {fmt(dist)} m  ticks {t0}->{t1}  airUseDuring={extra}")
        if tc == "TC-03-01":
            seg = [e for e in evts if e["type"] == "tick"]
            speeds = defaultdict(set)
            for e in seg:
                speeds[e["stateAfter"]].add(round(abs(e["vx"]), 3))
            print("  |vx| per state: " + ", ".join(f"{k}={sorted(v)}" for k, v in speeds.items()))
        if tc == "TC-01-02":
            seg = [e for e in evts if e["type"] == "tick"]
            diffs = [abs(e["camX"] - e["x"]) for e in seg]
            print(f"  map ids {sorted({e['mapId'] for e in seg})}  camX {fmt(seg[0]['camX'])}->{fmt(seg[-1]['camX'])}  player x {fmt(seg[0]['x'])}->{fmt(seg[-1]['x'])}  max |camX-x| {fmt(max(diffs))}  camY {sorted({e['camY'] for e in seg})}")
        if tc == "TC-03-04":
            seg = [e for e in evts if e["type"] == "tick"]
            print(f"  x range {fmt(min(e['x'] for e in seg))}..{fmt(max(e['x'] for e in seg))}  final state {seg[-1]['stateAfter']} at x {fmt(seg[-1]['x'])}")
        print()


if __name__ == "__main__":
    main(sys.argv[1])
