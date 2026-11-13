"""WP-11A-R1 log checks (GPT WP-11A review): the four TCs left INSUFFICIENT_EVIDENCE, on the WP11_R1 scene.
  TC-08-02     the player re-enters a still chaser's range while it is attacking, in its re-attack wait, staggered, down
  TC-08-12     the gauntlet boss parried with the player in the air, in each of its three states
  TC-UNITY-02  rendered mesh bounds vs judged bodies (view.bodyCheck) in the main run, and the view-only fault control run
  TC-UNITY-07  hit candidates linked to the hits; late external hit signals judged from the core's own state, never applied

Usage:
  python Tools/wp11r1_checks.py <main run>.ndjson <fault run>.ndjson [--selftest]
"""
import copy
import importlib.util
import json
import os
import re
import sys

_here = os.path.dirname(os.path.abspath(globals().get("__file__", "D:/ProjectR/Corum2Remake/Tools/x")))
_spec = importlib.util.spec_from_file_location("wp11_checks", os.path.join(_here, "wp11_checks.py"))
w11 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(w11)
of, ticks, enemy, attr_ok, load = w11.of, w11.ticks, w11.enemy, w11.attr_ok, w11.load

DT = 0.02
RANGE = 1.6
A2 = [("A2R1Attack", "ra", ("AttackActive", "AttackRecovery")), ("A2R1Wait", "rw", ("Idle",)),
      ("A2R1Stagger", "rs", ("Stagger",)), ("A2R1Down", "rd", ("Down",))]
GAIR = ["GAirg1", "GAirg2", "GAirg3"]
SCENARIOS = [a[0] for a in A2] + GAIR + ["U2BodyRun", "U2BodyDash", "U2BodyDive", "U2BodyKnock", "U2BodyEnemy", "U7Late", "U7LateCast", "ScriptEnd"]
PHASE = {"g1": (False, "Launchable"), "g2": (True, "Launchable"), "g3": (False, "Immune")}
PLAYER_CLASSES = ("walkRun", "air", "airDash", "dive", "knockback", "hitStun")
ENEMY_CLASSES = ("walk", "knockback", "air", "down", "dead")
VERDICTS = ("Rejected:NotActive:Startup", "Rejected:NotActive:Recovery", "Rejected:StaleInstance:Ended", "Rejected:StaleInstance:Cancelled",
            "Accepted:SameAsCollectedCandidate", "Rejected:AlreadyHitThisEvent", "Rejected:NoOverlap", "Rejected:NoAttackInstance")


def segments(ev):
    marks = [(e["eventId"], e["reason"]) for e in ev if e["type"] == "marker" and e["reason"] in SCENARIOS]
    seg = {}
    for i, (eid, name) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else float("inf")
        seg[name] = [e for e in ev if eid <= e["eventId"] < end]
    return seg


