"""WP-09 log checks: P0-01..03 residual evidence (R1: WP09-M01 depth classes, WP09-M02 linked checks, device pairs, left slide).

Usage:
  python Tools/wp09_checks.py <main run>.ndjson [--selftest]          TC-02-01 part 1, TC-03-01, TC-03-06..09
  python Tools/wp09_checks.py --plane <plane run>.ndjson [--selftest]  TC-01-01 (player per input direction, enemies by motion class, boss)
  python Tools/wp09_checks.py --dev <dev run>.ndjson [--selftest]      TC-02-01 part 2 (quickslots U/R/D/L, real page turns, B discard; per device)
  python Tools/wp09_checks.py --slide <slide run>.ndjson [--selftest]  TC-03-04 left-facing slide into the low passage (keyboard and pad)

R3 (GPT WP-09-R2 review): the run's <run>.data.json beside the log supplies stickDeadzone and slideSpeed.
R4 (GPT WP-09-R3 review): the ring release uses RingConfig.cursorDeadzone with the Core's strict boundary (|v| < dz is neutral);
the sidecar is used only when its runId equals the log's and, with a manifest beside it, its SHA-256 equals data_snapshot_sha256.
"""
import copy
import hashlib
import json
import struct
import math
import os
import re
import sys

DT = 0.02
SCENARIOS = ["MapJump", "MapAttack", "MapSpecial", "MapGuard", "MapMove", "MapRing", "MapQuickslot", "StrongTiltWalk", "PadDoubleTapRun",
             "KeyDoubleTapRun", "AirDirectionOnly", "AirDoubleTap", "AirA", "AirDirAttack", "AirDirSpecial", "AirRB", "DashThenJump", "JumpThenDash",
             "AirAttackKeepsCounts", "DiveThenAAndDash", "LandThenAttack", "LandThenGuard", "LandThenJumpAgain", "ScriptEnd"]
SLOTS = ["Up", "Right", "Down", "Left"]
DEV_SCENARIOS = [f"Dev{d}_{s}" for d in ("Kb", "Pad") for s in ["Register" + q for q in SLOTS] + ["UseQuickslots", "Pages", "Discard"]] + ["ScriptEnd"]
SLIDE_SCENARIOS = ["SlideLeftKb", "SlideLeftPad", "ScriptEnd"]
KB, PAD = "Corum2VirtualKeyboard", "Corum2VirtualGamepad"
FREE = "jump=free,dash=free,dive=free"
DIRS = ["None", "Up", "UpRight", "Right", "DownRight", "Down", "DownLeft", "Left", "UpLeft"]
CEILING = (40.0, 42.0)  # M01 low passage solid x range (Data/WP01/Map_M01_FieldA.asset, min y 1.2)
CFG = {}  # R3: the run's own data snapshot (<run>.data.json): input.stickDeadzone, movement.slideSpeed


def cfg(path, default=None):
    o = CFG
    for k in path.split("."):
        if not isinstance(o, dict) or k not in o:
            return default
        o = o[k]
    return o


def ring_deadzone():
    """R4: the ring's own cursor deadzone (RingController.Cursor -> Dir8Util.FromStick(x, y, RingConfig.cursorDeadzone))."""
    for x in CFG.get("extra") or []:
        if isinstance(x, dict) and x.get("asset") == "RingConfig" and isinstance(x.get("data"), dict):
            v = x["data"].get("cursorDeadzone")
            return v if isinstance(v, (int, float)) and not isinstance(v, bool) else None
    return None


def stick_neutral(stick, dz):
    """Dir8Util.FromStick: None when |v| < deadzone or |v| <= 0 (the exact boundary is not neutral)."""
    if dz is None or not (isinstance(stick, list) and len(stick) == 2 and all(isinstance(c, (int, float)) for c in stick)):
        return False
    # R7 (GPT R5-R6 review): Core takes float x, y, deadzone; x * x + y * y is float arithmetic, then Math.Sqrt in double
    # and the double compare with the float deadzone. Logged numbers are the shortest float round-trip, so f32() restores them.
    x, y, d = f32(stick[0]), f32(stick[1]), f32(dz)
    mag = math.sqrt(f32(f32(x * x) + f32(y * y)))
    return mag < d or mag <= 0.0


def f32(v):
    return struct.unpack("<f", struct.pack("<f", float(v)))[0]


def run_dt(ev):
    """R4: the run's own fixed step (most common game-time difference between consecutive tick records), not a 50 Hz constant."""
    ts = sorted((e["simulationTick"], e.get("gameTime")) for e in ev if e.get("type") == "tick" and isinstance(e.get("gameTime"), (int, float)))
    ds = [round(b[1] - a[1], 6) for a, b in zip(ts, ts[1:]) if b[0] == a[0] + 1]
    return max(set(ds), key=ds.count) if ds else None


def load_cfg(log_path, ev):
    """R4: load <run>.data.json only if it belongs to this run (GPT WP-09-R3: a same-named file with another runId passed)."""
    CFG.clear()
    dpath = re.sub(r"\.ndjson$", ".data.json", log_path)
    mpath = re.sub(r"\.ndjson$", ".manifest.yaml", log_path)
    run_id = next((e.get("runId") for e in ev if e.get("type") == "run.start"), None)
    try:
        raw = open(dpath, "rb").read()
    except OSError:
        return {"sidecar": dpath, "status": "missing"}
    d = json.loads(raw.decode("utf-8"))
    st = {"sidecar": dpath, "logRunId": run_id, "dataRunId": d.get("runId")}
    if not run_id or d.get("runId") != run_id or any(e.get("runId") != run_id for e in ev):
        return {**st, "status": "runId mismatch"}
    if os.path.exists(mpath):
        man = open(mpath, encoding="utf-8").read()
        want = re.search(r'^data_snapshot_sha256:\s*"?([0-9a-f]{64})"?', man, re.M)
        mid = re.search(r'^run_id:\s*"?([^"\s]+)"?', man, re.M)
        got = hashlib.sha256(raw).hexdigest()
        st.update(manifest=mpath, manifestSha=want and want.group(1), dataSha=got)
        if not want or want.group(1) != got or not mid or mid.group(1) != run_id:
            return {**st, "status": "manifest mismatch"}
    else:
        st["manifest"] = None
    CFG.update(d)
    return {**st, "status": "ok"}


def inv_count(s, item):
    """Count of `item` in an inventory string like 'potionx2,herbx1' (absent = 0)."""
    for part in (s or "").split(","):
        m = re.fullmatch(r"(.+)x(\d+)", part.strip())
        if m and m.group(1) == item:
            return int(m.group(2))
    return 0


def load(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(l) for l in f if l.strip()]


def segments(ev, names=SCENARIOS):
    marks = [(e["eventId"], e["reason"]) for e in ev if e["type"] == "marker" and e["reason"] in names]
    seg = {}
    for i, (eid, name) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else float("inf")
        seg[name] = [e for e in ev if eid <= e["eventId"] < end]
    return seg


def of(s, t, reason=None):
    return [e for e in s if e["type"] == t and (reason is None or str(e.get("reason", "")).startswith(reason))]


