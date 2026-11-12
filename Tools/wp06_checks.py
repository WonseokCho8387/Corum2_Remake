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
             # R1 (GPT WP-06 review): TC-05-01 matrix, WP06-F01 guard 1 / 0 pierce dash, TC-04-04 one attack on 5 targets
             "MoveGround+2", "MoveGround+12", "MoveGround+22", "MoveRepressGround", "JumpGround+2", "JumpGround+12", "JumpGround+22",
             "MoveLauncher+2", "MoveLauncher+12", "MoveLauncher+22", "MoveRepressLauncher", "JumpLauncher+2", "JumpLauncher+12", "JumpLauncher+22",
             "GuardThenPierce", "NoGuardPierce", "FiveTargetsOneAttack",
             # R2 (GPT WP-06~08 R1 review): TC-05-01 air normal / air dash / dive, control + move / jump at start / middle / end
             *[f"Air{a}_{c}" for a in ("AirNormal", "AirDash", "Dive") for c in ("Control", "Movestart", "Movemid", "Moveend", "Jumpstart", "Jumpmid", "Jumpend")],
             "ScriptEnd"]
AIR = {"AirNormal": ("air1", "ActionLock:AirAttack"), "AirDash": ("airDash", "ActionLock:AirDash"), "Dive": ("dive", "ActionLock:Dive")}

R1_MATRIX = ["MoveGround+2", "MoveGround+12", "MoveGround+22", "MoveRepressGround", "JumpGround+2", "JumpGround+12", "JumpGround+22",
             "MoveLauncher+2", "MoveLauncher+12", "MoveLauncher+22", "MoveRepressLauncher", "JumpLauncher+2", "JumpLauncher+12", "JumpLauncher+22"]


def effective(h, before=None):
    """An effective hit (R-04-05): not guarded, real HP loss, count +1 (comboBefore -> comboAfter)."""
    ok = (h.get("guarded") is False and h.get("hpAfter") is not None and h.get("hpBefore") is not None and h["hpAfter"] < h["hpBefore"]
          and h.get("comboAfter") == h.get("comboBefore", -99) + 1)
    return ok and (before is None or h.get("comboBefore") == before)


def next_start_after(ev, e):
    """The first attack.start after event e in the whole log (not cut at the next marker; GPT WP-06 review 3)."""
    return next((x for x in ev if x["type"] == "attack.start" and x["eventId"] > e["eventId"]), None)


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


