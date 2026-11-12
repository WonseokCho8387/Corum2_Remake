"""WP-07 log checks: P0-06 hit / reaction TCs TC-06-01..10 and the TC-06-03 Inspector hitstop run.

Usage:
  python Tools/wp07_checks.py <main run>.ndjson [--selftest]
  python Tools/wp07_checks.py --hs <main run>.ndjson <hitstop run>.ndjson [--selftest]

Sections are cut by scenario markers ("mark=" in the script). Enemy state, position and HP come from the per-tick
"enemies" field ("id:State@x,y hpN|..."). The self-test applies counterexample mutations to a copy of the log.
"""
import copy
import json
import re
import sys

SCENARIOS = ["SameMotionOverlap", "ThreeTargetsAndThreeEvents", "HitstopOneTarget", "HitstopTenTargets", "Reactions_k1", "Magic_k1", "Reactions_k2",
             "Magic_k2", "Reactions_k3", "Magic_k3", "Reactions_k4", "Magic_k4", "LaunchAirFour", "LaunchResist", "LaunchImmune",
             "LaunchAirTwoThenStop", "DownAttackBothSides", "DiveOnDowned", "WakeupPhysical", "WakeupMagic",
             # R1 (GPT WP-07 review): weak magic / equal-to-one-tick knockback, wakeup boundaries reached by an attack already active
             "MagicWeak_mw", "MagicEqual_me", "PhysEqual_pe", "WakeupStartBoundary", "WakeupEndBoundary", "ScriptEnd"]


def effective(h):
    return (h.get("hpAfter") is not None and h.get("hpBefore") is not None and h["hpAfter"] < h["hpBefore"]
            and h.get("comboAfter") == h.get("comboBefore", -99) + 1)


def state_at(tr, tick):
    return next((st for t, st, *_ in tr if t == tick), None)


def moved_after(s, eid, h, ticks=40):
    """Real displacement of eid caused by hit h: the full-precision enemy.knockback record (start -> stop of the knockback
    velocity) that begins on the tick after the hit (R1). Per-tick positions are rounded to 2 decimals and are not used."""
    k = next((x for x in s if x["type"] == "enemy.knockback" and x.get("actorId") == eid and h["simulationTick"] < x.get("startTick", -1) <= h["simulationTick"] + 8
              and x["simulationTick"] <= h["simulationTick"] + ticks), None)
    if not k: return None
    # the rounded per-tick positions must agree with the record (2-decimal rounding: +-0.011)
    tr = {t: x for t, _, x, _, _ in enemy_track(s, eid)}
    x0, x1 = tr.get(k["startTick"] - 1), tr.get(k["simulationTick"])
    if x0 is None or x1 is None or abs((x1 - x0) - k["displacement"]) > 0.011: return -1.0
    return abs(k["displacement"])
ENEMY = re.compile(r"([^:|]+):([A-Za-z]+)@(-?[\d.]+),(-?[\d.]+) hp(-?[\d.]+)")


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


def enemy_track(s, eid):
    out = []
    for t in s:
        if t["type"] != "tick": continue
        for m in ENEMY.finditer(str(t.get("enemies", ""))):
            if m.group(1) == eid:
                out.append((t["simulationTick"], m.group(2), float(m.group(3)), float(m.group(4)), float(m.group(5))))
    return out


def hits_by_attack(s):
    d = {}
    for h in of(s, "hit"): d.setdefault(h.get("attackInstanceId"), []).append(h)
    return d


