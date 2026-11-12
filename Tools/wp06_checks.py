"""WP-06 log checks: P0-04 / P0-05 residual TCs (TC-04-02/03/04/06/07/08, TC-05-01/03/06) and TC-04-09 combo-window runs.

Usage:
  python Tools/wp06_checks.py <main run>.ndjson [--selftest]
  python Tools/wp06_checks.py --cw <run w=0.2>.ndjson <run w=0.6>.ndjson [--selftest]

Each section reads only the log. A section is cut from its scenario marker ("mark=" in the script) to the next marker.
The self-test applies counterexample mutations to an in-memory copy; each must make its own section fail.
"""
import copy
import json
import sys

SCENARIOS = ["TenTargetsStandardChain", "LaunchThenStayOnGround", "InitialDash", "PierceDash", "InitialDive", "AreaDive", "EnemyGuardCancel",
             "MissEndsChain", "TimeoutEndsChain", "RbCancelEndsChain", "MoveDuringAttack", "JumpDuringAttack", "BufferedPress", "PressAfterWindow",
             "LaunchAirNormalDashThenAttack", "RbAtDashStart", "RbAtDashActive", "RbAtDashEnd", "RbAtDiveStart", "RbAtDiveMid", "ComboWindowBoundary",
             "ScriptEnd"]


def load(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(l) for l in f if l.strip()]


def segments(ev):
    marks = [(e["eventId"], e["reason"]) for e in ev if e["type"] == "marker" and e["reason"] in SCENARIOS]
    seg = {}
    for i, (eid, name) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else float("inf")
        seg[name] = [e for e in ev if eid <= e["eventId"] < end]
    return seg


def of(s, t, reason=None):
    return [e for e in s if e["type"] == t and (reason is None or str(e.get("reason", "")).startswith(reason))]


def hits_of(s, inst):
    return [e for e in s if e["type"] == "hit" and e.get("attackInstanceId") == inst]


def ticks(s):
    return [e for e in s if e["type"] == "tick"]