def presses(s, action, dev=None):
    return [e for e in of(s, "input.route") if e.get("action") == action and e.get("edge") == "Press" and (dev is None or e.get("device") == dev)]


def within(e, p, lo=0, hi=1):
    return p["simulationTick"] + lo <= e["simulationTick"] <= p["simulationTick"] + hi


def link_rows(s, action, outcome):
    """Per device: each press of `action` must have its own outcome (outcome(s, press) -> (event, value) or None),
    no outcome may serve two presses, and both devices must give the same sequence of values."""
    used, per = set(), {}
    for dev in (KB, PAD):
        rows = []
        for p in presses(s, action, dev):
            o = outcome(s, p, used)
            if o and o[0]["simulationTick"] < p["simulationTick"]:
                o = None  # R2: the result must come on or after its press tick (GPT WP-09-R1 M02); ring results are logged just before their route record
            if o:
                used.add(o[0]["eventId"])
            rows.append({"press": p["inputEventId"], "tick": p["simulationTick"], "outcomeTick": o[0]["simulationTick"] if o else None, "value": o[1] if o else None})
        per[dev] = rows
    ok = all(rows and all(r["value"] is not None for r in rows) for rows in per.values()) and [r["value"] for r in per[KB]] == [r["value"] for r in per[PAD]]
    return {"action": action, "keyboard": per[KB], "pad": per[PAD], "pass": ok}


def by_input_id(typ, value):
    def f(s, p, used):
        e = next((e for e in of(s, typ) if e.get("inputEventId") == p["inputEventId"] and e["eventId"] not in used), None)
        return (e, value(e)) if e else None
    return f


def by_window(typ, pred, value, hi=1):
    def f(s, p, used):
        e = next((e for e in s if e["type"] == typ and e["eventId"] not in used and within(e, p, 0, hi) and pred(e)), None)
        return (e, value(e)) if e else None
    return f


def quick_outcome(s, p, used):
    d = p["action"][len("Quick"):]
    req = next((e for e in of(s, "item.request") if e["eventId"] not in used and within(e, p, 0, 1) and e.get("via") == "quickslot." + d), None)
    if not req:
        return None
    uses = [e for e in of(s, "item.use") if e.get("requestId") == req["requestId"]]
    reqs = [e for e in of(s, "item.request") if e.get("requestId") == req["requestId"]]
    if len(reqs) != 1 or (req["reason"].startswith("Accepted") and len(uses) != 1):
        return None
    if uses and uses[0].get("targetId") != req["targetId"]:
        return None  # R2: request target = executed target (GPT WP-09-R1 M02)
    if uses:
        u = uses[0]
        # R3: press -> request -> execution on the timeline (GPT WP-09-R2 #3): the use's own tick is its execTick, its requestTick is the
        # request's tick, it executes after the request, and an Accepted:NextTick request that waited for nothing executes exactly one tick later.
        if not (u.get("execTick") == u["simulationTick"] and u.get("requestTick") == req["simulationTick"] and u["simulationTick"] > req["simulationTick"]
                and u["eventId"] > req["eventId"] and abs(u.get("execGameTime", -1) - u.get("gameTime", -2)) < 1e-6):
            return None
        if req["reason"].startswith("Accepted:NextTick") and not u.get("waitedFor") and u["simulationTick"] != req["simulationTick"] + 1:
            return None
        # R4: a non-empty waitedFor must be the last item.wait state the core logged for this request between request and execution
        # (GPT WP-09-R3: an invented wait reason let a NextTick request run two ticks late)
        if u.get("waitedFor"):
            waits = [w for w in s if w["type"] == "item.wait" and w.get("requestId") == req["requestId"] and req["eventId"] < w["eventId"] < u["eventId"]
                     and req["simulationTick"] <= w["simulationTick"] < u["simulationTick"]]
            if not waits or waits[-1].get("stateAfter") != u["waitedFor"]:
                return None
    return req, (d, req["reason"].split(":")[0], req["targetId"], uses[0]["reason"].split(":")[0] if uses else None)