def check(ev):
    seg = segments(ev)
    out = {}

    # ---- TC-06-01: one motion overlapping a target for several ticks hits it once; the next motion hits again (R-06-01)
    s = seg.get("SameMotionOverlap", [])
    st = of(s, "attack.start")
    hb = hits_by_attack(s)
    rows = []
    for a in st:
        hs = hb.get(a["attackInstanceId"], [])
        e = next((x for x in of(s, "attack.end") if x.get("attackInstanceId") == a["attackInstanceId"]), {})
        rows.append({"attack": a.get("attackId"), "instance": a["attackInstanceId"], "hits": [(h["targetId"], h.get("comboAfter")) for h in hs],
                     "motionTicks": e.get("simulationTick", 0) - a["simulationTick"]})
    track = enemy_track(s, "o1")
    out["TC-06-01"] = {"attacks": rows, "pass": len(rows) == 2 and all(len(r["hits"]) == 1 and r["hits"][0][0] == "o1" for r in rows)
                       and rows[0]["instance"] != rows[1]["instance"] and [r["hits"][0][1] for r in rows] == [1, 2]
                       and all(effective(h) for a in st for h in hb.get(a["attackInstanceId"], []))}

    # ---- TC-06-02: single attack on 3 -> once each; explicit 3-event test attack -> once per event per target (R-06-01/02)
    s = seg.get("ThreeTargetsAndThreeEvents", [])
    st = of(s, "attack.start")
    hb = hits_by_attack(s)
    single = [a for a in st if a.get("attackId") in ("ground1", "ground2", "ground3")]
    multi = next((a for a in st if a.get("attackId") == "ground4"), None)
    sing_ok = bool(single) and all(sorted(h["targetId"] for h in hb.get(a["attackInstanceId"], [])) == ["m1", "m2", "m3"] for a in single)
    mh = hb.get(multi["attackInstanceId"], []) if multi else []
    pairs = sorted((h["hitEventId"], h["targetId"]) for h in mh)
    events = sorted({h["hitEventId"] for h in mh})
    out["TC-06-02"] = {"singleAttacks": [(a.get("attackId"), sorted(h["targetId"] for h in hb.get(a["attackInstanceId"], []))) for a in single],
                       "threeEventAttack": {"events": events, "hits": len(mh), "uniquePairs": len(set(pairs))},
                       "pass": sing_ok and len(events) == 3 and len(mh) == 9 and len(set(pairs)) == 9
                       and all(sorted(h["targetId"] for h in mh if h["hitEventId"] == e) == ["m1", "m2", "m3"] for e in events)
                       and multi is not None and events == ["%d:%d" % (multi["attackInstanceId"], k) for k in range(3)]
                       and [min(h["simulationTick"] for h in mh if h["hitEventId"] == e) for e in events] == sorted(min(h["simulationTick"] for h in mh if h["hitEventId"] == e) for e in events)
                       and len({min(h["simulationTick"] for h in mh if h["hitEventId"] == e) for e in events}) == 3}

    # ---- TC-06-03 (main part): one stop per hit event for 10 targets; the motion length equals the single-target case (R-06-03)
    out["TC-06-03-main"] = hitstop_section(ev)

    # ---- TC-06-04 / 05 / 06: per-enemy stagger refresh / resistance, physical knockback by size, magic knockback > 0
    react, kb, mag = [], [], []
    for eid in ("k1", "k2", "k3", "k4"):
        s = seg.get("Reactions_" + eid, [])
        hs = [h for h in of(s, "hit") if h["targetId"] == eid]
        tr = enemy_track(s, eid)
        stag = [h for h in hs if h["reason"].startswith("Stagger(")]
        resisted = [h for h in hs if h["reason"].startswith("StaggerResisted")]
        x0 = tr[0][2] if tr else None
        xs = [x for _, _, x, _, _ in tr]
        react.append({"enemy": eid, "hits": len(hs), "stagger": len(stag), "staggerResisted": len(resisted),
                      "staggerStatesAfterHits": sum(1 for h in hs if h["stateAfter"] == "Stagger"),
                      "stateConsistent": all((h["stateAfter"] == "Stagger") == h["reason"].startswith("Stagger(") and state_at(tr, h["simulationTick"]) == h["stateAfter"] for h in hs)})
        kbv = [h.get("knockbackVel") for h in hs]
        kb.append({"enemy": eid, "knockbackVel": kbv, "maxDisplacement": round(max(xs) - x0, 3) if xs else None,
                   "lastHitVsFirst": (kbv[-1], kbv[0]) if kbv else None, "firstHitDisplacement": moved_after(s, eid, hs[0], 12) if hs else None})
        sm = seg.get("Magic_" + eid, [])
        mh = [h for h in of(sm, "hit") if h["targetId"] == eid and h.get("damageType") == "Magic"]
        mag.append(magic_row(sm, eid))
    for name, eid in (("MagicWeak_mw", "mw"), ("MagicEqual_me", "me")):
        mag.append(magic_row(seg.get(name, []), eid))
    by = {r["enemy"]: r for r in react}
    kbe = {r["enemy"]: r for r in kb}
    out["TC-06-04"] = {"enemies": react,
                       "pass": all(by[e]["hits"] == 6 for e in by) and by["k1"]["stagger"] == 6 and by["k2"]["stagger"] == 6 and by["k3"]["stagger"] == 6
                       and by["k4"]["staggerResisted"] == 6 and by["k4"]["staggerStatesAfterHits"] == 0 and all(by[e]["stateConsistent"] for e in by)}
    def mag_of(e): return [abs(v) for v in kbe[e]["knockbackVel"] if v is not None]
    # R1 (WP07-F01): equal-to-one-tick physical knockback (0.6 m/s) must also move
    sp = seg.get("PhysEqual_pe", [])
    ph = [h for h in of(sp, "hit") if h["targetId"] == "pe"]
    pe = {"knockbackVel": [h.get("knockbackVel") for h in ph], "firstHitDisplacement": moved_after(sp, "pe", ph[0], 12) if ph else None}
    out["TC-06-05"] = {"enemies": kb, "equalToOneTickDecel_pe": pe,
                       "pass": all(kbe[e]["knockbackVel"] for e in kbe) and max(mag_of("k1")) > max(mag_of("k2")) > max(mag_of("k3")) > 0
                       and max(mag_of("k4")) == 0 and kbe["k4"]["maxDisplacement"] == 0
                       and all(abs(kbe[e]["knockbackVel"][-1]) <= abs(kbe[e]["knockbackVel"][0]) for e in kbe)
                       and all((kbe[e]["firstHitDisplacement"] or 0) > 0 for e in ("k1", "k2", "k3")) and kbe["k4"]["firstHitDisplacement"] == 0
                       and len(ph) == 1 and abs(abs(ph[0].get("knockbackVel", 0)) - 0.6) < 1e-3 and (pe["firstHitDisplacement"] or 0) > 0}
    out["TC-06-06"] = {"enemies": mag, "pass": len(mag) == 6 and all(r["ok"] for r in mag)
                       and {r["enemy"]: r for r in mag}["mw"]["knockbackVel"] and abs({r["enemy"]: r for r in mag}["mw"]["knockbackVel"][0]) < 0.6}

    # ---- TC-06-07: slow fall while hit in the air, no stop / endless float, normal fall after the string stops (R-06-07)
    def juggle(name, eid):
        s = seg.get(name, [])
        tr = enemy_track(s, eid)
        air_hits = [h for h in of(s, "hit") if h["targetId"] == eid and h["reason"].startswith("AirHit")]
        if not tr: return {"slowOk": False}
        peak = max(tr, key=lambda r: r[3])
        last = max([h["simulationTick"] for h in air_hits] or [0])
        # vertical speed per tick (m/tick); slow window = up to 17 ticks (0.35 s) after each air hit
        vy = {tr[i][0]: tr[i][3] - tr[i - 1][3] for i in range(1, len(tr))}
        slow = [v for t, v in vy.items() if any(0 < t - h["simulationTick"] <= 17 for h in air_hits) and v < 0]
        after = [v for t, v in vy.items() if t > last + 17 and v < 0]
        landed = next((t for t, st, x, y, hp in tr if t > peak[0] and y <= 0.0 and st in ("Down", "Idle", "Stagger")), None)
        return {"airHits": len(air_hits), "peakY": round(peak[3], 3), "minFallPerTickDuringJuggle": round(min(slow), 4) if slow else None,
                "maxFallPerTickAfter": round(min(after), 4) if after else None, "landedTick": landed, "lastAirHit": last,
                "slowOk": len(air_hits) >= 2 and bool(slow) and min(slow) >= -0.041 and landed is not None,
                "normalFallAfterOk": bool(after) and min(after) < -0.041}
    s_air = seg.get("LaunchAirFour", [])
    air_st = [a for a in of(s_air, "attack.start") if a.get("attackId") in ("air1", "air2", "air3", "air4")]
    air_ids = {a["attackInstanceId"]: a.get("attackId") for a in air_st}
    air_h = [h for h in of(s_air, "hit") if h["targetId"] == "l1" and h["reason"].startswith("AirHit")]
    air_link = [a.get("attackId") for a in air_st] == ["air1", "air2", "air3", "air4"] and len(air_h) == 4 \
        and [air_ids.get(h.get("attackInstanceId")) for h in air_h] == ["air1", "air2", "air3", "air4"] and all(effective(h) for h in air_h)
    j4 = juggle("LaunchAirFour", "l1")
    j2 = juggle("LaunchAirTwoThenStop", "l4")
    out["TC-06-07"] = {"fourHits": j4, "twoThenStop": j2, "note": "slow fall = juggleFallSpeed 2 m/s = 0.04 m per tick",
                       "fourRegisteredAirAttacks": air_link,
                       "pass": bool(j4.get("slowOk")) and j4["airHits"] == 4 and bool(j2.get("slowOk")) and bool(j2.get("normalFallAfterOk")) and j2["airHits"] == 2 and air_link}

    # ---- TC-06-08: same launcher on launchable / resist / immune: different rise, immune still takes damage + count (R-06-08)
    rows = []
    for name, eid in (("LaunchAirFour", "l1"), ("LaunchResist", "l2"), ("LaunchImmune", "l3")):
        s = seg.get(name, [])
        h = next((x for x in of(s, "hit") if x["targetId"] == eid and x.get("attackId") == "launcher"), None)
        tr = enemy_track(s, eid)
        if h:
            tr = [r for r in tr if r[0] >= h["simulationTick"]][:60]
        rows.append({"enemy": eid, "samples": len(tr), "reaction": h["reason"] if h else None, "damage": (h["hpBefore"] - h["hpAfter"]) if h else None,
                     "comboAfter": h.get("comboAfter") if h else None, "peakY": round(max([r[3] for r in tr] or [0]), 3)})
    r = {x["enemy"]: x for x in rows}
    out["TC-06-08"] = {"targets": rows, "pass": all(x["reaction"] for x in rows) and all(x["samples"] >= 30 for x in rows) and r["l1"]["peakY"] > r["l2"]["peakY"] > 0 and r["l3"]["peakY"] == 0
                       and all(x["damage"] and x["damage"] > 0 and x["comboAfter"] == 1 for x in rows) and r["l3"]["reaction"].startswith("LaunchImmune")}

    # ---- TC-06-09: down attack from both sides is not a launcher; dive damages a downed enemy; one hit per motion (R-06-09)
    s = seg.get("DownAttackBothSides", [])
    da = [a for a in of(s, "attack.start") if a.get("attackId") == "downAttack"]
    hb = hits_by_attack(s)
    facing = []
    for a in da:
        t = next((x for x in s if x["type"] == "tick" and x["simulationTick"] == a["simulationTick"] + 1), {})
        facing.append(t.get("facing"))
    dah = [hb.get(a["attackInstanceId"], []) for a in da]
    presses = [any(x["type"] == "input" and x.get("action") == "SpecialAttack" and x.get("edge") == "Press" and x.get("inputEventId") == a.get("inputEventId")
                   and x["simulationTick"] <= a["simulationTick"] for x in s) for a in da]
    s2 = seg.get("DiveOnDowned", [])
    dv = next((a for a in of(s2, "attack.start") if a.get("attackId") == "dive"), None)
    dvh = hits_by_attack(s2).get(dv["attackInstanceId"], []) if dv else []
    out["TC-06-09"] = {"downAttacks": [{"facing": f, "hits": [(h["targetId"], h["stateBefore"], h["stateAfter"], h["reason"]) for h in hs]} for f, hs in zip(facing, dah)],
                       "dive": [(h["targetId"], h["stateBefore"], h["reason"], h["hpBefore"] - h["hpAfter"]) for h in dvh],
                       "pass": len(da) == 2 and sorted(facing) == [-1, 1] and all(len(hs) == 1 and hs[0]["stateBefore"] == "Down" and hs[0]["stateAfter"] != "Launched"
                                                                                  and hs[0]["hpAfter"] < hs[0]["hpBefore"] for hs in dah)
                       and len(dvh) == 1 and dvh[0]["stateBefore"] == "Down" and dvh[0]["hpAfter"] < dvh[0]["hpBefore"] and len(presses) == 2 and all(presses)}

    # ---- TC-06-10: down (non-down attack skipped), wakeup start / middle no damage (physical and magic), actionable at once (R-06-10)
    #      "start" = the first hit attempt reaches the target within 5 ticks of the wakeup start (ground1 startup is 3 ticks)
    s = seg.get("WakeupPhysical", [])
    skips = [(x["simulationTick"], x["stateAfter"], x["reason"]) for x in of(s, "hit.skip") if x["targetId"] == "w1"]
    tr = enemy_track(s, "w1")
    wake = [t for t, st, *_ in tr if st == "Wakeup"]
    idle_first = next((t for t, st, *_ in tr if wake and t > wake[-1] and st == "Idle"), None)
    hits = [h for h in of(s, "hit") if h["targetId"] == "w1" and h.get("attackId") != "launcher"]
    sm = seg.get("WakeupMagic", [])
    mskip = [(x["simulationTick"], x["reason"]) for x in of(sm, "hit.skip") if x["targetId"] == "w2"]
    mhits = [h for h in of(sm, "hit") if h["targetId"] == "w2" and h.get("damageType") == "Magic"]
    wake_skips = [k for k in skips if k[1] == "Wakeup"]
    out["TC-06-10"] = {"skips": skips, "wakeupTicks": (wake[0], wake[-1]) if wake else None, "firstIdleTick": idle_first,
                       "hitsAfterWakeup": [(h["simulationTick"], h["stateBefore"], h["reason"]) for h in hits], "magicDuringWakeup": mskip,
                       "magicHits": len(mhits),
                       "pass": any(k[1] == "Down" and "DownedNotHittable" in k[2] for k in skips) and len(wake_skips) >= 2
                       and wake and wake_skips[0][0] - wake[0] <= 5 and any(wake[0] < k[0] < wake[-1] - 3 for k in wake_skips)
                       and len(hits) == 1 and hits[0]["stateBefore"] == "Idle" and idle_first is not None and hits[0]["simulationTick"] - idle_first <= 5
                       and any("Wakeup" in r for _, r in mskip) and not mhits
                       and all(wake[0] <= k[0] <= wake[-1] and state_at(tr, k[0]) == "Wakeup" for k in wake_skips)
                       and hits[0]["simulationTick"] > wake[-1] and hits[0]["simulationTick"] >= idle_first
                       and not [x for x in of(s, "hit.skip") if x["targetId"] == "w1" and "Wakeup" in x["reason"] and x["simulationTick"] > wake[-1]]}
    out["TC-06-10"]["startBoundary"] = wake_boundary(seg.get("WakeupStartBoundary", []), "w3", "start")
    out["TC-06-10"]["endBoundary"] = wake_boundary(seg.get("WakeupEndBoundary", []), "w4", "end")
    out["TC-06-10"]["pass"] = out["TC-06-10"]["pass"] and out["TC-06-10"]["startBoundary"]["pass"] and out["TC-06-10"]["endBoundary"]["pass"]
    return out