def check(ev):
    seg = segments(ev)
    out = {}

    # ---- TC-04-04: one standard chain on 10 targets: 10 attacks x 10 distinct effective targets = 100 (R-04-01, R-04-04, R-04-05)
    s = seg.get("TenTargetsStandardChain", [])
    starts = of(s, "attack.start")
    chains = {a.get("chainId") for a in starts}
    kinds = [a.get("attackId") for a in starts]
    per = [(a.get("attackId"), len({h["targetId"] for h in hits_of(s, a.get("attackInstanceId"))}), len(hits_of(s, a.get("attackInstanceId")))) for a in starts]
    ends = of(s, "combo.end")
    final = ends[0] if ends else {}
    maxcombo = max([h.get("comboAfter", 0) for h in of(s, "hit")] or [0])
    jumps = of(s, "motor.jump")
    out["TC-04-04"] = {"attacks": kinds, "perAttack(id,distinctTargets,hits)": per, "chains": sorted(chains), "jumps": len(jumps),
                       "comboEnd": (final.get("reason"), final.get("attackChainStep"), final.get("comboHits")), "maxComboAfter": maxcombo,
                       "pass": len(starts) == 10 and len(chains) == 1 and all(d == 10 and n == 10 for _, d, n in per)
                       and kinds == ["ground1", "ground2", "ground3", "ground4", "launcher", "air1", "air2", "air3", "air4", "dive"]
                       and final.get("attackChainStep") == 10 and final.get("comboHits") == 100 and maxcombo == 100 and len(jumps) == 1}

    # ---- TC-04-02: launch, no automatic rise, no jump, then ground attack on the other enemy (R-04-02)
    s = seg.get("LaunchThenStayOnGround", [])
    la = next((a for a in of(s, "attack.start") if a.get("attackId") == "launcher"), None)
    la_hits = hits_of(s, la["attackInstanceId"]) if la else []
    ground = [a for a in of(s, "attack.start") if a.get("attackId", "").startswith("ground")]
    g_hits = [h for a in ground for h in hits_of(s, a["attackInstanceId"])]
    tk = ticks(s)
    out["TC-04-02"] = {"launcherHits": [(h["targetId"], h["reason"]) for h in la_hits], "groundAttackHits": [(h["targetId"], h["reason"]) for h in g_hits],
                       "playerMaxY": max([t.get("y", 0) for t in tk] or [None]), "allTicksGrounded": all(t.get("grounded") for t in tk),
                       "jumps": len(of(s, "motor.jump")),
                       "pass": bool(la_hits) and all(h["reason"].startswith("Launched") for h in la_hits) and bool(tk) and all(t.get("grounded") for t in tk)
                       and max(t.get("y", 0) for t in tk) == 0 and not of(s, "motor.jump") and len(g_hits) >= 1
                       and all(h["targetId"] != la_hits[0]["targetId"] for h in g_hits)}

    # ---- TC-04-06: initial dash stops at the first target (+1), upgraded dash pierces the line (+3) (R-04-06)
    def dash(name):
        s = seg.get(name, [])
        a = next((x for x in of(s, "attack.start") if x.get("attackId") == "airDash"), None)
        h = hits_of(s, a["attackInstanceId"]) if a else []
        e = next((x for x in of(s, "attack.end") if a and x.get("attackInstanceId") == a["attackInstanceId"]), None)
        c = next(iter(of(s, "combo.end")), {})
        return a, h, e, c
    a1, h1, e1, c1 = dash("InitialDash")
    a2, h2, e2, c2 = dash("PierceDash")
    out["TC-04-06"] = {"initial": {"targets": [x["targetId"] for x in h1], "comboHits": c1.get("comboHits"), "endTickAfterHit": (e1 or {}).get("simulationTick", 0) - (h1[0]["simulationTick"] if h1 else 0)},
                       "pierce": {"targets": [x["targetId"] for x in h2], "comboHits": c2.get("comboHits")},
                       "pass": len(h1) == 1 and c1.get("comboHits") == 1 and e1 is not None and e1["simulationTick"] - h1[0]["simulationTick"] <= 1
                       and len({x["targetId"] for x in h2}) == 3 and len(h2) == 3 and c2.get("comboHits") == 3}

    # ---- TC-04-08: initial dive hits one (closest), upgraded dive hits the configured area (R-04-08, U-05)
    def dive(name):
        s = seg.get(name, [])
        a = next((x for x in of(s, "attack.start") if x.get("attackId") == "dive"), None)
        return hits_of(s, a["attackInstanceId"]) if a else [], s
    dh1, s1 = dive("InitialDive")
    dh2, s2 = dive("AreaDive")
    out["TC-04-08"] = {"initial": [x["targetId"] for x in dh1], "area": [(x["targetId"], x.get("how")) for x in dh2],
                       "pass": len(dh1) == 1 and dh1[0]["targetId"] == "d1" and len({x["targetId"] for x in dh2}) == 3 and len(dh2) == 3}

    # ---- TC-04-03: guard cancel / miss / timeout / RB cancel each end the chain; the next attack starts a new chain (R-04-03)
    rows = []
    def ended(name, reason, want_hits):
        s = seg.get(name, [])
        st = of(s, "attack.start")
        ce = of(s, "combo.end")
        first = st[0] if st else None
        e = ce[0] if ce else None
        nxt = st[1] if len(st) > 1 else None
        ok = (first is not None and e is not None and str(e["reason"]).startswith(reason) and e.get("chainId") == first.get("chainId")
              and (nxt is None or nxt.get("chainId") != first.get("chainId")))
        if want_hits is not None and e is not None: ok = ok and e.get("comboHits") == want_hits
        rows.append({"case": name, "endReason": e["reason"] if e else None, "comboHits": e.get("comboHits") if e else None,
                     "chain": first.get("chainId") if first else None, "nextChain": nxt.get("chainId") if nxt else None, "pass": ok})
        return s, first
    s, first = ended("EnemyGuardCancel", "EnemyGuardCancel", 0)
    gc = of(s, "combo.guardCancel")
    gh = [h for h in of(s, "hit") if first and h.get("attackInstanceId") == first.get("attackInstanceId")]
    ae = [e for e in of(s, "attack.end") if first and e.get("attackInstanceId") == first.get("attackInstanceId")]
    guard_ok = bool(gc) and bool(gh) and all(h.get("guarded") is True and h.get("comboAfter") == 0 for h in gh) and bool(ae) and ae[0]["reason"].startswith("EnemyGuardCancel")
    rows[-1]["guardDetail"] = {"guardCancel": len(gc), "guardedHits": [(h["targetId"], h["reason"], h.get("comboAfter")) for h in gh],
                               "attackEnd": ae[0]["reason"] if ae else None}
    rows[-1]["pass"] = rows[-1]["pass"] and guard_ok and len(of(s, "attack.start")) >= 2
    s, first = ended("MissEndsChain", "Miss", 0)
    rows[-1]["pass"] = rows[-1]["pass"] and first is not None and not hits_of(s, first["attackInstanceId"])
    s, first = ended("TimeoutEndsChain", "Timeout", None)
    if first is not None:
        end = next((e for e in of(s, "attack.end") if e.get("attackInstanceId") == first["attackInstanceId"]), None)
        win = next(iter(of(s, "combo.window")), {})
        ce = next(iter(of(s, "combo.end")), {})
        gap = (ce.get("gameTime", 0) - end["gameTime"]) if end else None
        rows[-1]["windowSeconds"] = win.get("window")
        rows[-1]["timeoutAfterAttackEnd"] = gap
        rows[-1]["pass"] = rows[-1]["pass"] and end is not None and win.get("window") is not None and abs(gap - win["window"]) <= 0.03
    s, first = ended("RbCancelEndsChain", "GuardCancel", None)
    if first is not None:
        ae = next((e for e in of(s, "attack.end") if e.get("attackInstanceId") == first["attackInstanceId"]), None)
        late = [h for h in hits_of(s, first["attackInstanceId"]) if ae and h["eventId"] > ae["eventId"]]
        rows[-1]["attackEnd"] = (ae or {}).get("reason")
        rows[-1]["hitboxRemoved"] = (ae or {}).get("hitboxRemoved")
        rows[-1]["pass"] = rows[-1]["pass"] and ae is not None and ae["reason"] == "GuardCancel" and ae.get("hitboxRemoved") is True and not late
    out["TC-04-03"] = {"cases": rows, "pass": len(rows) == 4 and all(r["pass"] for r in rows)}

    # ---- TC-05-01: no user movement / jump before the attack motion ends; possible after (R-05-01)
    s = seg.get("MoveDuringAttack", [])
    tk = ticks(s)
    att = [t for t in tk if t.get("stateAfter") == "GroundAttack"]
    held = [t for t in att if "Right" in str(t.get("heldDir"))]
    runs, cur = [], []
    for t in tk:  # contiguous GroundAttack ticks = one attack motion
        if t.get("stateAfter") == "GroundAttack": cur.append(t)
        elif cur: runs.append(cur); cur = []
    if cur: runs.append(cur)
    moved_in_attack = (not runs) or any(len({round(t["x"], 4) for t in r}) > 1 for r in runs)
    after = [t for t in tk if t.get("stateAfter") == "Walk"]
    s2 = seg.get("JumpDuringAttack", [])
    rej = [e for e in of(s2, "input.reject") if e.get("action") == "Jump" and str(e["reason"]).startswith("ActionLock:GroundAttack")]
    att_end = of(s2, "attack.end")
    jmp = of(s2, "motor.jump")
    out["TC-05-01"] = {"attackTicks": len(att), "attackTicksWithRightHeld": len(held), "xPerAttackMotion": [sorted({round(t["x"], 4) for t in r}) for r in runs],
                       "walkTicksAfter": len(after), "jumpRejectedDuringAttack": len(rej),
                       "jumpAfterEnd": [(j["simulationTick"], j["reason"]) for j in jmp],
                       "pass": len(held) >= 3 and not moved_in_attack and len(after) > 0 and max(t["x"] for t in after) > att[0]["x"]
                       and len(rej) >= 1 and bool(att_end) and bool(jmp) and jmp[0]["simulationTick"] > att_end[0]["simulationTick"]}

    # ---- TC-05-06: press during motion is received then executed at motion end (times separated); after the window it is a new chain;
    #      the dash landing reservation keeps its receive and execute times (R-05-06, EXP-COMBO-01/03)
    s = seg.get("BufferedPress", [])
    buf = of(s, "attack.buffered")
    bx = of(s, "attack.bufferExecuted")
    ae = of(s, "attack.end")
    s2 = seg.get("PressAfterWindow", [])
    st2 = of(s2, "attack.start")
    ce2 = of(s2, "combo.end")
    sres = seg.get("LaunchAirNormalDashThenAttack", [])
    rv = of(sres, "attack.reserved")
    rx = of(sres, "attack.reservationExecuted")
    out["TC-05-06"] = {"buffered": [(b["simulationTick"], b.get("inputEventId")) for b in buf],
                       "bufferExecuted": [(b["simulationTick"], b.get("inputEventId"), b.get("execGameTime")) for b in bx],
                       "afterWindow": {"chains": [a.get("chainId") for a in st2], "endsBetween": [c["reason"] for c in ce2]},
                       "reservation": [(r["simulationTick"], r.get("inputEventId")) for r in rv],
                       "reservationExecuted": [(r["simulationTick"], r.get("reservedGameTime"), r.get("execGameTime")) for r in rx],
                       "pass": len(buf) == 1 and len(bx) == 1 and bx[0].get("inputEventId") == buf[0].get("inputEventId")
                       and bx[0]["simulationTick"] > buf[0]["simulationTick"] and any(a["simulationTick"] == bx[0]["simulationTick"] for a in ae)
                       and len(st2) == 2 and st2[0].get("chainId") != st2[1].get("chainId") and bool(ce2) and ce2[0]["eventId"] < st2[1]["eventId"]
                       and len(rv) == 1 and len(rx) == 1 and rx[0].get("inputEventId") == rv[0].get("inputEventId")
                       and rx[0]["execGameTime"] > rx[0]["reservedGameTime"]}

    # ---- TC-04-07: air normal -> dash branch; no air normal after the dash; the request runs as a ground attack on landing (R-04-07)
    s = sres
    st = of(s, "attack.start")
    ids = [a.get("attackId") for a in st]
    dash_a = next((a for a in st if a.get("attackId") == "airDash"), None)
    land = of(s, "motor.land")
    air_after = [a for a in st if dash_a and a.get("attackId", "").startswith("air") and a.get("attackId") != "airDash" and a["eventId"] > dash_a["eventId"]]
    exec_ = rx[0] if rx else None
    g = next((a for a in st if exec_ and a["simulationTick"] == exec_["simulationTick"] and a.get("attackId") == "ground1"), None)
    out["TC-04-07"] = {"attacks": ids, "airNormalAfterDash": len(air_after), "landTick": land[0]["simulationTick"] if land else None,
                       "reservationExecTick": exec_["simulationTick"] if exec_ else None, "sameChain": len({a.get("chainId") for a in st}) == 1,
                       "pass": ids[:3] == ["launcher", "air1", "airDash"] and not air_after and bool(rv) and dash_a is not None
                       and rv[0]["simulationTick"] >= dash_a["simulationTick"] and bool(land) and g is not None
                       and exec_["simulationTick"] == land[0]["simulationTick"] and len({a.get("chainId") for a in st}) == 1}

    # ---- TC-05-03: RB during dash / dive start, active, end is rejected; no guard cancel of these motions (R-05-03)
    rb = []
    for name, state in (("RbAtDashStart", "AirDash"), ("RbAtDashActive", "AirDash"), ("RbAtDashEnd", "AirDash"), ("RbAtDiveStart", "Dive"), ("RbAtDiveMid", "Dive")):
        s = seg.get(name, [])
        guard_in = [e for e in s if e["type"] == "input" and e.get("action") == "Guard" and e.get("edge") == "Press"]
        rj = [e for e in of(s, "input.reject") if e.get("action") == "Guard" and e["reason"] == "ActionLock:" + state]
        gcancel = [e for e in of(s, "attack.end") if e["reason"] == "GuardCancel"]
        a = next((x for x in of(s, "attack.start") if x.get("attackId") == ("airDash" if state == "AirDash" else "dive")), None)
        e = next((x for x in of(s, "attack.end") if a and x.get("attackInstanceId") == a["attackInstanceId"]), None)
        phase = None
        if a and rj and e:
            span = max(1, e["simulationTick"] - a["simulationTick"])
            phase = round((rj[0]["simulationTick"] - a["simulationTick"]) / span, 2)
        rb.append({"case": name, "guardPresses": len(guard_in), "rejected": [(r["simulationTick"], r["reason"]) for r in rj], "motionEnd": e["reason"] if e else None,
                   "pressPhase(0=start,1=end)": phase, "pass": len(guard_in) == 1 and len(rj) == 1 and not gcancel and e is not None
                   and e["reason"] in ("AirDashEnd", "DiveEnd")})
    out["TC-05-03"] = {"cases": rb, "pass": len(rb) == 5 and all(r["pass"] for r in rb)}
    return out