def check(ev):
    seg = segments(ev)
    out = {}

    # ---- TC-02-01 part 1: the same logical action from the keyboard and from the pad; each press linked to its own result (WP09-M02)
    rows = []
    def add(name, action, outcome):
        r = link_rows(seg.get(name, []), action, outcome)
        r["segment"] = name
        rows.append(r)
    add("MapJump", "Jump", by_input_id("motor.jump", lambda e: e["reason"]))
    add("MapAttack", "Attack", by_input_id("attack.start", lambda e: e["attackId"]))
    add("MapSpecial", "SpecialAttack", by_input_id("attack.start", lambda e: e["attackId"]))
    add("MapGuard", "Guard", by_window("motor.state", lambda e: e.get("stateAfter") == "Guard", lambda e: "Guard"))
    add("MapMove", "MoveRight", by_window("tick", lambda e: e.get("heldDir") == "Right" and e.get("vx", 0) > 0, lambda e: ("Right", e["vx"]), hi=2))
    add("MapRing", "RingToggle", lambda s, p, used: by_window("ring.open", lambda e: True, lambda e: "Open")(s, p, used)
        or by_window("ring.close", lambda e: e["reason"].startswith("Manual"), lambda e: "Close")(s, p, used))
    add("MapRing", "PagePrev", by_window("ring.page", lambda e: e["reason"].startswith("Prev"), lambda e: (e["reason"], e["pageCount"])))
    add("MapRing", "PageNext", by_window("ring.page", lambda e: e["reason"].startswith("Next"), lambda e: (e["reason"], e["pageCount"])))
    add("MapRing", "ButtonB", by_window("ring.ignore", lambda e: e.get("action") == "ButtonB", lambda e: e["reason"]))
    add("MapQuickslot", "QuickDown", quick_outcome)
    out["TC-02-01"] = {"pairs": rows, "note": "virtual keyboard / gamepad; real devices and Steam Deck are BLOCKED_ENV", "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-01: strong tilt only = walk (no run); double tap and hold = run; release ends it and no Run tick follows (R-03-01)
    def ticks(s):
        return [e for e in s if e["type"] == "tick"]
    st = seg.get("StrongTiltWalk", [])
    strong = presses(st, "MoveRight", PAD)
    states = [e["stateAfter"] for e in ticks(st)]
    rows = [{"case": "StrongTiltWalk", "runEvents": [e["reason"] for e in of(st, "motor.run")], "walkTicks": states.count("Walk"), "runTicks": states.count("Run"),
             "pass": bool(strong) and not of(st, "motor.run") and states.count("Walk") > 10 and states.count("Run") == 0}]
    for name, dev in (("PadDoubleTapRun", PAD), ("KeyDoubleTapRun", KB)):
        s = seg.get(name, [])
        armed, ended = of(s, "motor.run", "DoubleTap"), of(s, "motor.run", "RunDirectionReleased")
        ts = ticks(s)
        rel = ended[0]["simulationTick"] if ended else None
        run_after = [e["simulationTick"] for e in ts if rel is not None and e["simulationTick"] > rel and (e["stateAfter"] == "Run" or abs(e.get("vx", 0)) > 3.5)]
        run_ticks = [e for e in ts if e["stateAfter"] == "Run"]
        rows.append({"case": name, "armed": [e["simulationTick"] for e in armed], "released": [e["simulationTick"] for e in ended], "runTicks": len(run_ticks),
                     "runOrFastAfterRelease": run_after[:5],
                     "pass": len(armed) == 1 and len(ended) == 1 and armed[0]["simulationTick"] < rel and len(run_ticks) > 10 and not run_after
                     and all(armed[0]["simulationTick"] <= e["simulationTick"] <= rel for e in run_ticks)
                     and all(e.get("device") == dev for e in presses(s, "MoveRight"))})
    out["TC-03-01"] = {"cases": rows, "note": "strong tilt = stick (1,0) on the virtual pad", "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-06: air inputs are told apart; each ability is linked to its own press, made in the air, with the right direction held
    def tick_at(s, t):
        return next((e for e in s if e["type"] == "tick" and e["simulationTick"] == t), {})
    def press_input(s, eid):
        return next((e for e in of(s, "input") if e.get("inputEventId") == eid and e.get("edge") == "Press"), None)
    rows = []
    s = seg.get("AirDirectionOnly", [])
    air_ticks = [e for e in s if e["type"] == "tick" and e.get("grounded") is False]
    rows.append({"case": "AirDirectionOnly", "airUse": [e["reason"] for e in of(s, "air.use")],
                 "airVxWithRight": max([abs(e.get("vx", 0)) for e in air_ticks if e.get("heldDir") == "Right"] or [0]),
                 "pass": not of(s, "air.use") and any(e.get("heldDir") == "Right" and e.get("vx", 0) > 0 for e in air_ticks)})
    s = seg.get("AirDoubleTap", [])
    dt = of(s, "motor.run")
    rows.append({"case": "AirDoubleTap", "events": [e["reason"] for e in dt], "airUse": [e["reason"] for e in of(s, "air.use")],
                 "pass": len(dt) == 1 and dt[0]["reason"].startswith("AirDoubleTapIgnored") and not of(s, "air.use")
                 and tick_at(s, dt[0]["simulationTick"]).get("grounded") is False})
    for name, ability, action, dirs in (("AirA", "ExtraJump", "Jump", None), ("AirDirAttack", "AirDash", "Attack", ("Right",)),
                                       ("AirDirSpecial", "Dive", "SpecialAttack", ("Right", "DownRight", "Down", "DownLeft", "Left"))):
        s = seg.get(name, [])
        uses = of(s, "air.use")
        u = uses[0] if len(uses) == 1 else None
        inp = press_input(s, u.get("inputEventId")) if u else None
        before = tick_at(s, u["simulationTick"] - 1) if u else {}
        rows.append({"case": name, "airUse": [e["reason"] for e in uses], "inputAction": inp and inp.get("action"), "heldDir": inp and inp.get("heldDir"),
                     "airborneBefore": before.get("grounded") is False,
                     "pass": u is not None and u["reason"].startswith(ability) and inp is not None and inp.get("action") == action
                     and (dirs is None or inp.get("heldDir") in dirs) and before.get("grounded") is False})
    s = seg.get("AirRB", [])
    g = [e for e in of(s, "motor.state") if e.get("stateAfter") == "Guard"]
    gt = tick_at(s, g[0]["simulationTick"]) if g else {}
    rows.append({"case": "AirRB", "guardTick": g[0]["simulationTick"] if g else None, "grounded": gt.get("grounded"), "airUse": [e["reason"] for e in of(s, "air.use")],
                 "pass": bool(g) and gt.get("grounded") is False and not of(s, "air.use") and bool(presses(s, "Guard"))})
    out["TC-03-06"] = {"cases": rows, "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-07: each of extra jump / dash once per airtime, in either order, all inside the same airtime; an air normal attack uses neither
    rows = []
    for name, order in (("DashThenJump", ["AirDash", "ExtraJump"]), ("JumpThenDash", ["ExtraJump", "AirDash"]), ("AirAttackKeepsCounts", ["ExtraJump", "AirDash"])):
        s = seg.get(name, [])
        j0 = next(iter(of(s, "motor.jump")), None)
        land = next((e for e in of(s, "motor.land") if j0 and e["eventId"] > j0["eventId"]), None)
        uses = of(s, "air.use")
        rej = of(s, "input.reject", "AlreadyUsed")
        inside = lambda e: j0 is not None and land is not None and j0["simulationTick"] < e["simulationTick"] < land["simulationTick"]
        chain = all(uses[i]["stateAfter"] == uses[i + 1]["stateBefore"] for i in range(len(uses) - 1))
        air_atk = [e for e in of(s, "attack.start") if e.get("attackId", "").startswith("air") and e.get("attackId") != "airDash"]
        rows.append({"case": name, "jumpTick": j0 and j0["simulationTick"], "landTick": land and land["simulationTick"], "airUse": [(e["reason"], e["simulationTick"]) for e in uses],
                     "rejected": [(e["reason"], e["simulationTick"]) for e in rej], "airNormalAttacks": len(air_atk),
                     "pass": [e["reason"] for e in uses] == order and all(inside(e) for e in uses) and chain
                     and (name == "AirAttackKeepsCounts" or (len(rej) >= 1 and all(inside(e) for e in rej)))
                     and (name != "AirAttackKeepsCounts" or (len(air_atk) == 1 and inside(air_atk[0]) and air_atk[0]["eventId"] < uses[0]["eventId"]))})
    out["TC-03-07"] = {"cases": rows, "pass": all(r["pass"] for r in rows)}

    # ---- TC-03-08: after the dive starts and before it lands, A and attack are refused (ActionLock:Dive), each linked to its own press (R-03-09)
    s = seg.get("DiveThenAAndDash", [])
    dive = next(iter(of(s, "air.use", "Dive")), None)
    land = next((e for e in of(s, "motor.land") if dive and e["eventId"] > dive["eventId"]), None)
    rej = of(s, "input.reject", "ActionLock:Dive")
    def linked(r):
        i = press_input(s, r.get("inputEventId"))
        return i is not None and i.get("action") == r.get("action")
    ok_rej = {r.get("action") for r in rej if dive and land and dive["simulationTick"] <= r["simulationTick"] < land["simulationTick"] and linked(r)}
    out["TC-03-08"] = {"airUse": [e["reason"] for e in of(s, "air.use")], "diveTick": dive and dive["simulationTick"], "landTick": land and land["simulationTick"],
                       "rejected": [(e.get("action"), e["reason"], e["simulationTick"]) for e in rej],
                       "pass": [e["reason"] for e in of(s, "air.use")] == ["Dive"] and {"Jump", "Attack"} <= ok_rej}

    # ---- TC-03-09: a normal landing resets the counts that were really used, and attack / guard / jump start on or right after the landing tick
    rows = []
    for name, kind in (("LandThenAttack", "attack"), ("LandThenGuard", "guard"), ("LandThenJumpAgain", "jump")):
        s = seg.get(name, [])
        land = next(iter(of(s, "motor.land", "Landed")), None)
        if kind == "attack": act = next((e for e in of(s, "attack.start") if land and e["eventId"] > land["eventId"]), None)
        elif kind == "guard": act = next((e for e in of(s, "motor.state") if land and e.get("stateAfter") == "Guard" and e["eventId"] > land["eventId"]), None)
        else: act = next((e for e in of(s, "motor.jump") if land and e["eventId"] > land["eventId"]), None)
        used_before = [e for e in of(s, "air.use") if land and e["eventId"] < land["eventId"]]
        expect_before = used_before[-1]["stateAfter"] if used_before else FREE
        rej = [e for e in of(s, "input.reject") if land and act and land["eventId"] < e["eventId"] < act["eventId"]]
        extra_after = [e for e in of(s, "air.use", "ExtraJump") if act and e["eventId"] > act["eventId"]]
        gap = (act["simulationTick"] - land["simulationTick"]) if land and act else None
        rows.append({"case": name, "landTick": land and land["simulationTick"], "actionTick": act and act["simulationTick"], "ticksAfterLanding": gap,
                     "usedBefore": [e["reason"] for e in used_before], "airBefore": land and land.get("airBefore"), "airAfter": land and land.get("airAfter"),
                     "pass": land is not None and act is not None and 0 <= gap <= 2 and not rej and land.get("airBefore") == expect_before
                     and land.get("airAfter") == FREE and (kind == "guard" or (bool(used_before) and "used" in expect_before))
                     and (kind != "jump" or len(extra_after) == 1)})
    out["TC-03-09"] = {"cases": rows, "note": "CF-02 (landing after special moves) not included", "pass": all(r["pass"] for r in rows)}
    return out


def parse_enemies(s):
    return {m[0]: (m[1], float(m[2]), float(m[3])) for m in re.findall(r"(?:^|\|)([^:|]+):(\w+)@([-\d.]+),([-\d.]+)", s or "")}


def table(s):
    return {k: float(v) for k, v in (x.split(":") for x in (s or "").split(",") if ":" in x)}


def plane_check(ev):
    pc = {e["reason"].split("(")[0]: e for e in ev if e["type"] == "view.planeCheck"}
    p, en = pc.get("RenderedPlayerDepth", {}), pc.get("RenderedEnemyDepth", {})
    ticks = sorted((e for e in ev if e["type"] == "tick"), key=lambda e: e["simulationTick"])
    tn = [e["simulationTick"] for e in ticks]
    continuous = len(tn) > 100 and tn == list(range(tn[0], tn[0] + len(tn))) and p.get("simulationTick", -1) >= tn[-1]
    plane = {e.get("planeZ") for e in ticks}
    # simulated ground moves of enemies from the per-tick snapshot (y = 0, x changed since the previous tick)
    prev, ground_moves, air_ticks = {}, {}, {}
    for e in ticks:
        cur = parse_enemies(e.get("enemies"))
        for k, (st, x, y) in cur.items():
            if k in prev and y == 0 and prev[k][2] == 0 and x != prev[k][1]:
                ground_moves[k] = ground_moves.get(k, 0) + 1
            if y > 0:
                air_ticks[k] = air_ticks.get(k, 0) + 1
        prev = cur
    classes = ("firstSamples", "airborneSamples", "teleportSamples", "groundMovingSamples", "groundStillSamples")
    cls_sum = sum(en.get(k, 0) for k in classes)
    dirs = table(p.get("dirFrames"))
    dirz = table(p.get("dirMaxAbsZError"))
    zeros = [en.get(k) for k in ("maxAbsZError", "maxAbsZFirst", "maxAbsZAirborne", "maxAbsZTeleport", "maxAbsZGroundMoving", "maxAbsZGroundStill")]
    jumps = [e for e in ev if e["type"] == "motor.jump"]
    ai_jumps = [e for e in ev if e["type"] == "ai.jump"]
    r = {"player": {k: p.get(k) for k in ("frames", "maxAbsZError", "planeZ", "airFrames", "airMaxAbsZError")}, "playerDirFrames": dirs,
         "playerJumps": len(jumps), "enemies": {k: en.get(k) for k in ("samples", "bossSamples", "bossMovingSamples", *classes, "propelledSamples", "maxAbsZError")},
         "classSum": cls_sum, "aiJumps": len(ai_jumps), "simTicks": len(tn), "ticksContinuous": continuous, "tickPlaneZ": sorted(plane, key=str),
         "simGroundMoveTicksByEnemy": ground_moves, "simAirTicksByEnemy": air_ticks}
    r["pass"] = (continuous and plane == {p.get("planeZ")} and en.get("planeZ") == p.get("planeZ")
                 and p.get("frames", 0) > 0 and p.get("maxAbsZError") == 0 and sum(dirs.values()) == p.get("frames") and set(dirs) == set(DIRS) == set(dirz)
                 and all(v > 0 for v in dirs.values()) and all(v == 0 for v in dirz.values())
                 and p.get("airFrames", 0) > 0 and p.get("airMaxAbsZError") == 0
                 and en.get("samples", 0) > 0 and cls_sum == en.get("samples") and 0 < en.get("bossSamples", 0) <= en.get("samples", 0)
                 and 0 < en.get("bossMovingSamples", 0) <= en.get("bossSamples", 0)
                 and p.get("airFrames", 0) <= p.get("frames", 0)
                 # R3: CombatView.Sync counts a boss sample as moving only when it is airborne or ground-moving (teleport is not counted)
                 and en.get("bossMovingSamples", 0) <= en.get("groundMovingSamples", 0) + en.get("airborneSamples", 0)
                 # R3: PropelledSamples is counted before the first-sample classification, so the bound is all samples (GPT WP-09-R2)
                 and 0 < en.get("propelledSamples", 0) <= en.get("samples", 0)
                 and en.get("groundMovingSamples", 0) > 0 and en.get("airborneSamples", 0) > 0 and all(z == 0 for z in zeros)
                 and len(jumps) > 0 and len(ai_jumps) > 0 and sum(ground_moves.values()) > 0 and sum(air_ticks.values()) > 0)
    return {"TC-01-01": r}


def inv_snapshot(v):
    """R4: an inventory snapshot must be a string of item x count parts ('' = empty); None or another type is not a snapshot."""
    return isinstance(v, str) and all(re.fullmatch(r".+x\d+", p.strip()) for p in v.split(",") if p.strip())


def discard_snapshots_ok(ev, d):
    """R4 (GPT WP-09-R3): the discard's own snapshot is a real snapshot with countAfter, and the next later snapshot shows countAfter,
    unless that record itself changes the item, in which case it must start from countAfter and show its own countAfter."""
    item = d.get("item")
    if not inv_snapshot(d.get("inventory")) or inv_count(d["inventory"], item) != d.get("countAfter"):
        return False
    nxt = next((x for x in ev if x["eventId"] > d["eventId"] and "inventory" in x), None)
    if nxt is None:
        return True
    if not inv_snapshot(nxt.get("inventory")):
        return False
    if nxt.get("targetId") == item and "countBefore" in nxt and "countAfter" in nxt:
        return nxt["countBefore"] == d["countAfter"] and inv_count(nxt["inventory"], item) == nxt["countAfter"]
    return inv_count(nxt["inventory"], item) == d["countAfter"]


def dev_check(ev):
    seg = segments(ev, DEV_SCENARIOS)
    out = {}
    per = {}
    for d, dev in (("Kb", KB), ("Pad", PAD)):
        rows = []
        registered = {}
        for slot in SLOTS:
            s = seg.get(f"Dev{d}_Register{slot}", [])
            op = [e for e in of(s, "ring.open") if any(within(e, p) for p in presses(s, "RingToggle", dev))]
            fix = [e for e in of(s, "ring.mode", "TargetFixed") if any(within(e, p) for p in presses(s, "SpecialAttack", dev))]
            dp = presses(s, "Quick" + slot, dev)
            asg = [e for e in of(s, "quickslot.assign") if e.get("slot") == slot and any(within(e, p, 0, 0) and p.get("consumer") == "ring" for p in dp)]
            dz = ring_deadzone()
            # R4 (GPT WP-09-R3): the release is judged by the ring's own cursorDeadzone with the Core's strict boundary, once per
            # registering session for the fixed target. As in RingController (ReleasedSinceRegister), a later re-tilt does not undo it.
            same_session = fix and asg and not [x for x in s if x["type"] in ("ring.open", "ring.close") and fix[0]["eventId"] < x["eventId"] < asg[0]["eventId"]]
            rel = [e for e in of(s, "ring.release") if same_session and fix[0]["eventId"] < e["eventId"] < asg[0]["eventId"]
                   and e.get("mode") == "Registering" and (e.get("stateBefore"), e.get("stateAfter")) == ("Held", "Released")
                   and e.get("target") == fix[0]["target"] and fix[0]["simulationTick"] <= e["simulationTick"] <= asg[0]["simulationTick"]
                   and stick_neutral(e.get("stick"), dz)]
            rel = rel if len([e for e in of(s, "ring.release") if fix and asg and fix[0]["eventId"] < e["eventId"] < asg[0]["eventId"]]) == 1 else []
            ok = len(op) == 1 and len(fix) == 1 and len(asg) == 1 and asg[0]["stateAfter"] == fix[0]["target"] and asg[0].get("stickReleasedSinceY") is True \
                and len(rel) >= 1 \
                and not [p for p in presses(s, "Quick" + slot) if p.get("device") != dev]
            if asg:
                registered[slot] = asg[0]["stateAfter"]
            rows.append({"step": "Register" + slot, "item": asg[0]["stateAfter"] if asg else None, "pass": ok})
        s = seg.get(f"Dev{d}_UseQuickslots", [])
        req_ids = []
        for slot in SLOTS:
            ps = [p for p in presses(s, "Quick" + slot, dev) if p.get("consumer") == "quickslot"]
            o = quick_outcome(s, ps[0], set()) if len(ps) == 1 else None
            if o:
                req_ids.append(o[0]["requestId"])
            rows.append({"step": "Use" + slot, "outcome": o and o[1], "registered": registered.get(slot),
                         "pass": o is not None and o[1][1] == "Accepted" and o[1][2] == registered.get(slot) and o[1][3] is not None})
        s = seg.get(f"Dev{d}_Pages", [])
        nx = [e for e in of(s, "ring.page", "Next") if any(within(e, p) for p in presses(s, "PageNext", dev))]
        pv = [e for e in of(s, "ring.page", "Prev") if any(within(e, p) for p in presses(s, "PagePrev", dev))]
        rows.append({"step": "Pages", "next": [(e["stateBefore"], e["stateAfter"], e["pageCount"]) for e in nx], "prev": [(e["stateBefore"], e["stateAfter"]) for e in pv],
                     "pass": len(nx) == 1 and len(pv) == 1 and nx[0]["pageCount"] == 2 and (nx[0]["stateBefore"], nx[0]["stateAfter"]) == ("0", "1")
                     and (pv[0]["stateBefore"], pv[0]["stateAfter"]) == ("1", "0") and nx[0]["eventId"] < pv[0]["eventId"]})
        s = seg.get(f"Dev{d}_Discard", [])
        ask = [e for e in of(s, "ring.mode", "DiscardAsked") if any(within(e, p) for p in presses(s, "ButtonB", dev))]
        conf = [e for e in of(s, "ring.discard", "Confirmed") if any(within(e, p) and p.get("consumer") == "ring" for p in presses(s, "Jump", dev))]
        cl = [e for e in of(s, "ring.close", "Discarded") if conf and e["simulationTick"] == conf[0]["simulationTick"]]
        rows.append({"step": "Discard", "asked": [e["selectedItem"] for e in ask], "discarded": [(e["item"], e["countBefore"], e["countAfter"]) for e in conf],
                     "pass": len(ask) == 1 and ask[0]["selectedItem"] != "" and len(conf) == 1 and conf[0]["item"] == ask[0]["target"] == ask[0]["selectedItem"]
                     and conf[0]["countAfter"] == conf[0]["countBefore"] - 1 and len(cl) == 1
                     # R3: the discard's own inventory snapshot and the next later snapshot in this run show countAfter (GPT WP-09-R2 #4)
                     and discard_snapshots_ok(ev, conf[0])})
        all_req = [e["requestId"] for e in of(s, "item.request")]
        per[d] = {"steps": rows, "requestIdsUnique": len(req_ids) == len(set(req_ids)) == 4, "pass": all(r["pass"] for r in rows) and len(req_ids) == len(set(req_ids)) == 4}
    same = [r["step"] for r in per["Kb"]["steps"]] == [r["step"] for r in per["Pad"]["steps"]]
    ids = [e["requestId"] for e in ev if e["type"] == "item.request"]
    out["TC-02-01"] = {"part": "device pairs (WP09 R1)", "keyboard": per["Kb"], "pad": per["Pad"], "requestIdsUniqueInRun": len(ids) == len(set(ids)),
                       "note": "virtual devices; real keyboard / pad / Steam Deck are BLOCKED_ENV",
                       "pass": per["Kb"]["pass"] and per["Pad"]["pass"] and same and len(ids) == len(set(ids))}
    return out


def slide_check(ev):
    seg = segments(ev, SLIDE_SCENARIOS)
    rows = []
    for name, dev in (("SlideLeftKb", KB), ("SlideLeftPad", PAD)):
        s = seg.get(name, [])
        tk = {e["simulationTick"]: e for e in s if e["type"] == "tick"}
        starts = [e for e in of(s, "motor.state", "Slide(") if e.get("stateAfter") == "Slide"]
        ends = [e for e in of(s, "motor.state", "SlideEnd")]
        slides = []
        ids = [st.get("inputEventId") for st in starts]
        speed = cfg("movement.slideSpeed")
        dt0 = run_dt(ev)  # R4: the run's own fixed step
        gt = {n: e.get("gameTime") for n, e in tk.items()}
        for st in starts:
            # R2: the slide's own inputEventId must be a Jump press received on or before the start (GPT WP-09-R1 M02)
            # R3: ... delivered and consumed by the game on the start tick, and claimed by no other start (GPT WP-09-R2 #5)
            inp = next((e for e in of(s, "input") if e.get("inputEventId") == st.get("inputEventId") and e.get("edge") == "Press" and e.get("action") == "Jump"
                        and e["simulationTick"] == st["simulationTick"] and e.get("deliveredTick") == st["simulationTick"]  # R4
                        and e.get("consumer") == "game" and e.get("reason") == "slide"), None)
            if ids.count(st.get("inputEventId")) != 1:
                inp = None
            en = next((e for e in ends if e["eventId"] > st["eventId"]), None)
            before = tk.get(st["simulationTick"] - 1, {})
            traj = [tk.get(n, {}).get("x") for n in range(st["simulationTick"] - 1, en["simulationTick"] + 1)] if en else []
            fc = tk.get(st["simulationTick"], {}).get("facing") or 0
            traj_ok = bool(traj) and None not in traj and all((b - a) * fc >= 0 for a, b in zip(traj, traj[1:])) \
                and abs(traj[-1] - en.get("position", [1e9])[0]) < 1e-4 and tk.get(en["simulationTick"], {}).get("stateAfter") == en.get("stateAfter")
            # R3: no tick moves farther than slideSpeed x that tick's time step (+1 mm for rounding); collisions may shorten it (GPT WP-09-R2 #6)
            steps = []
            if traj_ok and speed:
                for n in range(st["simulationTick"], en["simulationTick"] + 1):
                    dt = (gt.get(n) or 0) - (gt.get(n - 1) or 0)
                    steps.append((n, abs(tk[n]["x"] - tk[n - 1]["x"]), dt))
            max_step = max((d for _, d, _ in steps), default=None)
            traj_ok = traj_ok and bool(steps) and dt0 is not None and all(0 < dt <= dt0 + 1e-6 and d <= speed * dt + 1e-3 for _, d, dt in steps)
            slides.append({"tick": st["simulationTick"], "facing": tk.get(st["simulationTick"], {}).get("facing"), "fromX": before.get("x"),
                           "device": inp and inp.get("device"), "heldDir": inp and inp.get("heldDir"),
                           "end": en and en["reason"], "endX": en and en.get("position", [None])[0], "endTick": en and en["simulationTick"],
                           "trajectoryTicks": len(traj), "maxStepM": max_step, "stepLimitM": speed and dt0 and speed * dt0, "runDt": dt0, "trajectoryOk": traj_ok})
        left = [x for x in slides if x["facing"] == -1]
        enter = next((x for x in left if x["end"] == "SlideEnd:CeilingKeepsCrouch" and CEILING[0] - 0.5 <= x["endX"] <= CEILING[1] + 0.5 and x["fromX"] > CEILING[1]), None)
        exit_ = next((x for x in left if enter and x["tick"] > enter["tick"] and x["fromX"] == enter["endX"] and x["endX"] < CEILING[0] - 0.5 and x["end"] == "SlideEnd"), None)
        rows.append({"case": name, "slides": slides, "leftEnter": enter, "leftExit": exit_,
                     "pass": enter is not None and exit_ is not None and all(x["device"] == dev and x["heldDir"] == "Down" and x["trajectoryOk"] for x in left)
                     and all(tk.get(t, {}).get("facing") == -1 for t in range(enter["tick"], exit_["endTick"] + 1))})
    return {"TC-03-04": {"cases": rows, "note": "left-facing down + A into the low passage (M01 ceiling x 40..42); the right-facing pass is from WP-01-R1",
                         "pass": all(r["pass"] for r in rows)}}


# ------------------------------------------------------------------ self-tests

def run_mutations(ev, fn, cases, segnames):
    base = fn(ev)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items()}}]
    for name, key, seg, edit in cases:
        m = copy.deepcopy(ev)
        rows = segments(m, segnames)[seg] if seg else m
        try:
            edit(m, rows)
        except (StopIteration, IndexError) as x:
            res.append({"case": name, "section": key, "sectionPass": None, "pass": None, "note": f"record to mutate not present ({x!r})"})
            continue
        r = fn(m)[key]["pass"]
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})
    return res


