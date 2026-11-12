"""WP-09 log checks: P0-01..03 residual evidence.

Usage:
  python Tools/wp09_checks.py <main run>.ndjson [--selftest]          TC-02-01, TC-03-01, TC-03-06..09
  python Tools/wp09_checks.py --plane <plane run>.ndjson [--selftest]  TC-01-01 (player, enemies and boss stay on the combat plane)
"""
import copy
import json
import sys

DT = 0.02
SCENARIOS = ["MapJump", "MapAttack", "MapSpecial", "MapGuard", "MapMove", "MapRing", "MapQuickslot", "StrongTiltWalk", "PadDoubleTapRun",
             "KeyDoubleTapRun", "AirDirectionOnly", "AirDoubleTap", "AirA", "AirDirAttack", "AirDirSpecial", "AirRB", "DashThenJump", "JumpThenDash",
             "AirAttackKeepsCounts", "DiveThenAAndDash", "LandThenAttack", "LandThenGuard", "LandThenJumpAgain", "ScriptEnd"]
KB, PAD = "Corum2VirtualKeyboard", "Corum2VirtualGamepad"


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


def presses(s, action):
    return [e for e in of(s, "input.route") if e.get("action") == action and e.get("edge") == "Press"]


def check(ev):
    seg = segments(ev)
    out = {}

    # ---- TC-02-01: the same logical action from the keyboard and from the pad, with the same game result (virtual devices)
    rows = []
    def pair(name, action, result):
        s = seg.get(name, [])
        p = presses(s, action)
        devs = sorted({e.get("device") for e in p})
        res = result(s)
        rows.append({"segment": name, "action": action, "devices": devs, "results": res,
                     "pass": devs == sorted([KB, PAD]) and len(res) >= 2 and len(set(r[1] for r in res)) == 1})
    def follow(t, key):
        return lambda s: [(e["simulationTick"], e.get(key) if key else e["type"]) for e in of(s, t)]
    pair("MapJump", "Jump", follow("motor.jump", "reason"))
    pair("MapAttack", "Attack", lambda s: [(e["simulationTick"], e["reason"]) for e in of(s, "attack.start")])
    pair("MapSpecial", "SpecialAttack", lambda s: [(e["simulationTick"], e["reason"]) for e in of(s, "attack.start")])
    pair("MapGuard", "Guard", lambda s: [(e["simulationTick"], e["stateAfter"]) for e in of(s, "motor.state") if e.get("stateAfter") == "Guard"])
    pair("MapMove", "MoveRight", lambda s: [(e["simulationTick"], e.get("action")) for e in presses(s, "MoveRight")])
    pair("MapRing", "RingToggle", lambda s: [(e["simulationTick"], e["type"]) for e in of(s, "ring.open")])
    pair("MapRing", "PagePrev", lambda s: [(e["simulationTick"], e["reason"]) for e in of(s, "ring.page", "Prev")])
    pair("MapRing", "PageNext", lambda s: [(e["simulationTick"], e["reason"]) for e in of(s, "ring.page", "Next")])
    pair("MapRing", "ButtonB", lambda s: [(e["simulationTick"], e["reason"]) for e in of(s, "ring.ignore") if e.get("targetId") == "ButtonB" or "B" in str(e.get("actorId", ""))] or
         [(e["simulationTick"], "B") for e in presses(s, "ButtonB")])
    pair("MapQuickslot", "QuickDown", lambda s: [(e["simulationTick"], e["reason"].split(":")[0]) for e in of(s, "item.use")])
    out["TC-02-01"] = {"pairs": rows, "note": "virtual keyboard / gamepad; real devices and Steam Deck are BLOCKED_ENV", "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-01: strong tilt only = walk (no run); double tap and hold = run; release ends it (R-03-01)
    def states(s):
        return [e["stateAfter"] for e in s if e["type"] == "tick"]
    st = seg.get("StrongTiltWalk", [])
    strong = [e for e in presses(st, "MoveRight") if e.get("device") == PAD]
    rows = [{"case": "StrongTiltWalk", "runEvents": [e["reason"] for e in of(st, "motor.run")], "walkTicks": states(st).count("Walk"), "runTicks": states(st).count("Run"),
             "pass": bool(strong) and not of(st, "motor.run") and states(st).count("Walk") > 10 and states(st).count("Run") == 0}]
    for name, dev in (("PadDoubleTapRun", PAD), ("KeyDoubleTapRun", KB)):
        s = seg.get(name, [])
        armed = of(s, "motor.run", "DoubleTap")
        ended = of(s, "motor.run", "RunDirectionReleased")
        rows.append({"case": name, "armed": [e["simulationTick"] for e in armed], "released": [e["simulationTick"] for e in ended], "runTicks": states(s).count("Run"),
                     "pass": len(armed) == 1 and len(ended) == 1 and armed[0]["simulationTick"] < ended[0]["simulationTick"] and states(s).count("Run") > 10
                     and all(e.get("device") == dev for e in presses(s, "MoveRight"))})
    out["TC-03-01"] = {"cases": rows, "note": "strong tilt = stick (1,0) on the virtual pad", "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-06: air inputs are told apart: move / double tap (no dash) / A = extra jump / dir+attack = dash / dir+special = dive / RB = guard
    def first_air(name):
        s = seg.get(name, [])
        return s, [e for e in s if e["type"] in ("air.use", "motor.run", "motor.state") and (e["type"] != "motor.state" or e.get("stateAfter") == "Guard")]
    rows = []
    s, a = first_air("AirDirectionOnly")
    air_ticks = [e for e in s if e["type"] == "tick" and e.get("grounded") is False]
    rows.append({"case": "AirDirectionOnly", "airUse": [e["reason"] for e in of(s, "air.use")], "airVx": max([abs(e.get("vx", 0)) for e in air_ticks] or [0]),
                 "pass": not of(s, "air.use") and any(abs(e.get("vx", 0)) > 0 for e in air_ticks)})
    for name, want in (("AirDoubleTap", ("motor.run", "AirDoubleTapIgnored")), ("AirA", ("air.use", "ExtraJump")), ("AirDirAttack", ("air.use", "AirDash")),
                       ("AirDirSpecial", ("air.use", "Dive"))):
        s, a = first_air(name)
        got = [(e["type"], e["reason"]) for e in a if e["type"] in ("air.use", "motor.run")]
        rows.append({"case": name, "events": got, "pass": len(got) == 1 and got[0][0] == want[0] and got[0][1].startswith(want[1])})
    s, a = first_air("AirRB")
    g = [e for e in a if e["type"] == "motor.state"]
    gt = next((e for e in s if e["type"] == "tick" and g and e["simulationTick"] == g[0]["simulationTick"]), {})
    rows.append({"case": "AirRB", "guardTick": g[0]["simulationTick"] if g else None, "grounded": gt.get("grounded"), "airUse": [e["reason"] for e in of(s, "air.use")],
                 "pass": bool(g) and gt.get("grounded") is False and not of(s, "air.use")})
    out["TC-03-06"] = {"cases": rows, "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-07: each of extra jump / dash once per airtime, in either order; an air normal attack does not use them
    rows = []
    for name, order in (("DashThenJump", ["AirDash", "ExtraJump"]), ("JumpThenDash", ["ExtraJump", "AirDash"]), ("AirAttackKeepsCounts", ["ExtraJump", "AirDash"])):
        s = seg.get(name, [])
        uses = [e["reason"] for e in of(s, "air.use")]
        rej = [e["reason"] for e in of(s, "input.reject", "AlreadyUsed")]
        air_atk = [e for e in of(s, "attack.start") if e.get("attackId", "").startswith("air") and e.get("attackId") != "airDash"]
        rows.append({"case": name, "airUse": uses, "rejected": rej, "airNormalAttacks": len(air_atk),
                     "pass": uses == order and (name != "AirAttackKeepsCounts" or (len(air_atk) == 1 and air_atk[0]["eventId"] < of(s, "air.use")[0]["eventId"]))
                     and (name == "AirAttackKeepsCounts" or len(rej) >= 1)})
    out["TC-03-07"] = {"cases": rows, "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-08: after the dive, A and dash do nothing (reason logged) (R-03-09)
    s = seg.get("DiveThenAAndDash", [])
    rej = [(e.get("action"), e["reason"]) for e in of(s, "input.reject")]
    out["TC-03-08"] = {"airUse": [e["reason"] for e in of(s, "air.use")], "rejected": rej,
                       "pass": [e["reason"] for e in of(s, "air.use")] == ["Dive"] and ("Jump", "ActionLock:Dive") in rej and ("Attack", "ActionLock:Dive") in rej}

    # ---- TC-03-09: a normal landing resets the counts and allows attack / guard / jump at once (R-03-10)
    rows = []
    for name, kind in (("LandThenAttack", "attack"), ("LandThenGuard", "guard"), ("LandThenJumpAgain", "jump")):
        s = seg.get(name, [])
        land = next(iter(of(s, "motor.land", "Landed")), None)
        if kind == "attack": act = next((e for e in of(s, "attack.start") if land and e["eventId"] > land["eventId"]), None)
        elif kind == "guard": act = next((e for e in of(s, "motor.state") if land and e.get("stateAfter") == "Guard" and e["eventId"] > land["eventId"]), None)
        else: act = next((e for e in of(s, "motor.jump") if land and e["eventId"] > land["eventId"]), None)
        rej = [e for e in of(s, "input.reject") if land and act and land["eventId"] < e["eventId"] < act["eventId"]]
        extra_after = [e for e in of(s, "air.use", "ExtraJump") if act and e["eventId"] > act["eventId"]]
        rows.append({"case": name, "landTick": land["simulationTick"] if land else None, "actionTick": act["simulationTick"] if act else None,
                     "ticksAfterLanding": (act["simulationTick"] - land["simulationTick"]) if land and act else None, "resetAfter": land.get("airAfter") if land else None,
                     "pass": land is not None and act is not None and act["simulationTick"] - land["simulationTick"] <= 2 and not rej
                     and land.get("airAfter") == "jump=free,dash=free,dive=free" and (kind != "jump" or len(extra_after) == 1)})
    out["TC-03-09"] = {"cases": rows, "note": "CF-02 (landing after special moves) not included", "pass": all(r["pass"] for r in rows)}
    return out


def plane_check(ev):
    pc = {e["reason"]: e for e in ev if e["type"] == "view.planeCheck"}
    p, en = pc.get("RenderedPlayerDepth", {}), pc.get("RenderedEnemyDepth", {})
    ticks = [e for e in ev if e["type"] == "tick"]
    jumps = [e for e in ev if e["type"] == "motor.jump"]
    ai_jumps = [e for e in ev if e["type"] == "ai.jump"]
    return {"TC-01-01": {"player": {k: p.get(k) for k in ("frames", "maxAbsZError", "planeZ")}, "playerJumps": len(jumps),
                         "enemies": {k: en.get(k) for k in ("samples", "bossSamples", "movingOrAirborneSamples", "maxAbsZError", "planeZ")}, "aiJumps": len(ai_jumps),
                         "allTicksPlaneZ": all(t.get("planeZ") == p.get("planeZ") for t in ticks),
                         "pass": p.get("frames", 0) > 0 and p.get("maxAbsZError") == 0 and en.get("samples", 0) > 0 and en.get("bossSamples", 0) > 0
                         and en.get("movingOrAirborneSamples", 0) > 0 and en.get("maxAbsZError") == 0 and len(jumps) > 0 and len(ai_jumps) > 0
                         and all(t.get("planeZ") == p.get("planeZ") for t in ticks)}}


def selftest(ev):
    base = check(ev)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]

    def mutate(name, key, fn):
        m = copy.deepcopy(ev)
        try:
            fn(m)
        except StopIteration:
            res.append({"case": name, "section": key, "sectionPass": None, "pass": None, "note": "record to mutate not present"})
            return
        r = check(m)[key]["pass"]
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})

    def first(m, scn, typ, pred=lambda e: True):
        return next(e for e in segments(m)[scn] if e["type"] == typ and pred(e))

    mutate("TC-02-01 pad press maps to another action", "TC-02-01",
           lambda m: first(m, "MapSpecial", "input.route", lambda e: e.get("device") == PAD and e.get("edge") == "Press").__setitem__("action", "Attack"))
    mutate("TC-03-01 strong tilt runs", "TC-03-01", lambda m: first(m, "StrongTiltWalk", "tick", lambda e: e.get("stateAfter") == "Walk").__setitem__("stateAfter", "Run"))
    mutate("TC-03-06 air double tap dashes", "TC-03-06", lambda m: first(m, "AirDoubleTap", "motor.run").update({"type": "air.use", "reason": "AirDash"}))
    mutate("TC-03-07 second dash allowed", "TC-03-07", lambda m: first(m, "DashThenJump", "input.reject", lambda e: "AirDash" in e["reason"]).update({"type": "air.use", "reason": "AirDash"}))
    mutate("TC-03-08 jump after the dive", "TC-03-08", lambda m: first(m, "DiveThenAAndDash", "input.reject", lambda e: e.get("action") == "Jump").update({"type": "air.use", "reason": "ExtraJump"}))

    def late(m):
        s = segments(m)["LandThenGuard"]
        land = next(e for e in s if e["type"] == "motor.land")
        g = next(e for e in s if e["type"] == "motor.state" and e.get("stateAfter") == "Guard" and e["eventId"] > land["eventId"])
        g["simulationTick"] += 6
    mutate("TC-03-09 guard only well after landing", "TC-03-09", late)
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    ev = load(args[0])
    if "--plane" in sys.argv:
        out = {"log": args[0], **plane_check(ev)}
        if "--selftest" in sys.argv:
            m = copy.deepcopy(ev)
            for e in m:
                if e["type"] == "view.planeCheck" and e["reason"] == "RenderedEnemyDepth": e["maxAbsZError"] = 0.3
            r = plane_check(m)["TC-01-01"]["pass"]
            out["selftest"] = [{"case": "an enemy rendered off the plane", "sectionPass": r, "pass": r is False}]
            out["selftest_all_pass"] = r is False
    else:
        out = {"log": args[0], **check(ev)}
        if "--selftest" in sys.argv:
            out["selftest"] = selftest(ev)
            out["selftest_all_pass"] = all(r["pass"] is not False for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