def cw_check(runs):
    """TC-04-09: the same input gap after an attack ends is inside the window w=0.6 (same chain) and outside w=0.2 (Timeout, new chain)."""
    rows = []
    for ev in runs:
        s = segments(ev).get("ComboWindowBoundary", [])
        win = next(iter(of(s, "combo.window")), {})
        st = of(s, "attack.start")
        ae = of(s, "attack.end")
        pairs = []
        for i in range(0, len(st) - 1, 2):
            a, b = st[i], st[i + 1]
            e = next((x for x in ae if x.get("attackInstanceId") == a.get("attackInstanceId")), None)
            press = next((x for x in ev if x["type"] == "input" and x.get("inputEventId") == b.get("inputEventId") and x.get("edge") == "Press"), None)
            gap = round(press["gameTime"] - e["gameTime"], 3) if e and press else None
            pairs.append({"gapAfterAttackEnd": gap, "sameChain": a.get("chainId") == b.get("chainId"), "step2": b.get("attackChainStep")})
        rows.append({"window": win.get("window"), "pairs": pairs})
    ok = (len(rows) == 2 and all(len(r["pairs"]) == 2 for r in rows))
    if ok:
        small, large = sorted(rows, key=lambda r: r["window"] or 0)
        gaps = [p["gapAfterAttackEnd"] for r in rows for p in r["pairs"]]
        ok = (small["window"] < large["window"] and all(g is not None and small["window"] < g <= large["window"] for g in gaps)
              and all(not p["sameChain"] for p in small["pairs"]) and all(p["sameChain"] and p["step2"] == 2 for p in large["pairs"]))
    return {"TC-04-09": {"runs": rows, "pass": ok}}


