"""WP-11 Parts B and C log checks.

Part B (scene WP11_Ops): TC-OPS-01 and TC-UNITY-03 from seven runs of one scenario:
  python Tools/wp11b_checks.py --ops base.ndjson base2.ndjson v1.ndjson v2.ndjson v3.ndjson v4.ndjson v5.ndjson [--selftest]
  v1 extraJump=0, v2 airDash=0, v3 dashPierce=1, v4 diveArea=1, v5 comboWindow=0.3 (each a one-value Inspector change, restored from git).
Part C (scene WP11_Ops): TC-UNITY-05 (observation) and TC-UNITY-06 from a 50 Hz and a 60 Hz run:
  python Tools/wp11b_checks.py --time s50.ndjson s60.ndjson [--selftest]
"""
import copy
import json
import os
import re
import sys

ZONES = ["O1Jump", "O2Dash", "O3Pierce", "O4Dive", "O5Combo", "O6Twins"]
PHASES = ["C_Normal", "C_Slow", "C_Pause", "C_Delay", "C_RingTx"]
NAMES = ZONES + PHASES + ["ScriptEnd"]
VARIANTS = {  # name -> (asset file, field, baseline value, variant value, zones expected to change)
    "v1": ("Assets/Corum2/Data/WP11/AirUnlocks_Ops.asset", "extraJump", "1", "0", {"O1Jump"}),
    "v2": ("Assets/Corum2/Data/WP11/AirUnlocks_Ops.asset", "airDash", "1", "0", {"O2Dash", "O3Pierce"}),
    "v3": ("Assets/Corum2/Data/WP11/CombatUnlocks_Ops.asset", "dashPierce", "0", "1", {"O3Pierce"}),
    "v4": ("Assets/Corum2/Data/WP11/CombatUnlocks_Ops.asset", "diveArea", "0", "1", {"O4Dive"}),
    "v5": ("Assets/Corum2/Data/WP11/CombatConfig_Ops.asset", "comboWindow", "0.5", "0.3", {"O5Combo"}),
}
SKIP = {"runId", "realTime", "eventId", "gameTime", "dataVersion", "testCaseId", "requestTime", "latency", "unscaledTime", "startGameTime",
        "effectGameTime", "requestGameTime", "execGameTime", "cooldownUntil", "file", "enemies"}


def load(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(l) for l in f if l.strip()]


def segments(ev):
    marks = [(e["eventId"], e["reason"]) for e in ev if e["type"] == "marker" and e["reason"] in NAMES]
    seg = {}
    for i, (eid, name) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else float("inf")
        seg[name] = [e for e in ev if eid <= e["eventId"] < end]
    return seg


def of(s, t, reason=None):
    return [e for e in s if e["type"] == t and (reason is None or str(e.get("reason", "")).startswith(reason))]


def norm(v):
    if isinstance(v, float):
        return round(v, 4)
    if isinstance(v, list):
        return [norm(x) for x in v]
    return v


def signature(s):
    """Every simulation record of a zone, ticks relative to the zone marker, without wall-clock fields and input ids
    (ids are allocated per run). Player ticks keep position and state; enemy positions come with their own records."""
    if not s:
        return []
    t0 = s[0]["simulationTick"]
    out = []
    for e in s:
        if e["type"] in ("frame.sample",):
            continue
        d = {k: norm(v) for k, v in e.items() if k not in SKIP and "inputEventId" not in k and k != "sampleGroup" and k != "simulationTick"}
        if e["type"] == "tick":
            d = {k: d.get(k) for k in ("type", "x", "y", "vx", "vy", "stateAfter", "grounded", "air", "chainId", "combo")}
        out.append((e["simulationTick"] - t0, json.dumps(d, sort_keys=True, ensure_ascii=False)))
    return out


def first_diff(a, b):
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            return {"index": i, "base": x, "variant": y}
    if len(a) != len(b):
        return {"index": min(len(a), len(b)), "base": a[len(b)] if len(a) > len(b) else None, "variant": b[len(a)] if len(b) > len(a) else None}
    return None


def patch_of(path):
    """The run's dirty patch: changed files and -/+ lines (manifest dirty_diff_artifact)."""
    p = path[:-len(".ndjson")] + ".dirty.patch"
    if not os.path.exists(p):
        return None
    files, minus, plus = [], [], []
    for line in open(p, encoding="utf-8", errors="replace"):
        if line.startswith("diff --git"):
            files.append(line.split(" b/")[-1].strip())
        elif line.startswith("-") and not line.startswith("---"):
            minus.append(line[1:].strip())
        elif line.startswith("+") and not line.startswith("+++"):
            plus.append(line[1:].strip())
    return {"files": files, "minus": minus, "plus": plus}