def remove(es, rows, pred):
    es[:] = [e for e in es if not (e in rows and pred(e))]


def selftest(ev):
    def first(s, typ, pred=lambda e: True):
        return next(e for e in s if e["type"] == typ and pred(e))

    def late(m, s):
        land = first(s, "motor.land")
        first(s, "motor.state", lambda e: e.get("stateAfter") == "Guard" and e["eventId"] > land["eventId"])["simulationTick"] += 6

    def early_end(m, s):  # GPT: run continues after release
        t = of(s, "motor.run")[1]["simulationTick"]
        for e in of(s, "tick"):
            if e["simulationTick"] >= t: e.update(stateAfter="Run", vx=7)

    def wrong_direction(m, s):  # GPT: no direction on the dive input
        first(s, "input", lambda e: e.get("action") == "SpecialAttack" and e.get("edge") == "Press")["heldDir"] = "None"

    def fake_reset(m, s):  # GPT: second ability after landing
        of(s, "air.use")[1]["simulationTick"] = of(s, "motor.land")[0]["simulationTick"] + 1

    def late_as_early(m, s):  # GPT: action before landing
        land = of(s, "motor.land")[0]
        next(x for x in of(s, "attack.start") if x["eventId"] > land["eventId"])["simulationTick"] = land["simulationTick"] - 1

    cases = [
        ("TC-02-01 pad press maps to another action", "TC-02-01", "MapSpecial",
         lambda m, s: first(s, "input.route", lambda e: e.get("device") == PAD and e.get("edge") == "Press").__setitem__("action", "Attack")),
        ("TC-03-01 strong tilt runs", "TC-03-01", "StrongTiltWalk", lambda m, s: first(s, "tick", lambda e: e.get("stateAfter") == "Walk").__setitem__("stateAfter", "Run")),
        ("TC-03-06 air double tap dashes", "TC-03-06", "AirDoubleTap", lambda m, s: first(s, "motor.run").update({"type": "air.use", "reason": "AirDash"})),
        ("TC-03-07 second dash allowed", "TC-03-07", "DashThenJump", lambda m, s: first(s, "input.reject", lambda e: "AirDash" in e["reason"]).update({"type": "air.use", "reason": "AirDash"})),
        ("TC-03-08 jump after the dive", "TC-03-08", "DiveThenAAndDash", lambda m, s: first(s, "input.reject", lambda e: e.get("action") == "Jump").update({"type": "air.use", "reason": "ExtraJump"})),
        ("TC-03-09 guard only well after landing", "TC-03-09", "LandThenGuard", late),
        # GPT WP-09 review M02 (main log)
        ("GPT TC-02-01 second jump reuses the first input id", "TC-02-01", "MapJump", lambda m, s: of(s, "motor.jump")[1].__setitem__("inputEventId", of(s, "motor.jump")[0]["inputEventId"])),
        ("GPT TC-02-01 second scroll reuses the first request id", "TC-02-01", "MapQuickslot", lambda m, s: of(s, "item.use")[1].__setitem__("requestId", of(s, "item.use")[0]["requestId"])),
        ("GPT TC-02-01 ring B disposition removed", "TC-02-01", "MapRing", lambda m, s: remove(m, s, lambda e: e["type"] == "ring.ignore" and e.get("action") == "ButtonB")),
        ("GPT TC-03-01 run continues after release", "TC-03-01", "PadDoubleTapRun", early_end),
        ("GPT TC-03-06 no direction on the dive input", "TC-03-06", "AirDirSpecial", wrong_direction),
        ("GPT TC-03-07 second ability after landing", "TC-03-07", "DashThenJump", fake_reset),
        ("GPT TC-03-08 rejected attack after landing", "TC-03-08", "DiveThenAAndDash",
         lambda m, s: first(s, "input.reject", lambda e: e.get("action") == "Attack").update(simulationTick=of(s, "motor.land")[0]["simulationTick"] + 1)),
        ("GPT TC-03-09 action before landing", "TC-03-09", "LandThenAttack", late_as_early),
        ("GPT TC-03-09 used counter missing before reset", "TC-03-09", "LandThenJumpAgain", lambda m, s: of(s, "motor.land")[0].__setitem__("airBefore", FREE)),
        # R1 additions
        ("R1 TC-02-01 pad guard result removed", "TC-02-01", "MapGuard",
         lambda m, s: remove(m, s, lambda e: e["type"] == "motor.state" and e.get("stateAfter") == "Guard" and e["simulationTick"] == presses(s, "Guard", PAD)[0]["simulationTick"] + 1)),
        ("R1 TC-03-06 dash made on the ground", "TC-03-06", "AirDirAttack",
         lambda m, s: [e.__setitem__("grounded", True) for e in of(s, "tick") if e["simulationTick"] == of(s, "air.use")[0]["simulationTick"] - 1]),
        # GPT WP-09-R1 review M02
        ("GPT-R1 jump result before its own press", "TC-02-01", "MapJump", lambda m, s: first(s, "motor.jump").update(simulationTick=1)),
    ]
    return run_mutations(ev, check, cases, SCENARIOS)