def selftest(ev):
    base = check(ev)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]

    def mutate(name, key, fn):
        m = copy.deepcopy(ev)
        fn(m)
        r = check(m)[key]["pass"]
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})

    def dup_target(m):  # one of the 100 hits lands on a target that this attack already hit
        hs = [e for e in m if e["type"] == "hit" and e.get("attackId") == "air2"]
        hs[1]["targetId"] = hs[0]["targetId"]
    mutate("TC-04-04 duplicate target inside one attack", "TC-04-04", dup_target)

    def auto_rise(m):
        s = segments(m)["LaunchThenStayOnGround"]
        t = [e for e in s if e["type"] == "tick"][20]
        t["y"] = 0.4
        t["grounded"] = False
    mutate("TC-04-02 player rises after the launcher", "TC-04-02", auto_rise)

    def dash_no_stop(m):
        s = segments(m)["InitialDash"]
        h = next(e for e in s if e["type"] == "hit")
        h2 = copy.deepcopy(h)
        h2["targetId"] = "p2"
        h2["eventId"] = h["eventId"] + 0.5
        m.insert(m.index(h) + 1, h2)
    mutate("TC-04-06 initial dash hits a second target", "TC-04-06", dash_no_stop)

    def guard_counts(m):
        for e in segments(m)["EnemyGuardCancel"]:
            if e["type"] == "hit" and e.get("guarded"): e["comboAfter"] = 1
    mutate("TC-04-03 guarded hit counted as combo", "TC-04-03", guard_counts)

    def miss_keeps_chain(m):
        drop = {id(e) for e in segments(m)["MissEndsChain"] if e["type"] == "combo.end"}
        m[:] = [e for e in m if id(e) not in drop]
    mutate("TC-04-03 a miss does not end the chain", "TC-04-03", miss_keeps_chain)

    def rb_new_chain_kept(m):
        st = [e for e in segments(m)["RbCancelEndsChain"] if e["type"] == "attack.start"]
        for e in m:
            if e["type"] == "attack.start" and e["eventId"] == st[1]["eventId"]: e["chainId"] = st[0]["chainId"]
    mutate("TC-04-03 attack after an RB cancel keeps the old chain", "TC-04-03", rb_new_chain_kept)

    def moved(m):
        att = [e for e in segments(m)["MoveDuringAttack"] if e["type"] == "tick" and e.get("stateAfter") == "GroundAttack"]
        att[-1]["x"] = att[-1]["x"] + 0.1
    mutate("TC-05-01 player moves during the attack", "TC-05-01", moved)

    def rb_cancels(m):
        s = segments(m)["RbAtDashActive"]
        e = next(x for x in s if x["type"] == "attack.end")
        e["reason"] = "GuardCancel"
    mutate("TC-05-03 RB cancels the dash", "TC-05-03", rb_cancels)

    def air_after_dash(m):
        s = segments(m)["LaunchAirNormalDashThenAttack"]
        a = next(x for x in s if x["type"] == "attack.start" and x.get("attackId") == "airDash")
        b = copy.deepcopy(a)
        b["attackId"] = "air2"
        b["eventId"] = a["eventId"] + 0.5
        b["attackInstanceId"] = -1
        m.insert(m.index(a) + 1, b)
    mutate("TC-04-07 air normal after the dash", "TC-04-07", air_after_dash)

    def exec_same_time(m):
        for e in segments(m)["BufferedPress"]:
            if e["type"] == "attack.bufferExecuted": e["simulationTick"] = e["simulationTick"] - 5
    mutate("TC-05-06 buffered press executed before the motion ends", "TC-05-06", exec_same_time)

    def area_one(m):
        s = segments(m)["AreaDive"]
        hs = [e for e in s if e["type"] == "hit"]
        drop = {id(h) for h in hs[1:]}
        m[:] = [e for e in m if id(e) not in drop]
    mutate("TC-04-08 upgraded dive hits one target", "TC-04-08", area_one)
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--cw" in sys.argv:
        runs = [load(p) for p in args]
        out = {"logs": args, **cw_check(runs)}
        if "--selftest" in sys.argv:
            m = copy.deepcopy(runs)
            for ev in m:  # counterexample: the larger-window run's second pair is a new chain
                for e in ev:
                    if e["type"] == "attack.start" and e.get("attackChainStep") == 2: e["chainId"] = -1
            out["selftest"] = [{"case": "second attacks start new chains in both runs", "sectionPass": cw_check(m)["TC-04-09"]["pass"]}]
            out["selftest"][0]["pass"] = out["selftest"][0]["sectionPass"] is False
            out["selftest_all_pass"] = out["selftest"][0]["pass"]
    else:
        ev = load(args[0])
        out = {"log": args[0], **check(ev)}
        if "--selftest" in sys.argv:
            out["selftest"] = selftest(ev)
            out["selftest_all_pass"] = all(r["pass"] is not False for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