def wake_boundary(s, eid, which):
    """R1: one ground attack is already active when the target changes state. start: the overlap continues from Down into the
    first Wakeup tick and that tick is refused as Wakeup. end: the overlap continues from the last Wakeup tick into the first Idle
    tick and the hit lands exactly on the first Idle tick (no extra invincibility)."""
    tr = enemy_track(s, eid)
    wake = [t for t, st, *_ in tr if st == "Wakeup"]
    row = {"wakeupTicks": (wake[0], wake[-1]) if wake else None}
    if not wake: return {**row, "pass": False}
    first_wake, last_wake = wake[0], wake[-1]
    first_idle = next((t for t, st, *_ in tr if t > last_wake and st == "Idle"), None)
    row["firstIdleTick"] = first_idle
    atk = [a for a in of(s, "attack.start") if a.get("attackId") == "ground1"]
    sk = of(s, "hit.skip")
    hits = [h for h in of(s, "hit") if h["targetId"] == eid and h.get("attackId") == "ground1"]
    ok = False
    for a in atk:
        inst = a["attackInstanceId"]
        e = next((x for x in of(s, "attack.end") if x.get("attackInstanceId") == inst), None)
        my_sk = [(x["simulationTick"], x["reason"]) for x in sk if x["targetId"] == eid and x.get("attackInstanceId") == inst]
        my_h = [h for h in hits if h.get("attackInstanceId") == inst]
        if which == "start":
            inv = [t for t, r in my_sk if "Wakeup" in r]
            cand = a["simulationTick"] < first_wake and inv == [first_wake] and not my_h and state_at(tr, first_wake - 1) == "Down" \
                and e is not None and e["simulationTick"] > first_wake
            if cand: row.update({"attack": inst, "start": a["simulationTick"], "skips": my_sk, "downSkipBefore": any("Downed" in r and t < first_wake for t, r in my_sk)})
        else:
            inv = [t for t, r in my_sk if "Wakeup" in r]
            cand = a["simulationTick"] < last_wake and len(inv) == 1 and inv[0] <= last_wake and len(my_h) == 1 and first_idle is not None \
                and my_h[0]["simulationTick"] == first_idle == last_wake + 1 and my_h[0]["stateBefore"] == "Idle" and my_h[0]["hpAfter"] < my_h[0]["hpBefore"]
            if cand: row.update({"attack": inst, "start": a["simulationTick"], "skips": my_sk, "hitTick": my_h[0]["simulationTick"]})
        ok = ok or cand
    no_extra = not [x for x in sk if x["targetId"] == eid and "Wakeup" in x["reason"] and (x["simulationTick"] < first_wake or x["simulationTick"] > last_wake)]
    row["pass"] = ok and no_extra
    return row