def plane_selftest(ev):
    def pc(m, reason):
        return next(e for e in m if e["type"] == "view.planeCheck" and e["reason"].startswith(reason))
    cases = [
        ("an enemy rendered off the plane", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(maxAbsZError=0.3)),
        ("GPT no simulation ticks", "TC-01-01", None, lambda m, s: m.__setitem__(slice(None), [x for x in m if x["type"] != "tick"])),
        ("GPT enemy plane differs from the player's", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(planeZ=8)),
        ("GPT boss samples exceed all samples", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(bossSamples=99999)),
        ("R1 no ground-moving samples (VelX-only count)", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(groundMovingSamples=0)),
        ("R1 classes do not add up", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(groundStillSamples=pc(m, "RenderedEnemyDepth")["groundStillSamples"] + 7)),
        ("R1 ground-moving sample off the plane", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(maxAbsZGroundMoving=0.2)),
        ("R1 a direction never rendered", "TC-01-01", None, lambda m, s: pc(m, "RenderedPlayerDepth").update(dirFrames=pc(m, "RenderedPlayerDepth")["dirFrames"].replace("DownLeft:", "DownLeftX:"))),
        ("R1 player off the plane while holding up-left", "TC-01-01", None, lambda m, s: pc(m, "RenderedPlayerDepth").update(dirMaxAbsZError=pc(m, "RenderedPlayerDepth")["dirMaxAbsZError"].replace("UpLeft:0", "UpLeft:0.4"))),
        ("R1 a tick missing in the middle", "TC-01-01", None, lambda m, s: m.remove(next(x for x in m if x["type"] == "tick" and x["simulationTick"] == 500))),
        ("R1 enemies never move on the ground in the simulation", "TC-01-01", None,
         lambda m, s: [x.__setitem__("enemies", re.sub(r"@[-\d.]+,0\.00(?= )", "@1.00,0.00", x.get("enemies", ""))) for x in m if x["type"] == "tick"]),
        # GPT WP-09-R1 review M02
        ("GPT-R1 air frames exceed all frames", "TC-01-01", None, lambda m, s: pc(m, "RenderedPlayerDepth").update(airFrames=pc(m, "RenderedPlayerDepth")["frames"] + 1)),
        ("GPT-R1 boss moving = all boss samples", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(bossMovingSamples=pc(m, "RenderedEnemyDepth")["bossSamples"])),
        ("GPT-R1 zero propelled despite walking AI", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(propelledSamples=0)),
        # GPT WP-09-R2 review #1: one still sample moved to teleport (class sum kept), boss moving = ground-moving + airborne + 1
        ("GPT-R2 boss moving counts a teleport sample", "TC-01-01", None, lambda m, s: (lambda e: e.update(
            groundStillSamples=e["groundStillSamples"] - 1, teleportSamples=e["teleportSamples"] + 1,
            bossMovingSamples=e["groundMovingSamples"] + e["airborneSamples"] + 1))(pc(m, "RenderedEnemyDepth"))),
        ("R3 propelled samples exceed all samples", "TC-01-01", None, lambda m, s: pc(m, "RenderedEnemyDepth").update(propelledSamples=pc(m, "RenderedEnemyDepth")["samples"] + 1)),
    ]
    return run_mutations(ev, plane_check, cases, [])