def behaviour(seg):
    """The specific behaviour each zone exists for (used to show the variant changed it the way the setting says)."""
    b = {}
    s = seg.get("O1Jump", [])
    b["O1Jump"] = {"extraJump": len(of(s, "air.use", "ExtraJump")), "rejected": [e["reason"] for e in of(s, "input.reject") if e.get("action") == "Jump"]}
    for z in ("O2Dash", "O3Pierce"):
        s = seg.get(z, [])
        d = [e for e in of(s, "attack.start") if e.get("attackId") == "airDash"]
        b[z] = {"dashes": len(d), "dashHits": sorted(e["targetId"] for e in of(s, "hit") if e.get("attackId") == "airDash"),
                "rejected": [e["reason"] for e in of(s, "input.reject") if e.get("action") == "Attack"]}
    s = seg.get("O4Dive", [])
    b["O4Dive"] = {"diveHits": sorted(e["targetId"] for e in of(s, "hit") if e.get("attackId") == "dive")}
    s = seg.get("O5Combo", [])
    st = of(s, "attack.start")
    b["O5Combo"] = {"attacks": [(e.get("attackId"), e.get("chainId"), e.get("attackChainStep")) for e in st],
                    "gapTicks": (st[1]["simulationTick"] - of(s, "attack.end")[0]["simulationTick"]) if len(st) > 1 and of(s, "attack.end") else None}
    s = seg.get("O6Twins", [])
    b["O6Twins"] = {"hitTargets": sorted(e["targetId"] for e in of(s, "hit"))}
    return b


def twins(ev, seg):
    """TC-UNITY-03 runtime isolation: two instances of one definition; only ta is hit; tb keeps its full HP and Idle on every tick."""
    s = seg.get("O6Twins", [])
    rx = re.compile(r"(t[ab]):([A-Za-z]+)@(-?[\d.]+),(-?[\d.]+) hp(-?[\d.]+)")
    hp = {"ta": set(), "tb": set()}
    st = {"ta": set(), "tb": set()}
    for e in s:
        if e["type"] == "tick":
            for m in rx.finditer(e.get("enemies", "")):
                hp[m.group(1)].add(float(m.group(5)))
                st[m.group(1)].add(m.group(2))
    return {"taHp": sorted(hp["ta"]), "tbHp": sorted(hp["tb"]), "tbStates": sorted(st["tb"]),
            "pass": len(hp["ta"]) >= 2 and len(hp["tb"]) == 1 and st["tb"] == {"Idle"} and max(hp["ta"]) == min(hp["tb"])}