def in_motion(s, a, x):
    """x happens inside attack a: at or after its start, at or before its end."""
    e = next((y for y in s if y["type"] == "attack.end" and y.get("attackInstanceId") == a["attackInstanceId"]), None)
    return e is not None and a["simulationTick"] <= x["simulationTick"] <= e["simulationTick"] and a["eventId"] < x["eventId"]


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
    sets = [frozenset(h["targetId"] for h in hits_of(s, a.get("attackInstanceId"))) for a in starts]
    seq = [h.get("comboAfter") for h in of(s, "hit")]
    every_eff = all(effective(h) for h in of(s, "hit"))
    same_ten = bool(sets) and len(sets[0]) == 10 and all(x == sets[0] for x in sets)
    # R1: one attack on 5 targets shows +5 (the same display path as 100, V+L)
    s5 = seg.get("FiveTargetsOneAttack", [])
    st5 = of(s5, "attack.start")
    h5 = hits_of(s5, st5[0]["attackInstanceId"]) if st5 else []
    ce5 = next(iter(of(s5, "combo.end")), {})
    five = {"attacks": len(st5), "targets": sorted(h["targetId"] for h in h5), "ticks": sorted({h["simulationTick"] for h in h5}),
            "counts": [h.get("comboAfter") for h in h5], "comboEnd": ce5.get("comboHits"),
            "pass": len(st5) == 1 and sorted(h["targetId"] for h in h5) == ["f0", "f1", "f2", "f3", "f4"] and len({h["simulationTick"] for h in h5}) == 1
            and [h.get("comboAfter") for h in h5] == [1, 2, 3, 4, 5] and all(effective(h) for h in h5) and ce5.get("comboHits") == 5}
    out["TC-04-04"] = {"attacks": kinds, "perAttack(id,distinctTargets,hits)": per, "chains": sorted(chains), "jumps": len(jumps),
                       "sameTenTargetsEveryAttack": same_ten, "everyHitEffective": every_eff, "countSequence1to100": seq == list(range(1, 101)),
                       "fiveTargets": five,
                       "comboEnd": (final.get("reason"), final.get("attackChainStep"), final.get("comboHits")), "maxComboAfter": maxcombo,
                       "pass": len(starts) == 10 and len(chains) == 1 and all(d == 10 and n == 10 for _, d, n in per)
                       and kinds == ["ground1", "ground2", "ground3", "ground4", "launcher", "air1", "air2", "air3", "air4", "dive"]
                       and final.get("attackChainStep") == 10 and final.get("comboHits") == 100 and maxcombo == 100 and len(jumps) == 1
                       and same_ten and every_eff and seq == list(range(1, 101)) and five["pass"]}

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
                       and all(h["targetId"] != la_hits[0]["targetId"] for h in g_hits)
                       and all(h["simulationTick"] > la["simulationTick"] and h["eventId"] > la["eventId"] for h in g_hits)
                       and all(in_motion(s, a, h) for a in ground for h in hits_of(s, a["attackInstanceId"]))}

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
                       "pass": len(h1) == 1 and c1.get("comboHits") == 1 and e1 is not None and 0 <= e1["simulationTick"] - h1[0]["simulationTick"] <= 1
                       and e1["eventId"] > h1[0]["eventId"] and effective(h1[0], 0)
                       and len({x["targetId"] for x in h2}) == 3 and len(h2) == 3 and c2.get("comboHits") == 3 and all(effective(x) for x in h2)}
    # R1 WP06-F01: guard on the first target ends the chain (no count); the later effective hit of the same dash opens a new chain and counts.
    ga, gh, ge, _ = dash("GuardThenPierce")
    sg = seg.get("GuardThenPierce", [])
    g_first = [x for x in gh if x["targetId"] == "gp"]
    g_later = [x for x in gh if x["targetId"] == "gq"]
    g_end = [x for x in of(sg, "combo.end", "EnemyGuardCancel") if ga and x.get("chainId") == ga.get("chainId")]
    reo = [x for x in of(sg, "combo.reopen") if ga and x.get("attackInstanceId") == ga["attackInstanceId"]]
    later_end = next((x for x in of(sg, "combo.end") if reo and x.get("chainId") == reo[0].get("chainId")), {})
    guard1 = bool(ga) and len(g_first) == 1 and len(g_later) == 1 and g_first[0].get("guarded") is True and g_first[0]["comboAfter"] == g_first[0]["comboBefore"] == 0 \
        and len(g_end) == 1 and g_end[0]["simulationTick"] == g_first[0]["simulationTick"] and g_later[0]["simulationTick"] > g_first[0]["simulationTick"] \
        and effective(g_later[0], 0) and g_later[0]["stateAfter"] == "Stagger" and len(reo) == 1 and reo[0]["simulationTick"] == g_later[0]["simulationTick"] \
        and reo[0]["eventId"] < g_later[0]["eventId"] and g_later[0].get("chainId") == reo[0].get("chainId") != ga.get("chainId") and later_end.get("comboHits") == 1
    # R2 (GPT WP06-M01-R1): the reopen names this dash's real guard tick, and the new chain really begins (combo.begin) on the hit tick
    beg = [x for x in of(sg, "combo.begin", "PostGuardEffectiveHit") if reo and x.get("chainId") == reo[0].get("chainId")]
    guard1 = guard1 and reo[0].get("guardEndTick") == g_first[0]["simulationTick"] == g_end[0]["simulationTick"] and reo[0].get("attackId") == "airDash" \
        and len(beg) == 1 and beg[0]["simulationTick"] == g_later[0]["simulationTick"] and beg[0]["eventId"] < reo[0]["eventId"]
    na, nh, _, _ = dash("NoGuardPierce")
    sn = seg.get("NoGuardPierce", [])
    n_first = [x for x in nh if x["targetId"] == "np"]
    n_later = [x for x in nh if x["targetId"] == "nq"]
    guard0 = bool(na) and len(n_first) == 1 and len(n_later) == 1 and effective(n_first[0], 0) and effective(n_later[0], 1) \
        and n_later[0]["simulationTick"] > n_first[0]["simulationTick"] and not of(sn, "combo.reopen") and not of(sn, "combo.guardCancel")
    out["TC-04-06"]["guardThenPierce(WP06-F01)"] = {"first": [(x["targetId"], x["simulationTick"], x.get("guarded"), x.get("comboBefore"), x.get("comboAfter")) for x in g_first],
                                                   "later": [(x["targetId"], x["simulationTick"], x.get("damageTaken"), x.get("comboBefore"), x.get("comboAfter")) for x in g_later],
                                                   "guardEnd": [(x["simulationTick"], x["reason"]) for x in g_end], "reopen": [x["simulationTick"] for x in reo],
                                                   "newChainComboHits": later_end.get("comboHits"), "pass": guard1}
    out["TC-04-06"]["noGuardControl"] = {"first": [(x["targetId"], x["simulationTick"], x.get("comboAfter")) for x in n_first],
                                         "later": [(x["targetId"], x["simulationTick"], x.get("comboAfter")) for x in n_later], "pass": guard0}
    out["TC-04-06"]["pass"] = out["TC-04-06"]["pass"] and guard1 and guard0

    # ---- TC-04-08: initial dive hits one (closest), upgraded dive hits the configured area (R-04-08, U-05)
    def dive(name):
        s = seg.get(name, [])
        a = next((x for x in of(s, "attack.start") if x.get("attackId") == "dive"), None)
        return hits_of(s, a["attackInstanceId"]) if a else [], s
    dh1, s1 = dive("InitialDive")
    dh2, s2 = dive("AreaDive")
    land2 = next(iter(of(s2, "motor.land")), None)
    area = [x for x in dh2 if str(x.get("how", "")).startswith("DiveArea")]
    contact = [x for x in dh2 if x.get("how") == "Overlap"]
    out["TC-04-08"] = {"initial": [(x["targetId"], x.get("how")) for x in dh1], "area": [(x["targetId"], x.get("how"), x["simulationTick"]) for x in dh2],
                       "landTick": land2["simulationTick"] if land2 else None,
                       "pass": len(dh1) == 1 and dh1[0]["targetId"] == "d1" and dh1[0].get("how") == "Overlap" and effective(dh1[0])
                       and len({x["targetId"] for x in dh2}) == 3 and len(dh2) == 3 and len(contact) == 1 and len(area) == 2 and land2 is not None
                       and all(x["simulationTick"] == land2["simulationTick"] for x in area) and contact[0]["simulationTick"] < land2["simulationTick"]
                       and all(effective(x) for x in dh2)}

    # ---- TC-04-03: guard cancel / miss / timeout / RB cancel each end the chain; the next attack starts a new chain (R-04-03)
    rows = []
    def ended(name, reason, want_hits):
        s = seg.get(name, [])
        st = of(s, "attack.start")
        ce = of(s, "combo.end")
        first = st[0] if st else None
        e = ce[0] if ce else None
        nxt = next_start_after(ev, e) if e is not None else None
        ok = (first is not None and e is not None and str(e["reason"]).startswith(reason) and e.get("chainId") == first.get("chainId")
              and nxt is not None and nxt.get("chainId") != first.get("chainId") and nxt.get("attackChainStep") == 1 and nxt.get("newChain") is True)
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

    # R1 matrix (GPT WP-06 review 5.3): ground normal and launcher; move held from start / middle / just before the end, released and
    # pressed again after the end; jump at the same three points. Every press inside the motion is linked to the motion by tick.
    mat = []
    for name in R1_MATRIX:
        sm = seg.get(name, [])
        a = next(iter(of(sm, "attack.start")), None)
        e = next((x for x in of(sm, "attack.end") if a and x.get("attackInstanceId") == a["attackInstanceId"]), None)
        row = {"case": name, "attack": a.get("attackId") if a else None}
        ok = a is not None and e is not None and a.get("attackId") == ("ground1" if "Ground" in name else "launcher")
        if ok and name.startswith("Move"):
            press = [x for x in sm if x["type"] == "input" and x.get("action") == "MoveRight" and x.get("edge") == "Press"]
            mtk = [t for t in ticks(sm) if a["simulationTick"] <= t["simulationTick"] < e["simulationTick"]]
            xs = {round(t["x"], 4) for t in mtk}
            walk = [t for t in ticks(sm) if t["simulationTick"] > e["simulationTick"] and t.get("stateAfter") == "Walk"]
            p0 = press[0] if press else None
            row.update({"pressOffsetTicks": p0["simulationTick"] - a["simulationTick"] if p0 else None, "motionTicks": e["simulationTick"] - a["simulationTick"],
                        "xDuringMotion": sorted(xs), "firstWalkAfterEnd": walk[0]["simulationTick"] - e["simulationTick"] if walk else None})
            ok = ok and p0 is not None and a["simulationTick"] <= p0["simulationTick"] < e["simulationTick"] and len(xs) == 1 and bool(mtk) \
                and all(t.get("stateAfter") == "GroundAttack" for t in mtk) and bool(walk) and walk[-1]["x"] > mtk[0]["x"]
            if "Repress" in name:
                rel = [x for x in sm if x["type"] == "input" and x.get("action") == "MoveRight" and x.get("edge") == "Release"]
                ok = ok and len(press) == 2 and bool(rel) and rel[0]["simulationTick"] < e["simulationTick"] < press[1]["simulationTick"] <= walk[0]["simulationTick"]
                row["repressAfterEnd"] = press[1]["simulationTick"] - e["simulationTick"] if len(press) == 2 else None
            else:
                ok = ok and walk[0]["simulationTick"] == e["simulationTick"] + 1 and all("Right" in str(t.get("heldDir")) for t in mtk if t["simulationTick"] >= p0["simulationTick"])
        elif ok:
            rj = [x for x in of(sm, "input.reject") if x.get("action") == "Jump" and x["reason"] == "ActionLock:GroundAttack"]
            jp = [x for x in sm if x["type"] == "input" and x.get("action") == "Jump" and x.get("edge") == "Press"]
            jm = of(sm, "motor.jump")
            row.update({"rejectOffsetTicks": rj[0]["simulationTick"] - a["simulationTick"] if rj else None, "motionTicks": e["simulationTick"] - a["simulationTick"],
                        "jumpAfterEnd": jm[0]["simulationTick"] - e["simulationTick"] if jm else None})
            ok = ok and len(rj) == 1 and len(jp) == 2 and rj[0].get("inputEventId") == jp[0].get("inputEventId") \
                and a["simulationTick"] <= rj[0]["simulationTick"] <= e["simulationTick"] and len(jm) == 1 and jm[0]["simulationTick"] > e["simulationTick"] \
                and jm[0].get("inputEventId") == jp[1].get("inputEventId")  # R2 (GPT WP06-M01-R1): the jump after the end is the second press
        row["pass"] = ok
        mat.append(row)
    phases_ok = True
    for atk in ("Ground", "Launcher"):
        for kind, key in (("Move", "pressOffsetTicks"), ("Jump", "rejectOffsetTicks")):
            rows_ = [r for r in mat if r["case"].startswith(kind + atk + "+")]
            offs = [r.get(key) for r in rows_]
            spans = [r.get("motionTicks") for r in rows_]
            # start (<= 2 ticks), middle, and the last 3 ticks before the end are each covered
            phases_ok = phases_ok and len(offs) == 3 and None not in offs and offs[0] <= 2 and 3 <= offs[1] <= spans[1] - 4 and offs[2] >= spans[2] - 3
    out["TC-05-01"]["matrix"] = mat
    out["TC-05-01"]["matrixPhasesCovered"] = phases_ok
    out["TC-05-01"]["pass"] = out["TC-05-01"]["pass"] and all(r["pass"] for r in mat) and phases_ok

    # R2 (GPT WP-06~08 R1 review TC-05-01): air normal / air dash / dive. The attack's own motion and gravity are told apart from
    # steering by the control (same attack, no extra input): x relative to the attack start must match the control tick by tick
    # until the motion ends. A held Left changes nothing before the end and steers after it; a jump inside the motion is refused
    # with the motion's lock (linked to its press) and a second press after the end executes (linked by input id).
    air_rows, air_ok = [], True
    for atk, (aid, lock) in AIR.items():
        def motion(name):
            sm = seg.get(f"Air{atk}_{name}", [])
            a = next((x for x in of(sm, "attack.start") if x.get("attackId") == aid), None)
            e = next((x for x in of(sm, "attack.end") if a and x.get("attackInstanceId") == a["attackInstanceId"]), None)
            tk = {t["simulationTick"]: t for t in ticks(sm)}
            if not a or not e or a["simulationTick"] not in tk:
                return sm, a, e, None, tk
            x0 = tk[a["simulationTick"]]["x"]
            d = [round(tk[k]["x"] - x0, 4) if k in tk else None for k in range(a["simulationTick"], e["simulationTick"] + 7)]
            return sm, a, e, d, tk
        _, ca, ce, cd, _ = motion("Control")
        ctrl_ok = ca is not None and ce is not None and cd is not None and None not in cd and not of(seg.get(f"Air{atk}_Control", []), "input.reject")
        n = ce["simulationTick"] - ca["simulationTick"] if ctrl_ok else None
        offs = {"Move": [], "Jump": []}
        for case in ("Movestart", "Movemid", "Moveend", "Jumpstart", "Jumpmid", "Jumpend"):
            sm, a, e, d, tk = motion(case)
            row = {"attack": atk, "case": case}
            ok = ctrl_ok and a is not None and e is not None and d is not None and e["simulationTick"] - a["simulationTick"] == n
            if ok:
                within = d[:n + 1] == cd[:n + 1]
                row.update({"motionTicks": n, "sameAsControlDuringMotion": within})
                if case.startswith("Move"):
                    p = next((x for x in sm if x["type"] == "input" and x.get("action") == "MoveLeft" and x.get("edge") == "Press"), None)
                    off = p["simulationTick"] - a["simulationTick"] if p else None
                    held = [tk[k].get("heldDir") for k in range(p["simulationTick"], e["simulationTick"] + 1) if k in tk] if p else []
                    steer = [k for k in range(n + 1, n + 7) if d[k] is not None and cd[k] is not None and d[k] < cd[k] - 1e-3]
                    row.update({"pressOffset": off, "heldLeftInMotion": all("Left" in str(h) for h in held), "steersAfterEndAtOffset": steer[:1]})
                    ok = within and p is not None and 0 <= off < n and bool(held) and row["heldLeftInMotion"] and bool(steer) \
                        and not [x for x in sm if x["type"] == "input.reject" and x.get("action") == "MoveLeft"]
                    offs["Move"].append(off)
                else:
                    jp = [x for x in sm if x["type"] == "input" and x.get("action") == "Jump" and x.get("edge") == "Press" and x["simulationTick"] >= a["simulationTick"]]
                    rj = [x for x in of(sm, "input.reject", lock) if x.get("action") == "Jump"]
                    ex = [x for x in sm if x["type"] in ("air.use", "motor.jump") and x["simulationTick"] > a["simulationTick"]]
                    off = rj[0]["simulationTick"] - a["simulationTick"] if rj else None
                    row.update({"rejectOffset": off, "afterEnd": [(x["type"], x["reason"], x["simulationTick"] - e["simulationTick"]) for x in ex]})
                    ok = within and len(jp) == 2 and len(rj) == 1 and rj[0].get("inputEventId") == jp[0].get("inputEventId") and 0 <= off <= n \
                        and bool(ex) and all(x["simulationTick"] > e["simulationTick"] for x in ex) and ex[0].get("inputEventId") == jp[1].get("inputEventId")
                    offs["Jump"].append(off)
            row["pass"] = ok
            air_ok = air_ok and ok
            air_rows.append(row)
        for kind, o in offs.items():  # start (<= 2), middle, last 3 ticks before the end
            cov = n is not None and len(o) == 3 and None not in o and o[0] <= 2 and 3 <= o[1] <= n - 3 and n - 3 <= o[2] < n
            air_rows.append({"attack": atk, "case": kind + "PhasesCovered", "offsets": o, "motionTicks": n, "pass": cov})
            air_ok = air_ok and cov
    out["TC-05-01"]["airMatrix(R2)"] = air_rows
    out["TC-05-01"]["pass"] = out["TC-05-01"]["pass"] and air_ok

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
                       and bx[0].get("execGameTime") is not None and abs(bx[0]["execGameTime"] - bx[0]["gameTime"]) < 1e-6
                       and any(a["simulationTick"] == bx[0]["simulationTick"] and a.get("inputEventId") == buf[0].get("inputEventId") for a in of(s, "attack.start"))
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
                   and e["reason"] in ("AirDashEnd", "DiveEnd") and rj[0].get("inputEventId") == guard_in[0].get("inputEventId")
                   and a is not None and a["simulationTick"] <= rj[0]["simulationTick"] < e["simulationTick"] and rj[0].get("stateBefore") == state
                   and guard_in[0]["simulationTick"] <= rj[0]["simulationTick"]})
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
        same = all(abs(a["gapAfterAttackEnd"] - b["gapAfterAttackEnd"]) <= 0.021 for a, b in zip(small["pairs"], large["pairs"])) if None not in gaps else False
        ok = (same and max(gaps) >= large["window"] - 0.021 and small["window"] < large["window"] and all(g is not None and small["window"] < g <= large["window"] for g in gaps)
              and all(not p["sameChain"] for p in small["pairs"]) and all(p["sameChain"] and p["step2"] == 2 for p in large["pairs"]))
    return {"TC-04-09": {"runs": rows, "pass": ok}}