def dev_selftest(ev):
    def first(s, typ, pred=lambda e: True):
        return next(e for e in s if e["type"] == typ and pred(e))
    cases = [
        ("pad page turn does not change the page", "TC-02-01", "DevPad_Pages", lambda m, s: first(s, "ring.page", lambda e: e["reason"].startswith("Next")).update(stateAfter="0")),
        ("keyboard left quickslot press comes from the pad", "TC-02-01", "DevKb_RegisterLeft",
         lambda m, s: first(s, "input.route", lambda e: e.get("action") == "QuickLeft" and e.get("edge") == "Press").__setitem__("device", PAD)),
        ("pad up quickslot uses another item than registered", "TC-02-01", "DevPad_UseQuickslots", lambda m, s: first(s, "item.request", lambda e: e.get("via") == "quickslot.Up").__setitem__("targetId", "elixir")),
        ("keyboard right quickslot reuses an earlier request id", "TC-02-01", "DevKb_UseQuickslots",
         lambda m, s: [e.__setitem__("requestId", 1) for e in s if e["type"] in ("item.request", "item.use") and e.get("requestId") == 2]),
        ("pad discard confirms another item", "TC-02-01", "DevPad_Discard", lambda m, s: first(s, "ring.discard").__setitem__("item", "potion")),
        ("keyboard B with no selection", "TC-02-01", "DevKb_Discard", lambda m, s: first(s, "ring.mode", lambda e: e["reason"].startswith("DiscardAsked")).update(selectedItem="")),
        ("pad registration without stick release", "TC-02-01", "DevPad_RegisterDown", lambda m, s: first(s, "quickslot.assign").__setitem__("stickReleasedSinceY", False)),
        ("keyboard up quickslot use removed", "TC-02-01", "DevKb_UseQuickslots", lambda m, s: remove(m, s, lambda e: e["type"] == "item.use" and e.get("targetId") == "potion")),
        # GPT WP-09-R1 review M02
        ("GPT-R1 assignment without a neutral event", "TC-02-01", "DevKb_RegisterUp", lambda m, s: m.remove(first(s, "ring.release"))),
        ("GPT-R1 wrong item used under a valid request id", "TC-02-01", "DevPad_UseQuickslots", lambda m, s: first(s, "item.use").update(targetId="key")),
        ("GPT-R1 discard asked for another selected item", "TC-02-01", "DevPad_Discard",
         lambda m, s: first(s, "ring.mode", lambda e: "DiscardAsked" in e["reason"]).update(selectedItem="key")),
        # GPT WP-09-R2 review #2-#4
        ("GPT-R2 keyboard up release with the stick held right", "TC-02-01", "DevKb_RegisterUp", lambda m, s: first(s, "ring.release").update(stick=[1, 0])),
        ("GPT-R2 pad request 5 executed a tick before it was made", "TC-02-01", "DevPad_UseQuickslots",
         lambda m, s: (lambda u: u.update(simulationTick=u["simulationTick"] - 2, execTick=u["execTick"] - 2))(first(s, "item.use", lambda e: e.get("requestId") == 5))),
        ("GPT-R2 pad discard inventory still holds the item", "TC-02-01", "DevPad_Discard",
         lambda m, s: (lambda d: d.update(inventory=d["inventory"] + "," + d["item"] + "x1"))(first(s, "ring.discard"))),
        # GPT WP-09-R3 review section 3 (false passes)
        ("GPT-R3 NextTick request runs two ticks late under an invented wait reason", "TC-02-01", "DevPad_UseQuickslots", r3_invented_wait),
        ("GPT-R3 discard snapshot is null", "TC-02-01", "DevPad_Discard", lambda m, s: first(s, "ring.discard").__setitem__("inventory", None)),
        ("R4 release stick exactly on the ring deadzone boundary", "TC-02-01", "DevKb_RegisterUp", r4_boundary_release),
    ]
    return run_mutations(ev, dev_check, cases, DEV_SCENARIOS)