def check_ops(paths, logs):
    names = ["base", "base2", "v1", "v2", "v3", "v4", "v5"]
    runs = dict(zip(names, logs))
    seg = {n: segments(runs[n]) for n in names}
    sig = {n: {z: signature(seg[n].get(z, [])) for z in ZONES} for n in names}
    beh = {n: behaviour(seg[n]) for n in names}
    out = {}
    # TC-UNITY-03 (rerun): base and base2 are the same simulation, zone by zone
    rerun = {z: sig["base"][z] == sig["base2"][z] and len(sig["base"][z]) > 0 for z in ZONES}
    pb = {n: patch_of(p) for n, p in zip(names, paths)}
    rows = []
    for v, (asset, field, b0, b1, expect) in VARIANTS.items():
        changed = {z for z in ZONES if sig["base"][z] != sig[v][z]}
        p = pb[v]
        one = p is not None and p["files"] == [asset] and len(p["minus"]) == 1 and len(p["plus"]) == 1 \
            and p["minus"][0] == f"{field}: {b0}" and p["plus"][0] == f"{field}: {b1}"
        bb, bv = beh["base"], beh[v]
        if v == "v1":
            spec = bb["O1Jump"]["extraJump"] == 1 and bv["O1Jump"]["extraJump"] == 0 and len(bv["O1Jump"]["rejected"]) == 1
        elif v == "v2":
            spec = bb["O2Dash"]["dashes"] == 1 and bv["O2Dash"]["dashes"] == 0 and bv["O3Pierce"]["dashes"] == 0 \
                and bv["O2Dash"]["rejected"] and bv["O3Pierce"]["dashHits"] == []
        elif v == "v3":
            spec = bb["O3Pierce"]["dashHits"] == ["pa"] and bv["O3Pierce"]["dashHits"] == ["pa", "pb", "pc"]
        elif v == "v4":
            spec = bb["O4Dive"]["diveHits"] == ["da"] and bv["O4Dive"]["diveHits"] == ["da", "db", "dc"]
        else:
            a0, a1 = bb["O5Combo"]["attacks"], bv["O5Combo"]["attacks"]
            spec = len(a0) == 2 == len(a1) and a0[1][0] == "ground2" and a0[1][1] == a0[0][1] and a1[1][0] == "ground1" and a1[1][1] != a1[0][1] \
                and bb["O5Combo"]["gapTicks"] == bv["O5Combo"]["gapTicks"] and 15 < bb["O5Combo"]["gapTicks"] < 25
        rows.append({"variant": v, "asset": asset, "field": field, "value": [b0, b1], "patchOneLine": one, "patch": p,
                     "zonesChanged": sorted(changed), "zonesExpected": sorted(expect), "behaviour": {z: bv[z] for z in sorted(expect)},
                     "baseBehaviour": {z: bb[z] for z in sorted(expect)},
                     "firstUnexpectedDiff": {z: first_diff(sig["base"][z], sig[v][z]) for z in sorted(changed - expect)},
                     "pass": one and changed == expect and spec})
    base_clean = pb["base"] is None and pb["base2"] is None
    out["TC-OPS-01"] = {"variants": rows, "baselineClean": base_clean, "baselineBehaviour": beh["base"],
                        "note": "each variant changes one Inspector value of the WP11_Ops fixtures; every other zone is identical to the baseline, record by record",
                        "pass": base_clean and all(r["pass"] for r in rows) and all(rerun.values())}
    tw = {n: twins(runs[n], seg[n]) for n in names}
    out["TC-UNITY-03"] = {"rerunIdentical": rerun, "twins": tw,
                          "inspectorChanges": [{"variant": r["variant"], "file": r["asset"], "line": [f"{r['field']}: {r['value'][0]}", f"{r['field']}: {r['value'][1]}"],
                                                "inPatch": r["patchOneLine"]} for r in rows],
                          "note": "value / file tracked by each run's manifest dirty patch (one line); runtime state isolated per instance; rerun identical",
                          "pass": all(rerun.values()) and all(t["pass"] for t in tw.values()) and all(r["patchOneLine"] for r in rows) and base_clean}
    return out


# ------------------------------------------------------------------ Part C
def phase_obs(s):
    fr = of(s, "frame.sample")
    tpf = {}
    for e in fr:
        tpf[e["ticksThisFrame"]] = tpf.get(e["ticksThisFrame"], 0) + 1
    ts = {}
    for e in fr:
        k = round(e["timeScale"], 3)
        ts[k] = ts.get(k, 0) + 1
    cost = [e["simCostMs"] / e["ticksThisFrame"] for e in fr if e["ticksThisFrame"]]
    ticks = [e["simTick"] for e in fr]
    real = [e["realTime"] for e in fr]
    inputs = [{"action": e.get("action"), "edge": e.get("edge"), "requestTime": round(e.get("requestTime", 0), 4), "deliveredTick": e.get("deliveredTick"),
               "latencyRealS": round(e.get("latency", 0), 4)} for e in of(s, "input") if e.get("edge") in ("Press", "Release")]
    atk = []
    for a in of(s, "attack.start"):
        end = next((e for e in of(s, "attack.end") if e.get("attackInstanceId") == a["attackInstanceId"]), None)
        if end:
            atk.append({"attackId": a.get("attackId"), "ticks": end["simulationTick"] - a["simulationTick"],
                        "gameSeconds": round(end.get("elapsed", 0), 4)})
    return {"frames": len(fr), "ticksPerFrame": tpf, "timeScale": ts, "simTicks": (ticks[-1] - ticks[0]) if ticks else 0,
            "realSeconds": round(real[-1] - real[0], 3) if real else 0, "simCostMsPerTick": {"mean": round(sum(cost) / len(cost), 4) if cost else None,
                                                                                             "max": round(max(cost), 4) if cost else None},
            "fixedDeltaTime": fr[0]["fixedDeltaTime"] if fr else None, "inputs": inputs, "attacks": atk,
            "pauses": [(e["reason"], e["simulationTick"]) for e in of(s, "time.pause")]}