def magic_row(sm, eid):
    """One magic cast -> its effect -> one effective magic hit on eid with that instance -> real displacement (R-06-06)."""
    casts = of(sm, "magic.cast")
    effects = of(sm, "magic.effect")
    mh = [h for h in of(sm, "hit") if h["targetId"] == eid and h.get("damageType") == "Magic"]
    disp = moved_after(sm, eid, mh[0], 20) if mh else None
    ok = len(casts) == 1 and len(mh) == 1 and mh[0].get("attackInstanceId") == casts[0].get("attackInstanceId") \
        and any(x.get("attackInstanceId") == casts[0].get("attackInstanceId") and x["simulationTick"] <= mh[0]["simulationTick"] for x in effects) \
        and mh[0]["hpAfter"] < mh[0]["hpBefore"] and abs(mh[0].get("knockbackVel") or 0) > 0 and (disp or 0) > 0
    return {"enemy": eid, "magicHits": len(mh), "knockbackVel": [h.get("knockbackVel") for h in mh], "reaction": [h["reason"] for h in mh],
            "displacement": disp, "ok": ok}


def hitstop_section(ev):
    """Motion length (attack.end - attack.start ticks) and hitstop records of the first ground1 in each hitstop scenario."""
    seg = segments(ev)
    res = {}
    for name in ("SameMotionOverlap", "HitstopOneTarget", "HitstopTenTargets"):
        s = seg.get(name)
        if not s: continue
        a = next((x for x in of(s, "attack.start") if x.get("attackId") == "ground1"), None)
        if not a: continue
        e = next((x for x in of(s, "attack.end") if x.get("attackInstanceId") == a["attackInstanceId"]), None)
        stops = [x for x in of(s, "hitstop") if x.get("attackInstanceId") == a["attackInstanceId"]]
        targets = len({h["targetId"] for h in of(s, "hit") if h.get("attackInstanceId") == a["attackInstanceId"]})
        res["one" if name != "HitstopTenTargets" else "ten"] = {"targets": targets, "motionTicks": e["simulationTick"] - a["simulationTick"] if e else None,
                                                               "stops": [(x.get("seconds"), x.get("targets")) for x in stops]}
    ok = "one" in res and "ten" in res and res["one"]["targets"] == 1 and res["ten"]["targets"] == 10 and len(res["ten"]["stops"]) == 1 and len(res["one"]["stops"]) == 1 \
        and res["one"]["motionTicks"] == res["ten"]["motionTicks"] and res["one"]["stops"][0][0] == res["ten"]["stops"][0][0]
    res["pass"] = ok
    return res