def r3_invented_wait(m, s):
    """GPT WP-09-R3 probes.py `delayed`: move request 5's execution to request+2 with waitedFor set, no item.wait record."""
    u = next(e for e in s if e["type"] == "item.use" and e.get("requestId") == 5)
    m.remove(u)
    t = next(e for e in s if e["type"] == "tick" and e["simulationTick"] == 746)
    u.update(simulationTick=746, execTick=746, gameTime=t["gameTime"], execGameTime=t["gameTime"], realTime=t["realTime"], waitedFor="unsupported-wait")
    m.insert(m.index(t), u)
    for n, row in enumerate(m, m[0]["eventId"]):
        row["eventId"] = n


def r4_boundary_release(m, s):
    """The release stick is the smallest float x on the deadzone boundary that Dir8Util.FromStick does not treat as neutral.
    R7 (GPT R5-R6 review): with float arithmetic, [dz, 0] itself can be neutral (e.g. 0.35f), so step up one float at a time."""
    dz = ring_deadzone()
    v = f32(dz)
    while stick_neutral([v, 0], dz):
        v = struct.unpack("<f", struct.pack("<i", struct.unpack("<i", struct.pack("<f", v))[0] + 1))[0]
    for e in s:
        if e["type"] == "ring.release":
            e["stick"] = [v, 0]