def transport(ev):
    """TC-UNITY-06 on one run: every device edge is routed exactly once, with an owner and a time; game-bound edges are delivered
    once on a tick at or after their routing; every allocated id is accounted for (an edge id or a stick sample group)."""
    routes = [e for e in of(ev, "input.route") if e.get("edge") in ("Press", "Release")]
    rid = [e["inputEventId"] for e in routes]
    dev = [e for e in of(ev, "input") if e.get("edge") in ("Press", "Release") and e.get("reason") != "router"]
    game = [e for e in routes if e.get("consumer") == "game"]
    delivered = {}
    for e in dev:
        delivered.setdefault(e["inputEventId"], []).append(e)
    missing = [e["inputEventId"] for e in game if e["inputEventId"] not in delivered]
    dup = [i for i, v in delivered.items() if len(v) > 1]
    late = [e["inputEventId"] for e in game if e["inputEventId"] in delivered and delivered[e["inputEventId"]][0]["simulationTick"] < e["simulationTick"]]
    owner = all(e.get("consumer") in ("game", "ring", "quickslot") and e.get("requestTime") is not None for e in routes)
    groups = {e.get("sampleGroup") for e in ev if e.get("sampleGroup") is not None}
    ids = set(rid) | set(delivered) | groups | {e.get("inputEventId") for e in ev if e.get("inputEventId") is not None}
    ids.discard(None)
    top = max(ids) if ids else 0
    small = sorted(i for i in ids if i < 1000000000)
    gaps = [i for i in range(1, (small[-1] if small else 0) + 1) if i not in ids]
    return {"routedEdges": len(routes), "uniqueRouteIds": len(rid) == len(set(rid)), "gameEdges": len(game), "gameDeliveredOnce": not missing and not dup,
            "missing": missing, "duplicated": dup, "deliveredBeforeRouting": late, "ownerAndTime": owner, "idGaps": gaps,
            "pass": len(rid) == len(set(rid)) and not missing and not dup and not late and owner and not gaps and len(routes) > 0}


def check_time(paths, logs):
    out = {}
    obs = {}
    tr = {}
    for hz, ev in zip((50, 60), logs):
        seg = segments(ev)
        obs[hz] = {p: phase_obs(seg.get(p, [])) for p in PHASES}
        tr[hz] = transport(ev)
    # observation completeness (no policy verdict): each phase sampled at both rates with the conditions it is meant to show
    def ok(hz):
        o = obs[hz]
        dt = 0.02 if hz == 50 else 1 / 60
        return all(o[p]["frames"] > 0 for p in PHASES) and abs((o["C_Normal"]["fixedDeltaTime"] or 0) - dt) < 1e-4 \
            and o["C_Slow"]["timeScale"].get(0.2, 0) > 0 and o["C_Pause"]["timeScale"].get(0.0, 0) > 0 and o["C_Pause"]["ticksPerFrame"].get(0, 0) > 0 \
            and max(o["C_Delay"]["ticksPerFrame"]) >= 5 and len(o["C_Pause"]["pauses"]) == 2
    out["TC-UNITY-05"] = {"status": "OBSERVED", "policyVerdict": "BLOCKED_SPEC (U-06, CF-04/05, EXP-SIM-01)", "rates": obs,
                          "observationComplete": ok(50) and ok(60),
                          "note": "observation only: frames, ticks per frame, time scale, input request/delivery, attack length, simulation cost per tick",
                          "pass": ok(50) and ok(60)}
    # TC-UNITY-06: transport at both rates, plus the specific same-frame cases
    cases = {}
    for hz, ev in zip((50, 60), logs):
        seg = segments(ev)
        d = seg.get("C_Delay", [])
        jd = [e for e in of(d, "input") if e.get("action") == "Attack"]
        multi = next((e for e in of(d, "frame.sample") if e["ticksThisFrame"] >= 5), None)
        st = of(d, "attack.start")
        r = seg.get("C_RingTx", [])
        rr = [(e.get("action"), e.get("edge"), e.get("consumer")) for e in of(r, "input.route") if e.get("edge") in ("Press", "Release")]
        cases[hz] = {"pressReleaseSameFrame": [(e["edge"], e["deliveredTick"]) for e in jd], "multiTickFrame": multi and multi["ticksThisFrame"],
                     "attackStartedOnce": len(st) == 1, "ringTransitionRoutes": rr,
                     "pass": len(jd) == 2 and jd[0]["edge"] == "Press" and jd[1]["edge"] == "Release" and jd[0]["deliveredTick"] == jd[1]["deliveredTick"]
                     and multi is not None and len(st) == 1
                     and ("RingToggle", "Press", "ring") in rr and ("Jump", "Press", "game") in rr}
    out["TC-UNITY-06"] = {"transport": tr, "cases": cases,
                          "note": "an attack pressed on the frame the ring opens goes to the ring (its release to the game): the contention order is CF-01/07/08",
                          "pass": all(t["pass"] for t in tr.values()) and all(c["pass"] for c in cases.values())}
    return out