def hs_check(main, hs):
    a, b = hitstop_section(main), hitstop_section(hs)
    ok = a["pass"] and b["pass"] and b["one"]["stops"][0][0] > a["one"]["stops"][0][0] and b["one"]["motionTicks"] > a["one"]["motionTicks"]
    return {"TC-06-03": {"mainRun": a, "inspectorRun": b,
                         "extraTicks": (b["one"]["motionTicks"] - a["one"]["motionTicks"]) if ok else None, "pass": ok}}


def selftest(ev):
    base = check(ev)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]

    def mutate(name, key, fn):
        m = copy.deepcopy(ev)
        fn(m)
        r = check(m)[key]["pass"]
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})

    def dup(scn, tgt):
        def f(m):
            s = segments(m)[scn]
            h = next(e for e in s if e["type"] == "hit" and e["targetId"] == tgt)
            h2 = copy.deepcopy(h)
            h2["eventId"] += 0.5
            m.insert(m.index(h) + 1, h2)
        return f
    mutate("TC-06-01 same motion hits the target twice", "TC-06-01", dup("SameMotionOverlap", "o1"))

    def multi_dup(m):
        s = segments(m)["ThreeTargetsAndThreeEvents"]
        hs = [e for e in s if e["type"] == "hit" and e.get("attackId") == "ground4"]
        hs[1]["hitEventId"] = hs[0]["hitEventId"]
        hs[1]["targetId"] = hs[0]["targetId"]
    mutate("TC-06-02 one event hits a target twice", "TC-06-02", multi_dup)

    def stop_twice(m):
        s = segments(m)["HitstopTenTargets"]
        h = next(e for e in s if e["type"] == "hitstop")
        h2 = copy.deepcopy(h)
        h2["eventId"] += 0.5
        m.insert(m.index(h) + 1, h2)
    mutate("TC-06-03 ten targets stop twice", "TC-06-03-main", stop_twice)

    def boss_staggers(m):
        for e in segments(m)["Reactions_k4"]:
            if e["type"] == "hit" and e["targetId"] == "k4": e["stateAfter"] = "Stagger"
    mutate("TC-06-04 stagger-resistant boss staggers", "TC-06-04", boss_staggers)

    def same_kb(m):
        for e in segments(m)["Reactions_k3"]:
            if e["type"] == "hit": e["knockbackVel"] = 1.5
    mutate("TC-06-05 large enemy knocked back like a small one", "TC-06-05", same_kb)

    def magic_zero(m):
        for e in segments(m)["Magic_k4"]:
            if e["type"] == "hit": e["knockbackVel"] = 0
    mutate("TC-06-06 magic knockback 0 on the boss", "TC-06-06", magic_zero)

    def float_forever(m):
        s = segments(m)["LaunchAirTwoThenStop"]
        ticks = [e for e in s if e["type"] == "tick"]
        y = None
        for t in ticks:
            t["enemies"] = re.sub(r"(l4:)([A-Za-z]+)@(-?[\d.]+),(-?[\d.]+)", lambda mm: f"{mm.group(1)}Launched@{mm.group(3)},2.00", t.get("enemies", ""))
    mutate("TC-06-07 enemy floats after the string stops", "TC-06-07", float_forever)

    def resist_as_able(m):
        s = segments(m)["LaunchResist"]
        for t in s:
            if t["type"] == "tick":
                t["enemies"] = re.sub(r"(l2:[A-Za-z]+@-?[\d.]+,)(-?[\d.]+)", lambda mm: mm.group(1) + "%.2f" % (float(mm.group(2)) * 5), t.get("enemies", ""))
    mutate("TC-06-08 resist rises like launchable", "TC-06-08", resist_as_able)

    def down_launch(m):
        s = segments(m)["DownAttackBothSides"]
        h = [e for e in s if e["type"] == "hit" and e.get("attackId") == "downAttack"][0]
        h["stateAfter"] = "Launched"
    mutate("TC-06-09 down attack launches", "TC-06-09", down_launch)

    def wake_hit(m):
        s = segments(m)["WakeupPhysical"]
        sk = [e for e in s if e["type"] == "hit.skip" and e.get("stateAfter") == "Wakeup"][0]
        sk["type"] = "hit"
        sk["stateBefore"] = "Wakeup"
        sk["attackId"] = "ground1"
        sk["hpBefore"], sk["hpAfter"] = 990, 980
    mutate("TC-06-10 hit lands during wakeup", "TC-06-10", wake_hit)

    # GPT WP-07 review M02: the 12 counterexamples it found passing
    def m01(m):
        h = next(e for e in segments(m)["SameMotionOverlap"] if e["type"] == "hit")
        h["hpAfter"] = h["hpBefore"]
        h["damageTaken"] = 0
    mutate("M02 TC-06-01 first hit with no damage", "TC-06-01", m01)

    def m02(m):
        hs = [e for e in segments(m)["ThreeTargetsAndThreeEvents"] if e["type"] == "hit" and e.get("attackId") == "ground4"]
        for h in hs:
            inst, k = h["hitEventId"].split(":")
            h["hitEventId"] = "%s:%d" % (inst, 90 + int(k))
    mutate("M02 TC-06-02 undeclared event ids 90/91/92", "TC-06-02", m02)

    def m03(m):
        s = segments(m)["SameMotionOverlap"]
        a = next(e for e in s if e["type"] == "attack.start" and e.get("attackId") == "ground1")
        h = next(e for e in s if e["type"] == "hitstop" and e.get("attackInstanceId") == a["attackInstanceId"])
        h2 = copy.deepcopy(h)
        h2["eventId"] += 0.5
        m.insert(m.index(h) + 1, h2)
    mutate("M02 TC-06-03 single-target stop duplicated", "TC-06-03-main", m03)

    def m04(m):
        h = next(e for e in segments(m)["Reactions_k1"] if e["type"] == "hit" and e["targetId"] == "k1")
        h["stateAfter"] = "Idle"
    mutate("M02 TC-06-04 small enemy first reaction recorded as Idle", "TC-06-04", m04)

    def m05(m):
        s = segments(m)["Reactions_k1"]
        x0 = None
        for t in s:
            if t["type"] != "tick": continue
            mm = re.search(r"k1:[A-Za-z]+@(-?[\d.]+),", t.get("enemies", ""))
            if mm and x0 is None: x0 = mm.group(1)
            if x0 is not None:
                t["enemies"] = re.sub(r"(k1:[A-Za-z]+@)(-?[\d.]+)(,)", lambda q: q.group(1) + x0 + q.group(3), t["enemies"])
    mutate("M02 TC-06-05 small enemy x pinned to its start", "TC-06-05", m05)

    def m06(m):
        h = next(e for e in segments(m)["Magic_k4"] if e["type"] == "hit" and e.get("damageType") == "Magic")
        h["attackInstanceId"] = 99999
    mutate("M02 TC-06-06 boss magic hit linked to an unrelated instance", "TC-06-06", m06)

    def m07(m):
        for e in segments(m)["LaunchAirFour"]:
            if e["type"] == "hit" and e["targetId"] == "l1" and e["reason"].startswith("AirHit"):
                e["attackInstanceId"] = 88888
                e["attackId"] = "air1"
    mutate("M02 TC-06-07 four air hits on one unregistered instance", "TC-06-07", m07)

    def m08(m):
        for t in segments(m)["LaunchImmune"]:
            if t["type"] == "tick":
                t["enemies"] = "|".join(x for x in str(t.get("enemies", "")).split("|") if not x.startswith("l3:"))
    mutate("M02 TC-06-08 immune target samples removed", "TC-06-08", m08)

    def m09(m):
        drop = {id(e) for e in segments(m)["DownAttackBothSides"] if e["type"] == "input" and e.get("action") == "SpecialAttack" and e.get("edge") == "Press"}
        m[:] = [e for e in m if id(e) not in drop]
    mutate("M02 TC-06-09 special presses removed", "TC-06-09", m09)

    def wake_info(m):
        s = segments(m)["WakeupPhysical"]
        tr = enemy_track(s, "w1")
        wake = [t for t, st, *_ in tr if st == "Wakeup"]
        return s, wake

    def m10a(m):
        s, wake = wake_info(m)
        sk = next(e for e in s if e["type"] == "hit.skip" and e["targetId"] == "w1" and "Wakeup" in e["reason"])
        sk["simulationTick"] = wake[0] - 1
    mutate("M02 TC-06-10 wakeup refusal moved before wakeup starts", "TC-06-10", m10a)

    def m10b(m):
        s, wake = wake_info(m)
        h = next(e for e in s if e["type"] == "hit" and e["targetId"] == "w1" and e.get("attackId") != "launcher")
        h["simulationTick"] = wake[-1]
    mutate("M02 TC-06-10 Idle hit moved to the last wakeup tick", "TC-06-10", m10b)

    def m10c(m):
        s, wake = wake_info(m)
        sk = next(e for e in s if e["type"] == "hit.skip" and e["targetId"] == "w1" and "Wakeup" in e["reason"])
        x = copy.deepcopy(sk)
        x["simulationTick"] = wake[-1] + 1
        x["eventId"] += 0.5
        m.insert(m.index(sk) + 1, x)
    mutate("M02 TC-06-10 extra wakeup refusal on the first Idle tick", "TC-06-10", m10c)

    # R1 cases
    def r1_kb_record_zero(m):
        for e in segments(m)["PhysEqual_pe"]:
            if e["type"] == "enemy.knockback": e["displacement"] = 0.0
    mutate("R1 TC-06-05 equal-to-one-tick knockback record shows no movement", "TC-06-05", r1_kb_record_zero)

    def r1_weak_still(m):
        for e in segments(m)["MagicWeak_mw"]:
            if e["type"] == "enemy.knockback": e["displacement"] = 0.0
    mutate("R1 TC-06-06 weak magic knockback does not move", "TC-06-06", r1_weak_still)

    def r1_start_late(m):
        s = segments(m)["WakeupStartBoundary"]
        for e in s:
            if e["type"] == "hit.skip" and "Wakeup" in e["reason"]: e["simulationTick"] += 1
    mutate("R1 TC-06-10 start-boundary refusal one tick after wakeup starts", "TC-06-10", r1_start_late)

    def r1_end_late(m):
        s = segments(m)["WakeupEndBoundary"]
        for e in s:
            if e["type"] == "hit" and e["targetId"] == "w4": e["simulationTick"] += 1
    mutate("R1 TC-06-10 end-boundary hit one tick after the first Idle tick", "TC-06-10", r1_end_late)
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--hs" in sys.argv:
        m, h = load(args[0]), load(args[1])
        out = {"logs": args, **hs_check(m, h)}
        if "--selftest" in sys.argv:
            hh = copy.deepcopy(h)
            for e in hh:
                if e["type"] == "hitstop": e["seconds"] = 0.04
            for e in hh:
                if e["type"] == "attack.end" and e.get("attackId") == "ground1": e["simulationTick"] -= 3
            r = hs_check(m, hh)["TC-06-03"]["pass"]
            out["selftest"] = [{"case": "Inspector change has no effect", "sectionPass": r, "pass": r is False}]
            out["selftest_all_pass"] = r is False
    else:
        ev = load(args[0])
        out = {"log": args[0], **check(ev)}
        if "--selftest" in sys.argv:
            out["selftest"] = selftest(ev)
            out["selftest_all_pass"] = all(r["pass"] is not False for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