def selftest(ev):
    base = check(ev)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]

    def mutate(name, key, fn):
        m = copy.deepcopy(ev)
        try:
            fn(m)
        except (StopIteration, KeyError, IndexError) as x:
            res.append({"case": name, "section": key, "sectionPass": None, "pass": None, "note": f"record to mutate not present ({x!r})"})
            return
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

    # GPT WP-06 review M01: the 8 counterexamples it found passing
    def foreign_target(m):
        h = next(e for e in segments(m)["TenTargetsStandardChain"] if e["type"] == "hit")
        h["targetId"] = "zz"
    mutate("M01 TC-04-04 one target replaced by an id the other attacks never hit", "TC-04-04", foreign_target)

    def first_guarded(m):
        h = next(e for e in segments(m)["TenTargetsStandardChain"] if e["type"] == "hit")
        h["guarded"] = True
        h["hpAfter"] = h["hpBefore"]
        h["damageTaken"] = 0
    mutate("M01 TC-04-04 first hit guarded with no damage, count kept", "TC-04-04", first_guarded)

    def end_before_hit(m):
        s = segments(m)["InitialDash"]
        h = next(e for e in s if e["type"] == "hit")
        e = next(x for x in s if x["type"] == "attack.end")
        e["simulationTick"] = h["simulationTick"] - 1
    mutate("M01 TC-04-06 initial dash ends 1 tick before its hit", "TC-04-06", end_before_hit)

    def area_as_contact(m):
        for e in segments(m)["AreaDive"]:
            if e["type"] == "hit": e["how"] = "Overlap"
    mutate("M01 TC-04-08 landing-area hits turned into contact hits", "TC-04-08", area_as_contact)

    def rb_after_end(m):
        s = segments(m)["RbAtDiveMid"]
        e = next(x for x in s if x["type"] == "attack.end")
        for x in s:
            if (x["type"] == "input" and x.get("action") == "Guard") or (x["type"] == "input.reject" and x.get("action") == "Guard"):
                x["simulationTick"] = e["simulationTick"] + 2
    mutate("M01 TC-05-03 RB press and reject moved 2 ticks after the dive ended", "TC-05-03", rb_after_end)

    def no_exec_time(m):
        for e in segments(m)["BufferedPress"]:
            if e["type"] == "attack.bufferExecuted": e.pop("execGameTime", None)
    mutate("M01 TC-05-06 execGameTime removed", "TC-05-06", no_exec_time)

    def ground_hit_before_launch(m):
        s = segments(m)["LaunchThenStayOnGround"]
        la = next(e for e in s if e["type"] == "attack.start" and e.get("attackId") == "launcher")
        g = [e for e in s if e["type"] == "hit" and str(e.get("attackId", "")).startswith("ground")]
        g[0]["simulationTick"] = la["simulationTick"] - 3
    mutate("M01 TC-04-02 ground hit moved before the launcher start", "TC-04-02", ground_hit_before_launch)

    def miss_next_old_chain(m):
        s = segments(m)["MissEndsChain"]
        ce = next(e for e in s if e["type"] == "combo.end")
        nx = next_start_after(m, ce)
        nx["chainId"] = ce["chainId"]
        nx["newChain"] = False
    mutate("TC-04-03 attack after a miss (outside the marker) reuses the old chain", "TC-04-03", miss_next_old_chain)

    # R1 cases
    def guard_swallows(m):
        for e in segments(m)["GuardThenPierce"]:
            if e["type"] == "hit" and e["targetId"] == "gq": e["comboAfter"] = e["comboBefore"]
    mutate("R1 WP06-F01 later effective hit not counted", "TC-04-06", guard_swallows)

    def guard_no_end(m):
        drop = {id(e) for e in segments(m)["GuardThenPierce"] if e["type"] == "combo.end" and str(e["reason"]).startswith("EnemyGuardCancel")}
        m[:] = [e for e in m if id(e) not in drop]
    mutate("R1 WP06-F01 guard does not end the chain", "TC-04-06", guard_no_end)

    def five_as_four(m):
        h = [e for e in segments(m)["FiveTargetsOneAttack"] if e["type"] == "hit"]
        m[:] = [e for e in m if e is not h[-1]]
    mutate("R1 TC-04-04 one attack reaches only 4 of 5", "TC-04-04", five_as_four)

    def moved_mid(m):
        s = segments(m)["MoveLauncher+12"]
        a = next(e for e in s if e["type"] == "attack.start")
        t = [e for e in s if e["type"] == "tick" and e["simulationTick"] == a["simulationTick"] + 8]
        t[0]["x"] += 0.07
    mutate("R1 TC-05-01 launcher moves after a middle press", "TC-05-01", moved_mid)

    def jump_inside(m):
        s = segments(m)["JumpGround+22"]
        a = next(e for e in s if e["type"] == "attack.start")
        j = next(e for e in s if e["type"] == "motor.jump")
        j["simulationTick"] = a["simulationTick"] + 5
    mutate("R1 TC-05-01 jump executed during the motion", "TC-05-01", jump_inside)

    # GPT WP-06~08 R1 review §3 (WP06-M01-R1)
    def seg_first(m, name, typ, pred=lambda e: True):
        return next(e for e in segments(m)[name] if e["type"] == typ and pred(e))
    mutate("GPT R1 reopen claims a wrong guard tick", "TC-04-06", lambda m: seg_first(m, "GuardThenPierce", "combo.reopen").__setitem__("guardEndTick", -500))
    mutate("GPT R1 new chain never begins", "TC-04-06",
           lambda m: m.remove(seg_first(m, "GuardThenPierce", "combo.begin", lambda e: "PostGuardEffectiveHit" in e["reason"])))
    mutate("GPT R1 jump after the end has another input id", "TC-05-01", lambda m: seg_first(m, "JumpGround+2", "motor.jump").__setitem__("inputEventId", 99999))

    # R2 air matrix
    def air_steer(m):  # the dash moves with the held Left during its motion
        s = segments(m)["AirAirDash_Movemid"]
        a = next(e for e in s if e["type"] == "attack.start")
        for e in s:
            if e["type"] == "tick" and a["simulationTick"] + 8 <= e["simulationTick"] <= a["simulationTick"] + 12: e["x"] -= 0.05
    mutate("R2 TC-05-01 air dash steered by Left during the motion", "TC-05-01", air_steer)
    mutate("R2 TC-05-01 jump during the air normal executes", "TC-05-01",
           lambda m: seg_first(m, "AirAirNormal_Jumpmid", "input.reject").update({"type": "air.use", "reason": "ExtraJump"}))
    mutate("R2 TC-05-01 dive jump reject from another press", "TC-05-01", lambda m: seg_first(m, "AirDive_Jumpend", "input.reject").__setitem__("inputEventId", 99999))
    mutate("R2 TC-05-01 dive move pressed after the end", "TC-05-01",
           lambda m: seg_first(m, "AirDive_Moveend", "input", lambda e: e.get("action") == "MoveLeft" and e.get("edge") == "Press").__setitem__(
               "simulationTick", seg_first(m, "AirDive_Moveend", "attack.end")["simulationTick"] + 1))
    def no_steer_after(m):  # Left held but the air normal never steers after its end
        s = segments(m)["AirAirNormal_Moveend"]
        c = {e["simulationTick"] - next(x for x in segments(m)["AirAirNormal_Control"] if x["type"] == "attack.start")["simulationTick"]: e["x"]
             for e in segments(m)["AirAirNormal_Control"] if e["type"] == "tick"}
        a = next(e for e in s if e["type"] == "attack.start")
        for e in s:
            if e["type"] == "tick" and (e["simulationTick"] - a["simulationTick"]) in c:
                e["x"] = c[e["simulationTick"] - a["simulationTick"]] - next(x for x in segments(m)["AirAirNormal_Control"] if x["type"] == "tick" and x["simulationTick"] == next(y for y in segments(m)["AirAirNormal_Control"] if y["type"] == "attack.start")["simulationTick"])["x"] + next(x for x in s if x["type"] == "tick" and x["simulationTick"] == a["simulationTick"])["x"]
    mutate("R2 TC-05-01 held Left never steers after the air normal", "TC-05-01", no_steer_after)
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
            # GPT WP-06 review M01: the large-window run's two measured gaps changed to 0.40 / 0.50 s
            m2 = copy.deepcopy(runs)
            big = max(m2, key=lambda ev: next((e.get("window") for e in ev if e["type"] == "combo.window"), 0))
            st = [e for e in segments(big)["ComboWindowBoundary"] if e["type"] == "attack.start"]
            for k, gap in ((1, 0.40), (3, 0.50)):
                a_prev = st[k - 1]
                end = next(e for e in big if e["type"] == "attack.end" and e.get("attackInstanceId") == a_prev.get("attackInstanceId"))
                press = next(e for e in big if e["type"] == "input" and e.get("inputEventId") == st[k].get("inputEventId") and e.get("edge") == "Press")
                press["gameTime"] = end["gameTime"] + gap
            out["selftest"].append({"case": "M01 large-window gaps 0.40 / 0.50 s", "sectionPass": cw_check(m2)["TC-04-09"]["pass"]})
            for r in out["selftest"]: r["pass"] = r["sectionPass"] is False
            out["selftest_all_pass"] = all(r["pass"] for r in out["selftest"])
    else:
        ev = load(args[0])
        out = {"log": args[0], **check(ev)}
        if "--selftest" in sys.argv:
            out["selftest"] = selftest(ev)
            out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