# ------------------------------------------------------------------ self-tests
def selftest(mode, paths, logs):
    fn = check_ops if mode == "ops" else check_time
    base = fn(paths, logs)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]

    def mutate(name, key, which, edit):
        m = copy.deepcopy(logs)
        try:
            edit(m[which], segments(m[which]))
        except (StopIteration, KeyError, IndexError) as x:
            res.append({"case": name, "section": key, "sectionPass": None, "pass": None, "note": f"record to mutate not present ({x!r})"})
            return
        r = fn(paths, m)[key]["pass"]
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})

    def first(s, typ, pred=lambda e: True):
        return next(e for e in s if e["type"] == typ and pred(e))
    if mode == "ops":
        mutate("pierce variant also changes the dive", "TC-OPS-01", 4, lambda m, s: first(s["O4Dive"], "hit").update(targetId="db"))
        mutate("extra jump still used with extraJump 0", "TC-OPS-01", 2, lambda m, s: first(s["O1Jump"], "input.reject", lambda e: e.get("action") == "Jump").update(
            type="air.use", reason="ExtraJump"))
        mutate("combo variant keeps the chain", "TC-OPS-01", 6, lambda m, s: [e.update(attackId="ground2", chainId=of(s["O5Combo"], "attack.start")[0]["chainId"])
                                                                              for e in of(s["O5Combo"], "attack.start")[1:2]])
        mutate("area variant hits only one", "TC-OPS-01", 5, lambda m, s: [m.remove(e) for e in [x for x in of(s["O4Dive"], "hit") if x["targetId"] != "da"]])
        mutate("rerun differs in one tick", "TC-UNITY-03", 1, lambda m, s: first(s["O2Dash"], "tick").update(x=first(s["O2Dash"], "tick")["x"] + 0.01))
        mutate("second twin damaged by the hit on the first", "TC-UNITY-03", 0,
               lambda m, s: [e.update(enemies=re.sub(r"(tb:\w+@[-\d.]+,[-\d.]+ hp)[\d.]+", lambda x: x.group(1) + "40", e["enemies"])) for e in of(s["O6Twins"], "tick")[-5:]])
        mutate("dash variant changes the jump zone too", "TC-OPS-01", 3, lambda m, s: first(s["O1Jump"], "tick").update(y=0.5))
    else:
        mutate("game press delivered twice", "TC-UNITY-06", 0, lambda m, s: m.insert(m.index(first(s["C_Normal"], "input", lambda e: e.get("edge") == "Press")) + 1,
                                                                                    dict(first(s["C_Normal"], "input", lambda e: e.get("edge") == "Press"), simulationTick=first(s["C_Normal"], "input", lambda e: e.get("edge") == "Press")["simulationTick"] + 1)))
        mutate("game release lost", "TC-UNITY-06", 1, lambda m, s: m.remove(first(s["C_Delay"], "input", lambda e: e.get("action") == "Attack" and e.get("edge") == "Release")))
        mutate("route without owner", "TC-UNITY-06", 0, lambda m, s: first(s["C_RingTx"], "input.route").update(consumer=""))
        mutate("attack pressed on the long frame runs twice", "TC-UNITY-06", 1,
               lambda m, s: m.insert(m.index(first(s["C_Delay"], "attack.start")) + 1, dict(first(s["C_Delay"], "attack.start"), attackInstanceId=999)))
        mutate("id allocated but never seen", "TC-UNITY-06", 0, lambda m, s: m.remove(first(s["C_Normal"], "input.route")) or
               [m.remove(e) for e in [x for x in m if x.get("inputEventId") == 1]])
        mutate("no stop frames observed at 60 Hz", "TC-UNITY-05", 1, lambda m, s: [e.update(timeScale=1, ticksThisFrame=1) for e in of(s["C_Pause"], "frame.sample")])
        mutate("60 Hz run at 50 Hz step", "TC-UNITY-05", 1, lambda m, s: [e.update(fixedDeltaTime=0.02) for e in of(s["C_Normal"], "frame.sample")])
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    mode = "ops" if "--ops" in sys.argv else "time"
    logs = [load(p) for p in args]
    out = {"logs": args, **(check_ops(args, logs) if mode == "ops" else check_time(args, logs))}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(mode, args, logs)
        out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