# ------------------------------------------------------------------ TC-08-02
def tc0802(seg):
    rows = []
    for name, eid, entry_states in A2:
        s = seg.get(name, [])
        tk = ticks(s)
        track = [(t, enemy(tk[t], eid)["state"], round(abs(tk[t]["x"] - enemy(tk[t], eid)["x"]), 3)) for t in sorted(tk) if enemy(tk[t], eid)]
        starts = [e for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateAfter") == "AttackStartup"]
        blocked = [e for e in of(s, "ai.blocked") if e.get("actorId") == eid]
        first = starts[0] if starts else None
        # entries: a tick in range after a tick out of range, after the first attack (the case's first entry starts it)
        entries = [(t, st, d) for (t0, s0, d0), (t, st, d) in zip(track, track[1:]) if first and t > first["simulationTick"] and d0 > RANGE and d <= RANGE]
        ent = entries[0] if entries else None
        nxt = next((e for e in starts if ent and e["simulationTick"] > ent[0]), None)
        # every attack starts from Idle with the player in range on that tick
        st_at = {t: (state, d) for t, state, d in track}
        from_idle = all(e.get("stateBefore") == "Idle" and st_at.get(e["simulationTick"] - 1, ("", 99))[0] == "Idle" and st_at.get(e["simulationTick"], ("", 99))[1] <= RANGE + 0.01
                        for e in starts)
        row = {"case": name, "enemy": eid, "firstAttack": first and first["simulationTick"], "reentry": ent, "nextAttack": nxt and nxt["simulationTick"]}
        ok = first is not None and ent is not None and ent[1] in entry_states and nxt is not None and from_idle
        if ok:
            # from the re-entry to the next attack: the player stays in range and the enemy stays restricted or waits (no attack in between)
            between = [(t, state, d) for t, state, d in track if ent[0] <= t < nxt["simulationTick"]]
            free_from = next((t for t, state, d in between if state == "Idle"), None)
            row.update({"inRangeUntilNextAttack": all(d <= RANGE for t, state, d in between), "firstIdleTick": free_from,
                        "statesBetween": sorted({state for t, state, d in between})})
            ok = row["inRangeUntilNextAttack"]
            if name == "A2R1Wait":
                # re-entered while Idle and still inside the re-attack interval: the wait is logged as a cooldown block, the attack starts when it ends
                blk = next((e for e in blocked if ent[0] <= e["simulationTick"] < nxt["simulationTick"] and str(e.get("reason", "")).startswith("Cooldown:ReattackInterval")
                            and e.get("cooldown", 0) > 0 and e.get("distance", 99) <= e.get("attackRange", RANGE) + 1e-6), None)
                due = blk and blk["simulationTick"] + round(blk["cooldown"] / DT)
                row.update({"cooldownBlock": blk and (blk["simulationTick"], blk["cooldown"]), "attackDue": due})
                ok = ok and blk is not None and abs(nxt["simulationTick"] - due) <= 1 and nxt["simulationTick"] > ent[0] + 1
            else:
                # restricted at re-entry: no attack until the restriction ends; then the re-decision is an attack or a logged cooldown wait that ends in one
                end = free_from
                atk_at_free = end is not None and nxt["simulationTick"] - end in (0, 1)
                blk = next((e for e in blocked if end is not None and e["simulationTick"] - end in (0, 1) and str(e.get("reason", "")).startswith("Cooldown:ReattackInterval")
                            and e.get("cooldown", 0) > 0), None)
                due = blk and blk["simulationTick"] + round(blk["cooldown"] / DT)
                row.update({"restrictionEnds": end, "attackOnRelease": atk_at_free, "cooldownBlock": blk and (blk["simulationTick"], blk["cooldown"]), "attackDue": due})
                ok = ok and end is not None and end > ent[0] and (atk_at_free or (blk is not None and abs(nxt["simulationTick"] - due) <= 1))
        row["pass"] = ok
        rows.append(row)
    return {"TC-08-02": {"cases": rows, "note": "re-entry into range while restricted (attacking, re-attack wait, stagger 1.5 s, down 2.5 s test data); "
                                                "the ready entry and the dead case are in WP-11A", "pass": len(rows) == 4 and all(r["pass"] for r in rows)}}


# ------------------------------------------------------------------ TC-08-12 boss air parry
def tc0812(seg, ev):
    rows = []
    for name in GAIR:
        st = name[-2:]
        b = "a" + st
        s = seg.get(name, [])
        tk = ticks(s)
        ph = [e for e in of(ev, "boss.phase") if e.get("actorId") == b and e["eventId"] < (s[0]["eventId"] if s else 0)]
        phase = ph[-1] if ph else None
        p = next((e for e in of(s, "guard.parry") if e.get("targetId") == b), None)
        row = {"case": name, "phase": phase and phase["stateAfter"]}
        ok = p is not None and phase is not None and phase["stateAfter"] == st and (phase.get("superArmor"), phase.get("launch")) == PHASE[st]
        if ok:
            t = p["simulationTick"]
            inst = p.get("enemyAttackInstanceId")
            rec = [e for e in of(ev, "enemy.state") if e.get("actorId") == b and e.get("attackInstanceId") == inst and e["eventId"] < p["eventId"]]
            en = enemy(tk.get(t, {}), b) or {}
            jump = next((e for e in of(s, "motor.jump") if e["simulationTick"] < t), None)
            k = next((x for x in of(s, "enemy.knockback") if x.get("actorId") == b and x.get("startTick", -1) >= t), None)
            recov = [e for e in of(ev, "enemy.state") if e.get("actorId") == b and e.get("attackInstanceId") == inst and e.get("stateAfter") == "AttackRecovery"]
            row.update({"parryTick": t, "playerGroundedBefore": tk.get(t - 1, {}).get("grounded"), "playerGroundedOnTick": tk.get(t, {}).get("grounded"),
                        "playerY": tk.get(t, {}).get("y"), "jumpTick": jump and jump["simulationTick"], "bossAttackActive": bool(rec) and rec[-1].get("stateAfter") == "AttackActive",
                        "phaseOnTick": en.get("phase"), "bossOnTick": en.get("state"), "knockback": k and (k.get("startVel"), k.get("displacement")),
                        "rbToHit": p.get("rbToHit"), "parryWindow": p.get("parryWindow")})
            ok = tk.get(t - 1, {}).get("grounded") is False and tk.get(t, {}).get("grounded") is False and jump is not None \
                and not [e for e in of(s, "motor.land") if jump["simulationTick"] < e["simulationTick"] <= t] \
                and row["bossAttackActive"] and en.get("phase") == st and en.get("state") == "Stagger" and not recov \
                and p.get("rbToHit", 1) <= p.get("parryWindow", 0) and k is not None and attr_ok(k, "Parry", inst) and k["displacement"] > 0 \
                and w11.knock_consistent(k, ev, b) and not [e for e in of(s, "hit.player") if e["simulationTick"] == t]
        row["pass"] = ok
        rows.append(row)
    return {"TC-08-12": {"part": "boss parried in the air (R2 carry-over)", "cases": rows, "pass": len(rows) == 3 and all(r["pass"] for r in rows)}}


# ------------------------------------------------------------------ TC-UNITY-02 rendered mesh
def body_tables(ev):
    return {e["actorId"]: (w11.track_table(e.get("classes")), e) for e in of(ev, "view.bodyCheck")}


def tcu02(ev, fault):
    b, f = body_tables(ev), body_tables(fault)
    zero = lambda t: all(c["renderPen"] <= 1e-4 and c["size"] <= 1e-4 and c["outside"] <= 1e-4 for c in t.values())
    p, pe = b.get("player", ({}, {}))
    en, ee = b.get("enemies", ({}, {}))
    main = {"playerClasses": {k: v["frames"] for k, v in p.items()}, "enemyClasses": {k: v["frames"] for k, v in en.items()},
            "playerExcessFrames": pe.get("excessFrames"), "enemyExcessFrames": ee.get("excessFrames"),
            "playerClassesPresent": [c for c in PLAYER_CLASSES if p.get(c, {}).get("frames", 0) > 0],
            "enemyClassesPresent": [c for c in ENEMY_CLASSES if en.get(c, {}).get("frames", 0) > 0]}
    main_ok = bool(p) and bool(en) and zero(p) and zero(en) and pe.get("excessFrames") == 0 and ee.get("excessFrames") == 0 \
        and pe.get("faultOffsetX") == 0 and ee.get("faultOffsetX") == 0 \
        and len(main["playerClassesPresent"]) == len(PLAYER_CLASSES) and len(main["enemyClassesPresent"]) == len(ENEMY_CLASSES)
    # fault control: only the drawn bodies are shifted 0.3 m; every measured class must show it, and the judged side stays clean
    fp, fpe = f.get("player", ({}, {}))
    fen, fee = f.get("enemies", ({}, {}))
    shifted = lambda t: bool(t) and all(abs(c["outside"] - 0.3) <= 0.02 for k, c in t.items() if k != "dive") and all(c["simPen"] == 0 for c in t.values())
    ctrl = {"playerFault": fpe.get("faultOffsetX"), "enemyFault": fee.get("faultOffsetX"), "playerExcessFrames": fpe.get("excessFrames"), "enemyExcessFrames": fee.get("excessFrames"),
            "playerOutsideByClass": {k: v["outside"] for k, v in fp.items()}, "enemyOutsideByClass": {k: v["outside"] for k, v in fen.items()},
            "playerWallPenetrationMax": max([v["renderPen"] for v in fp.values()] or [0])}
    ctrl_ok = fpe.get("faultOffsetX") == 0.3 and fee.get("faultOffsetX") == 0.3 and fpe.get("excessFrames", 0) > 0 and fee.get("excessFrames", 0) > 0 \
        and shifted(fp) and shifted(fen) and ctrl["playerWallPenetrationMax"] > 0.25
    pc = {e["reason"].split("(")[0]: e for e in of(ev, "view.planeCheck")}
    pp, pz = pc.get("RenderedPlayerDepth"), pc.get("RenderedEnemyDepth")
    plane_ok = pp is not None and pz is not None and pp.get("maxAbsZError") == 0 and pz.get("maxAbsZError") == 0 and pp.get("planeZ") == pz.get("planeZ")
    return {"TC-UNITY-02": {"part": "rendered mesh bounds vs judged bodies (R1)", "main": main, "faultControl": ctrl, "planeCheck": plane_ok,
                            "pass": main_ok and ctrl_ok and plane_ok}}


# ------------------------------------------------------------------ TC-UNITY-07 candidates and late signals
def tcu07(seg, ev):
    starts = {e["attackInstanceId"]: e for e in ev if e.get("actorId") == "player" and e["type"] in ("attack.start", "magic.cast")}
    ends = {e["attackInstanceId"]: e for e in of(ev, "attack.end")}
    cands = {}
    for c in of(ev, "hit.candidates"):
        cands.setdefault(c["simulationTick"], set()).update(x for x in str(c.get("playerCandidates", "")).split(",") if x)
    hits = [e for e in of(ev, "hit") if e.get("actorId") == "player"]
    # every player hit resolves a candidate collected on its own tick (instance:target:hit event)
    def key(h):
        ev_ = str(h.get("hitEventId", "")).split(":")[-1]
        return f"{h['attackInstanceId']}:{h['targetId']}:{ev_}"
    # hits resolved outside ResolveHits (dive landing area) carry another 'how'; this run has none, and any other hit must link
    linked = [h for h in hits if key(h) in cands.get(h["simulationTick"], set())]
    unresolved = []
    dodged = {(d.get("playerAttackInstanceId"), d.get("targetId"), d["simulationTick"]) for d in of(ev, "dodge")}
    for t, cs in cands.items():
        for c in cs:
            inst, tg, _ = c.split(":")
            if not any(h["simulationTick"] == t and str(h["attackInstanceId"]) == inst and h["targetId"] == tg for h in hits) \
                    and not any(str(a) == inst and b == tg and tt == t for a, b, tt in dodged):
                unresolved.append((t, c))
    # late signals: the verdict follows from the log's own state at the judged tick
    sig = of(ev, "hit.signal.resolved")
    hitset = {(h["attackInstanceId"], h["targetId"]) for h in hits}

    def current(t):  # player attack alive on tick t (started at or before t, not yet ended before t)
        alive = [i for i, s in starts.items() if s["simulationTick"] <= t and (i not in ends or ends[i]["simulationTick"] >= t)]
        return alive[-1] if alive else None
    checked = []
    for r in sig:
        i, t, v = r["attackInstanceId"], r["simulationTick"], r["reason"]
        cur = current(t) if not (i in ends and ends[i]["simulationTick"] < t) else None
        if i == 0:
            exp = v == "Rejected:NoAttackInstance"
        elif i in ends and ends[i]["simulationTick"] <= t and cur != i:
            how = ("Cancelled:" if ends[i].get("hitboxRemoved") else "Ended:") + str(ends[i].get("reason"))
            exp = v == "Rejected:StaleInstance:" + how
        elif v.startswith("Rejected:NotActive:"):
            exp = cur == i and not any(h["attackInstanceId"] == i and h["simulationTick"] == t for h in hits) \
                and f"{i}:{r['targetId']}" not in {c.rsplit(':', 1)[0] for c in cands.get(t, set())}
        elif v == "Accepted:SameAsCollectedCandidate":
            exp = any(c.startswith(f"{i}:{r['targetId']}:") for c in cands.get(t, set())) and r.get("accepted") is True
        elif v == "Rejected:AlreadyHitThisEvent":
            exp = any(h["attackInstanceId"] == i and h["targetId"] == r["targetId"] and h["simulationTick"] < t for h in hits)
        elif v == "Rejected:NoOverlap":
            exp = (i, r["targetId"]) not in hitset
        else:
            exp = False
        checked.append({"tick": t, "instance": i, "target": r["targetId"], "verdict": v, "delayTicks": t - r.get("receivedTick", t),
                        "consistent": bool(exp) and r.get("appliedAsHit") is False and (r.get("accepted") is True) == v.startswith("Accepted")})
    covered = {k: any(c["verdict"].startswith(k) for c in checked) for k in VERDICTS}
    # no hit beyond the core's own: every hit is a collected candidate, one per (instance, target, event)
    keys = [key(h) for h in hits]
    glob = {"playerHits": len(hits), "hitsLinkedToCandidates": len(linked), "candidatesWithoutResolution": unresolved[:10], "uniqueHitKeys": len(set(keys)) == len(keys),
            "signals": len(checked), "signalsConsistent": sum(1 for c in checked if c["consistent"]), "verdictsCovered": covered,
            "lateSignalsApplied": sum(1 for r in sig if r.get("appliedAsHit") is not False)}
    cast = next((e for e in of(seg.get("U7LateCast", []), "magic.cast")), None)
    cancel = next((e for e in of(seg.get("U7LateCast", []), "magic.cancel")), None)
    late_cast = [c for c in checked if cast and c["instance"] == cast["attackInstanceId"]]
    glob["castCase"] = {"cast": cast and cast["attackInstanceId"], "cancelTick": cancel and cancel["simulationTick"],
                        "verdicts": [(c["tick"], c["verdict"]) for c in late_cast]}
    ok = len(hits) > 0 and len(linked) == len(hits) and not unresolved and glob["uniqueHitKeys"] and checked and all(c["consistent"] for c in checked) \
        and all(covered.values()) and glob["lateSignalsApplied"] == 0 and cancel is not None \
        and any(c["verdict"].startswith("Rejected:StaleInstance:Cancelled") and c["tick"] > cancel["simulationTick"] for c in late_cast) \
        and any(c["verdict"] == "Rejected:NotActive:Startup" and c["tick"] < cancel["simulationTick"] for c in late_cast)
    return {"TC-UNITY-07": {"part": "candidate -> resolution links and late signals (R1)", "wholeRun": glob, "signals": checked, "pass": ok}}


def check(ev, fault):
    seg = segments(ev)
    out = {}
    out.update(tc0802(seg))
    out.update(tc0812(seg, ev))
    out.update(tcu02(ev, fault))
    out.update(tcu07(seg, ev))
    return out


# ------------------------------------------------------------------ self-tests
def selftest(ev, fault):
    base = check(ev, fault)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]

    def mutate(name, key, fn, on_fault=False):
        m, f = copy.deepcopy(ev), copy.deepcopy(fault)
        try:
            fn(f if on_fault else m)
        except (StopIteration, KeyError, IndexError) as x:
            res.append({"case": name, "section": key, "sectionPass": None, "pass": None, "note": f"record to mutate not present ({x!r})"})
            return
        r = check(m, f)[key]["pass"]
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})

    def first(m, scn, typ, pred=lambda e: True):
        return next(e for e in segments(m)[scn] if e["type"] == typ and pred(e))

    def attack_while(scn, eid, state):
        def f(m):
            s = segments(m)[scn]
            t = next(e for e in s if e["type"] == "tick" and (enemy(e, eid) or {}).get("state") == state and e["simulationTick"] > first(m, scn, "enemy.state", lambda x: x.get("stateAfter") == "AttackStartup")["simulationTick"] + 30)
            x = copy.deepcopy(first(m, scn, "enemy.state", lambda e: e.get("actorId") == eid and e.get("stateAfter") == "AttackStartup"))
            x.update(simulationTick=t["simulationTick"], stateBefore=state, eventId=t["eventId"] + 0.5)
            m.insert(m.index(t) + 1, x)
        return f
    mutate("08-02 attack starts while staggered after re-entry", "TC-08-02", attack_while("A2R1Stagger", "rs", "Stagger"))
    mutate("08-02 attack starts while down after re-entry", "TC-08-02", attack_while("A2R1Down", "rd", "Down"))
    mutate("08-02 re-entry wait has no cooldown block", "TC-08-02", lambda m: [m.remove(e) for e in [e for e in segments(m)["A2R1Wait"] if e["type"] == "ai.blocked"]])
    def never_leaves(m):
        for e in segments(m)["A2R1Attack"]:
            if e["type"] == "tick" and enemy(e, "ra"):
                e["x"] = enemy(e, "ra")["x"] - 1.5
    mutate("08-02 the player never leaves the range", "TC-08-02", never_leaves)
    mutate("08-12 air parry on the ground", "TC-08-12",
           lambda m: next(e for e in m if e["type"] == "tick" and e["simulationTick"] == first(m, "GAirg2", "guard.parry")["simulationTick"] - 1).update(grounded=True))
    mutate("08-12 air parry names another boss attack", "TC-08-12", lambda m: first(m, "GAirg3", "guard.parry").update(enemyAttackInstanceId=-4))
    mutate("08-12 air parry in the wrong phase", "TC-08-12", lambda m: [e.update(stateAfter="g1", superArmor=False) for e in m if e["type"] == "boss.phase" and e.get("actorId") == "ag2"])
    mutate("08-12 parried boss keeps attacking", "TC-08-12",
           lambda m: m.insert(m.index(first(m, "GAirg1", "guard.parry")) + 1, dict(first(m, "GAirg1", "enemy.state", lambda e: e.get("stateAfter") == "AttackActive"),
                                                                                    stateAfter="AttackRecovery", eventId=first(m, "GAirg1", "guard.parry")["eventId"] + 0.5)))
    def body_off(m):
        e = next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "player")
        e["classes"] = re.sub(r"(airDash:\d+/[\d.]+/[\d.]+/[\d.]+/)[\d.]+", lambda x: x.group(1) + "0.0200", e["classes"])
    mutate("U-02 rendered player outside its tick segment in the dash", "TC-UNITY-02", body_off)
    mutate("U-02 rendered enemy size differs while down", "TC-UNITY-02", lambda m: next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "enemies").update(
        classes=re.sub(r"(down:\d+/[\d.]+/[\d.]+/)[\d.]+", lambda x: x.group(1) + "0.1000", next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "enemies")["classes"])))
    mutate("U-02 no dead enemy frames measured", "TC-UNITY-02", lambda m: next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "enemies").update(
        classes=re.sub(r"dead:\d+", "dead:0", next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "enemies")["classes"])))
    mutate("U-02 fault control does not react", "TC-UNITY-02", lambda m: next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "player").update(
        classes=re.sub(r"(\w+:\d+/[\d.]+/[\d.]+/[\d.]+/)[\d.]+", lambda x: x.group(1) + "0.0000", next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "player")["classes"]),
        excessFrames=0), on_fault=True)
    mutate("U-02 main run has excess frames", "TC-UNITY-02", lambda m: next(x for x in m if x["type"] == "view.bodyCheck" and x["actorId"] == "enemies").update(excessFrames=3))
    mutate("U-07 late signal applied as a hit", "TC-UNITY-07", lambda m: first(m, "U7Late", "hit.signal.resolved", lambda e: "Stale" in e["reason"]).update(appliedAsHit=True))
    mutate("U-07 stale signal accepted", "TC-UNITY-07",
           lambda m: first(m, "U7LateCast", "hit.signal.resolved", lambda e: "Cancelled" in e["reason"]).update(reason="Accepted:SameAsCollectedCandidate", accepted=True))
    mutate("U-07 startup signal judged after the cancel", "TC-UNITY-07",
           lambda m: first(m, "U7LateCast", "hit.signal.resolved", lambda e: "Startup" in e["reason"]).update(
               simulationTick=first(m, "U7LateCast", "magic.cancel")["simulationTick"] + 3))
    mutate("U-07 hit without a collected candidate", "TC-UNITY-07", lambda m: m.remove(first(m, "U7Late", "hit.candidates")))
    mutate("U-07 recovery signal called startup", "TC-UNITY-07",
           lambda m: first(m, "U7Late", "hit.signal.resolved", lambda e: e["reason"] == "Rejected:NotActive:Recovery").update(reason="Rejected:NoOverlap", targetId="lb"))
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    w11.DATA_DIRS[:] = sorted({os.path.dirname(os.path.abspath(a)) for a in args})
    ev, fault = load(args[0]), load(args[1])
    out = {"logs": args[:2], **check(ev, fault)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(ev, fault)
        out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