def slide_selftest(ev):
    def first(s, typ, pred=lambda e: True):
        return next(e for e in s if e["type"] == typ and pred(e))
    def face_right(m, s):
        t = [e for e in of(s, "motor.state", "Slide(") if e.get("stateAfter") == "Slide"][2]["simulationTick"]
        for e in of(s, "tick"):
            if e["simulationTick"] == t: e["facing"] = 1
    cases = [
        ("keyboard left slide faces right", "TC-03-04", "SlideLeftKb", face_right),
        ("pad left slide ends outside the ceiling", "TC-03-04", "SlideLeftPad",
         lambda m, s: [e for e in of(s, "motor.state", "SlideEnd:Ceiling")][1].update(reason="SlideEnd", position=[43.0, 0])),
        ("pad left slide pressed on the keyboard", "TC-03-04", "SlideLeftPad",
         lambda m, s: [e for e in of(s, "input") if e.get("action") == "Jump" and e.get("edge") == "Press"][2].__setitem__("device", KB)),
        ("keyboard left slide never leaves the passage", "TC-03-04", "SlideLeftKb", lambda m, s: of(s, "motor.state", "SlideEnd")[-1].__setitem__("position", [40.9, 0])),
        # GPT WP-09-R1 review M02
        ("GPT-R1 slide claims an unrelated input id", "TC-03-04", "SlideLeftKb",
         lambda m, s: first(s, "motor.state", lambda e: e["simulationTick"] == 325 and e.get("stateAfter") == "Slide").update(inputEventId=90000)),
        ("GPT-R1 exit ticks never leave the ceiling", "TC-03-04", "SlideLeftKb",
         lambda m, s: [e.update(x=40.65) for e in of(s, "tick") if 360 <= e["simulationTick"] <= 377]),
        # GPT WP-09-R2 review #5, #6
        ("GPT-R2 exit slide reuses the entry press id", "TC-03-04", "SlideLeftKb",
         lambda m, s: first(s, "motor.state", lambda e: e["simulationTick"] == 360 and e.get("stateAfter") == "Slide").update(inputEventId=15)),
        ("GPT-R2 exit slide teleports on its last tick", "TC-03-04", "SlideLeftKb",
         lambda m, s: [e.update(x=40.6500053) for e in of(s, "tick") if 360 <= e["simulationTick"] <= 376]),
        # GPT WP-09-R3 review section 3
        ("GPT-R3 exit Jump delivered on another tick", "TC-03-04", "SlideLeftKb",
         lambda m, s: first(s, "input", lambda e: e.get("inputEventId") == 17).__setitem__("deliveredTick", 90000)),
    ]
    return run_mutations(ev, slide_check, cases, SLIDE_SCENARIOS)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    ev = load(args[0])
    src = load_cfg(args[0], ev)  # R4: without a verified snapshot of this run the config-bound rules fail closed
    if "--plane" in sys.argv: fn, st = plane_check, plane_selftest
    elif "--dev" in sys.argv: fn, st = dev_check, dev_selftest
    elif "--slide" in sys.argv: fn, st = slide_check, slide_selftest
    else: fn, st = check, selftest
    out = {"log": args[0], "dataSnapshot": src, **fn(ev)}
    if "--selftest" in sys.argv:
        out["selftest"] = st(ev)
        out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
