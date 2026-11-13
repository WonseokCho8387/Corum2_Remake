"""WP-11 log checks (Part A): TC-05-04, TC-08-02, TC-08-05, TC-08-09, TC-08-12 on the WP11_Evidence scene.

Usage:
  python Tools/wp11_checks.py <main run>.ndjson [--selftest]
"""
import copy
import json
import re
import sys

DT = 0.02
INPUTS = ("None", "Move", "Jump", "Attack", "Special", "Guard", "Ring")
H5 = [f"H5{d}_{i}" for d in ("Kb", "Pad") for i in INPUTS]
A2 = ["A2Ready", "A2Stagger", "A2Down", "A2Dead"]
J5 = ["J5NearIn", "J5NearOut", "J5FarIn", "J5FarOut"]
B9 = ["B9Solo", "B9Duo", "B9Warden"]
G12 = [f"G{s}{c}" for s in ("g1", "g2", "g3") for c in ("Phys", "Magic", "Parry", "Simul", "Launch")]
U2 = ["U2WallRun", "U2WallDash", "U2WallDive", "U2WallKnock", "U2WallEnemy"]
U7 = ["U7Multi", "U7Cancel", "U7Wakeup"]
SCENARIOS = H5 + A2 + J5 + B9 + G12 + U2 + U7 + ["ScriptEnd"]
KB, PAD = "Corum2VirtualKeyboard", "Corum2VirtualGamepad"
ENEMY = re.compile(r"([^:|]+):([A-Za-z]+)@(-?[\d.]+),(-?[\d.]+) hp(-?[\d.]+)(?: ph:(\w+))?")
ACTION = {"Jump": "Jump", "Attack": "Attack", "Special": "SpecialAttack", "Guard": "Guard", "Ring": "RingToggle", "Move": "MoveLeft"}
# Test data (Wp11ContentBuilder, wp11-1): still chaser range 1.6; jumper windows; boss phase tables.
RANGE = 1.6
JUMPER = {"J5NearIn": ("jn1", 3.0, 5.0, 5.0, 9.0, True), "J5NearOut": ("jn2", 3.0, 5.0, 5.0, 9.0, False),
          "J5FarIn": ("jf1", 5.0, 7.0, 7.0, 6.0, True), "J5FarOut": ("jf2", 5.0, 7.0, 7.0, 6.0, False)}
AIR_ATTACK_RANGE = 2.2
RESTRICTED = ("AttackStartup", "AttackActive", "AttackRecovery", "Stagger", "Launched", "Down", "Wakeup", "Dead", "DodgeReact")

# R3 knockback attribution rule (same as WP-07/08/10)
ATTR_FIELDS = ("attribution", "velocitySets", "firstSource", "lastSource", "firstAttackInstanceId", "lastAttackInstanceId", "aiMoveDuring", "blocked")


def attr_ok(k, source, inst):
    return all(f in k for f in ATTR_FIELDS) and k["attribution"] == "SingleSource" and k["velocitySets"] == 1 \
        and k["firstSource"] == k["lastSource"] == source and k["firstAttackInstanceId"] == k["lastAttackInstanceId"] == inst \
        and k["aiMoveDuring"] is False and k["blocked"] is False


def run_data(ev):
    """R1: the run's own data snapshot (<runId>.data.json, next to the log or under Evidence / Diagnostics), or None."""
    import pathlib
    rid = next((e.get("runId") for e in ev if e.get("runId")), None)
    f = globals().get("__file__")
    repo = pathlib.Path(f).resolve().parents[1] if f else pathlib.Path("D:/ProjectR/Corum2Remake")
    for d in [*DATA_DIRS, repo / "Evidence", repo / "Diagnostics"]:
        d = pathlib.Path(d)
        hit = d / (str(rid) + ".data.json")
        if not hit.is_file():
            hit = next(iter(sorted(d.rglob(str(rid) + ".data.json"))), None) if d.is_dir() else None
        if hit:
            return json.loads(hit.read_text(encoding="utf-8"))
    return None


DATA_DIRS = []


def attack_defs(data):
    """attackId -> (startup, active, recovery) from every attack definition in the run data."""
    out = {}
    def walk(o):
        if isinstance(o, dict):
            if isinstance(o.get("id"), str) and all(isinstance(o.get(k), (int, float)) for k in ("startup", "active", "recovery")):
                out.setdefault(o["id"], (o["startup"], o["active"], o["recovery"]))
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(data or {})
    return out


def knock_consistent(k, ev, eid):
    """R1 (GPT WP-11A review #6/#7): the knockback record agrees with itself and with the logged positions:
    displacement = endX - startX, its sign is the start velocity's, startX is the enemy x on the tick before startTick, endX on the record tick."""
    tk = {e["simulationTick"]: e for e in ev if e["type"] == "tick" and e["simulationTick"] in (k.get("startTick", -9) - 1, k["simulationTick"])}
    e0, e1 = enemy(tk.get(k.get("startTick", -9) - 1, {}), eid), enemy(tk.get(k["simulationTick"], {}), eid)
    return e0 is not None and e1 is not None and abs(k["displacement"] - (k["endX"] - k["startX"])) <= 1e-4 \
        and k["displacement"] * k.get("startVel", 0) > 0 and abs(k["startX"] - e0["x"]) <= 0.0051 and abs(k["endX"] - e1["x"]) <= 0.0051


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


def ticks(s):
    return {e["simulationTick"]: e for e in s if e["type"] == "tick"}


def enemy(t, eid):
    for m in ENEMY.finditer(str(t.get("enemies", ""))):
        if m.group(1) == eid:
            return {"state": m.group(2), "x": float(m.group(3)), "y": float(m.group(4)), "hp": float(m.group(5)), "phase": m.group(6)}
    return None


# ------------------------------------------------------------------ TC-05-04
def tc0504(seg):
    rows = []
    for dev, devname in (("Kb", KB), ("Pad", PAD)):
        ctrl = None
        for inp in INPUTS:
            s = seg.get(f"H5{dev}_{inp}", [])
            tk = ticks(s)
            hit = next((e for e in of(s, "hit.player") if e.get("stateAfter") == "HitStun"), None)
            end = next((e for e in of(s, "motor.state") if hit and e.get("stateBefore") == "HitStun" and e["simulationTick"] > hit["simulationTick"]), None)
            row = {"device": dev, "input": inp, "hitTick": hit and hit["simulationTick"], "stunEnd": end and end["simulationTick"]}
            ok = hit is not None and end is not None
            if ok:
                h, u = hit["simulationTick"], end["simulationTick"]
                stun = [tk[t] for t in range(h, u) if t in tk]
                ok = len(stun) == u - h and all(t.get("stateAfter") == "HitStun" for t in stun)
                traj = [round(tk[t]["x"] - tk[h]["x"], 4) for t in range(h, u + 1) if t in tk]
                row["stunTicks"] = u - h
                if inp == "None":
                    ctrl = {"traj": traj, "ticks": u - h}
                    row["pass"] = ok and not [e for e in s if e["type"] == "input.route" and e.get("edge") == "Press" and h <= e["simulationTick"] <= u + 30]
                    rows.append(row)
                    continue
                act = ACTION[inp]
                pr = [e for e in s if e["type"] == "input.route" and e.get("edge") == "Press" and e.get("action") == act and e["simulationTick"] > h]
                during = [p for p in pr if p["simulationTick"] < u]
                after = [p for p in pr if p["simulationTick"] >= u]
                ok = ok and len(during) >= 1 and len(after) >= 1 and all(p.get("device") == devname for p in pr)
                d, a = (during[0] if during else None), (after[0] if after else None)
                row.update({"pressDuring": d and d["simulationTick"], "pressAfter": a and a["simulationTick"]})
                if ok and inp == "Move":
                    # the held direction changes nothing during the stun: x follows the control's knockback tick by tick; walk after the end
                    walk = [e for e in of(s, "motor.state") if e.get("stateAfter") == "Walk" and a["simulationTick"] <= e["simulationTick"] <= a["simulationTick"] + 2]
                    held = [tk[t].get("heldDir") for t in range(d["simulationTick"], u) if t in tk]
                    row.update({"heldLeftDuringStun": "Left" in str(held), "sameAsControl": ctrl is not None and traj == ctrl["traj"], "walkAfter": bool(walk)})
                    ok = ok and row["heldLeftDuringStun"] and row["sameAsControl"] and bool(walk) and walk[0].get("vx", 0) < 0
                elif ok and inp == "Ring":
                    op = [e for e in of(s, "ring.open") if e["simulationTick"] in (d["simulationTick"], a["simulationTick"])]
                    rr = [e for e in of(s, "input.route") if e.get("reason") == "Open" and e.get("consumer") == "ring" and e.get("inputEventId") in (d["inputEventId"], a["inputEventId"])]
                    row.update({"ringOpenedDuringStun": any(e["simulationTick"] == d["simulationTick"] for e in op), "ringOpenedAfter": any(e["simulationTick"] == a["simulationTick"] for e in op)})
                    ok = ok and row["ringOpenedDuringStun"] and row["ringOpenedAfter"] and len(rr) == 2 and d["simulationTick"] < u
                elif ok:
                    rej = [e for e in of(s, "input.reject", "ActionLock:HitStun") if e.get("inputEventId") == d["inputEventId"]]
                    # nothing of that action starts during the stun
                    started = [e for e in s if h <= e["simulationTick"] < u and (e["type"] in ("motor.jump", "attack.start") or
                               (e["type"] == "motor.state" and e.get("stateAfter") == "Guard"))]
                    if inp == "Jump":
                        res = [e for e in of(s, "motor.jump") if e.get("inputEventId") == a["inputEventId"]]
                    elif inp in ("Attack", "Special"):
                        aid = "ground1" if inp == "Attack" else "launcher"
                        res = [e for e in of(s, "attack.start") if e.get("attackId") == aid and a["simulationTick"] <= e["simulationTick"] <= a["simulationTick"] + 1
                               and e.get("inputEventId") == a["inputEventId"]]  # R1 (GPT WP-11A review #1): executed by that press
                    else:
                        res = [e for e in of(s, "motor.state") if e.get("stateAfter") == "Guard" and a["simulationTick"] <= e["simulationTick"] <= a["simulationTick"] + 1]
                    row.update({"rejectedDuring": rej[0]["reason"] if rej else None, "startedDuringStun": len(started), "executedAfter": bool(res)})
                    ok = ok and len(rej) == 1 and not started and bool(res)
            row["pass"] = ok
            rows.append(row)
    return {"TC-05-04": {"cases": rows, "note": "virtual keyboard and pad; HitStun = WP-02 player reaction; Move compares x with the no-input control",
                         "pass": len(rows) == 2 * len(INPUTS) and all(r["pass"] for r in rows)}}


# ------------------------------------------------------------------ TC-08-02
def tc0802(seg):
    rows = []
    for name, eid in (("A2Ready", "pr"), ("A2Stagger", "ps"), ("A2Down", "pd"), ("A2Dead", "px")):
        s = seg.get(name, [])
        tk = ticks(s)
        track = []
        for t in sorted(tk):
            en = enemy(tk[t], eid)
            if en:
                track.append((t, en["state"], round(abs(tk[t]["x"] - en["x"]), 3)))
        starts = [e for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateAfter") == "AttackStartup"]
        blocked = [e for e in of(s, "ai.blocked") if e.get("actorId") == eid]
        st = dict((t, (state, d)) for t, state, d in track)
        # 1) every attack starts from Idle, with the player in range on that tick (state shown on the tick before)
        from_idle = all(e.get("stateBefore") == "Idle" and st.get(e["simulationTick"] - 1, ("", 99))[0] == "Idle"
                        and st.get(e["simulationTick"], ("", 99))[1] <= RANGE + 0.01 for e in starts)
        # 2) first chance: the first attack starts on the first tick the player is in range (or the next, positions are logged with 2 decimals)
        first_in = next((t for t, state, d in track if d <= RANGE), None)
        first_ok = bool(starts) and first_in is not None and starts[0]["simulationTick"] - first_in in (0, 1)
        # 3) in range during restricted states, no attack starts (an attack only starts from Idle, checked in 1); count those ticks per state
        restricted = {}
        for t, state, d in track:
            if state in RESTRICTED and d <= RANGE:
                restricted[state] = restricted.get(state, 0) + 1
        # 4) re-decision on the first Idle tick after each restriction while in range: an attack or a logged cooldown block; a block
        #    is followed by an attack when the re-attack interval runs out (still in range)
        redecide = []
        for i in range(1, len(track)):
            t, state, d = track[i]
            pstate = track[i - 1][1]
            if state == "Idle" and pstate in RESTRICTED and pstate != "Dead" and d <= RANGE:
                atk = next((e for e in starts if e["simulationTick"] - t in (0, 1)), None)
                # R1 (GPT WP-11A review #2): the block is the re-attack wait itself, with the player in range and time left
                blk = next((e for e in blocked if e["simulationTick"] - t in (0, 1) and str(e.get("reason", "")).startswith("Cooldown:ReattackInterval")
                            and e.get("cooldown", 0) > 0 and e.get("distance", 99) <= e.get("attackRange", RANGE) + 1e-6), None)
                later, past_end = None, False
                if blk:
                    due = blk["simulationTick"] + round(blk.get("cooldown", 0) / DT)
                    later = next((e for e in starts if abs(e["simulationTick"] - due) <= 1), None)
                    past_end = due > track[-1][0]  # the interval runs past the end of this segment (next case already started)
                redecide.append({"idleTick": t, "after": pstate, "attack": atk and atk["simulationTick"], "blocked": blk and blk["reason"],
                                 "attackWhenCooldownEnds": later and later["simulationTick"], "cooldownPastSegmentEnd": past_end,
                                 "ok": bool(atk) or bool(blk and (later or past_end))})
        row = {"case": name, "enemy": eid, "firstInRangeTick": first_in, "attackStarts": [e["simulationTick"] for e in starts],
               "inRangeTicksByRestrictedState": restricted, "redecisions": redecide}
        ok = from_idle and all(r["ok"] for r in redecide)
        if name == "A2Ready":
            ok = ok and first_ok and restricted.get("AttackRecovery", 0) > 0 and any(r["blocked"] for r in redecide)
        elif name == "A2Stagger":
            ok = ok and first_ok and restricted.get("Stagger", 0) > 0 and any(r["after"] == "Stagger" for r in redecide)
        elif name == "A2Down":
            ok = ok and first_ok and restricted.get("Down", 0) > 0 and restricted.get("Wakeup", 0) > 0 and any(r["after"] == "Wakeup" for r in redecide)
        else:
            death = next((e for e in of(s, "hit") if e.get("targetId") == eid and e.get("stateAfter") == "Dead"), None)
            dead = [(t, d) for t, state, d in track if state == "Dead"]
            left = next((i for i, (t, d) in enumerate(dead) if d > RANGE), None)  # the player leaves and re-enters the range while it is shown dead
            back = left is not None and any(d <= RANGE for t, d in dead[left:])
            row.update({"deathTick": death and death["simulationTick"], "leftAndReentered": back,
                        "attacksAfterDeath": [e["simulationTick"] for e in starts if death and e["simulationTick"] > death["simulationTick"]]})
            ok = ok and first_ok and death is not None and restricted.get("Dead", 0) > 0 and back and not row["attacksAfterDeath"]
        row["pass"] = ok
        rows.append(row)
    return {"TC-08-02": {"cases": rows, "note": "still chaser (0.01 m/s), range 1.6, re-attack interval 1.2 s (test data)", "pass": all(r["pass"] for r in rows)}}


# ------------------------------------------------------------------ TC-08-05
def tc0805(seg):
    rows, apex = [], {}
    for name, (eid, lo, hi, sx, vy, expect_jump) in JUMPER.items():
        s = seg.get(name, [])
        tk = ticks(s)
        jumps = [e for e in of(s, "ai.jump") if e.get("actorId") == eid]
        track = [(t, enemy(tk[t], eid), tk[t]["x"]) for t in sorted(tk) if enemy(tk[t], eid)]
        dists = [round(abs(px - en["x"]), 3) for t, en, px in track]
        row = {"case": name, "enemy": eid, "window": [lo, hi], "jumps": [(e["simulationTick"], e.get("distance"), e.get("speedX"), e.get("velocityY")) for e in jumps]}
        if expect_jump:
            j = jumps[0] if len(jumps) == 1 else None
            ok = j is not None and lo <= j.get("distance", -1) <= hi and j.get("speedX") == sx and j.get("velocityY") == vy
            dec = next((e for e in of(s, "ai.decide") if j and e.get("actorId") == eid and e["simulationTick"] == j["simulationTick"]), None)
            ok = ok and dec is not None and dec["reason"].startswith(f"JumpWindow[{lo:g},{hi:g}]")
            if ok:
                t0 = j["simulationTick"]
                air = [(t, en, px) for t, en, px in track if t >= t0]
                land = next((t for t, en, px in air if t > t0 and en["y"] == 0), None)
                arc = [(t, en, px) for t, en, px in air if land is None or t <= land]
                xs = [en["x"] for t, en, px in arc[:6]]
                speed = [round(abs(b - a) / DT, 2) for a, b in zip(xs, xs[1:])]
                apex[name] = max(en["y"] for t, en, px in arc)
                air_dist = min(abs(px - en["x"]) for t, en, px in arc)
                aa = [e for e in of(s, "ai.decide") if e.get("actorId") == eid and e["reason"].startswith("InAirAttackRange") and t0 <= e["simulationTick"] <= (land or 10 ** 9)]
                # R1 (GPT WP-11A review #3): each decision has its own AirAttack startup (same tick or next) that becomes active
                def realized(d):
                    st = next((e for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateAfter") == "AttackStartup"
                               and str(e.get("reason", "")).startswith("AirAttack") and 0 <= e["simulationTick"] - d["simulationTick"] <= 1), None)
                    return st is not None and any(e.get("actorId") == eid and e.get("attackInstanceId") == st.get("attackInstanceId") and e.get("stateAfter") == "AttackActive"
                                                  for e in of(s, "enemy.state"))
                aa_real = all(realized(d) for d in aa)
                row.update({"jumpTick": t0, "landTick": land, "apex": apex[name], "speedXMeasured": speed, "minDistanceInAir": round(air_dist, 2),
                            "airAttack": [(e["simulationTick"], round(e.get("distance", 0), 2)) for e in aa]})
                row["airAttackRealized"] = aa_real
                ok = land is not None and all(abs(v - sx) <= 0.5 for v in speed) and apex[name] > 0 \
                    and (bool(aa) == (air_dist <= AIR_ATTACK_RANGE + 0.01)) and all(e.get("distance", 99) <= AIR_ATTACK_RANGE for e in aa) and aa_real
        else:
            row["distanceRange"] = [min(dists or [0]), max(dists or [0])]
            ok = not jumps and bool(dists) and all(not (lo <= d <= hi) for d in dists)
        row["pass"] = ok
        rows.append(row)
    # height follows the configured velocity: apex ratio = (vy1 / vy2)^2 within 10 %
    ratio = apex.get("J5NearIn", 0) / apex["J5FarIn"] if apex.get("J5FarIn") else None
    height_ok = ratio is not None and abs(ratio / (9.0 / 6.0) ** 2 - 1) <= 0.1
    air_case = any(r.get("airAttack") for r in rows) and any(r.get("airAttack") == [] for r in rows if r["case"].endswith("In"))
    return {"TC-08-05": {"cases": rows, "apexRatioNearToFar": ratio and round(ratio, 3), "expectedApexRatio": 2.25, "heightFollowsConfig": height_ok,
                         "airAttackMetAndNotMet": air_case, "pass": all(r["pass"] for r in rows) and height_ok and air_case}}


# ------------------------------------------------------------------ TC-08-09
def tc0809(seg):
    rows = []
    spec = {"B9Solo": ("ba", []), "B9Duo": ("bb", [("b1", "b2", "HpBelow:0.5")]),
            "B9Warden": ("bc", [("p1", "p2", "HpBelow:0.6"), ("p2", "p3", "PatternUsed:crushx2")])}
    for name, (eid, expect) in spec.items():
        s = seg.get(name, [])
        ph = [e for e in of(s, "boss.phase") if e.get("actorId") == eid]
        got = [(e["stateBefore"], e["stateAfter"], e.get("condition")) for e in ph]
        hits = [e for e in of(s, "hit") if e.get("targetId") == eid]
        low = min([e.get("hpAfter", 10 ** 9) for e in hits] or [10 ** 9])
        conds = []
        for e in ph:
            c = e.get("condition", "")
            if c.startswith("HpBelow:"):
                conds.append(e.get("hpRatio", 1) < float(c.split(":")[1]))
            elif c.startswith("PatternUsed:"):
                nm, n = c.split(":")[1].rsplit("x", 1)
                used = [p for p in of(s, "boss.pattern") if p.get("actorId") == eid and p["stateBefore"] == e["stateBefore"] and p["stateAfter"] == nm
                        and p["simulationTick"] <= e["simulationTick"]]
                conds.append(len(used) == int(n))
            else:
                conds.append(False)
        tk = ticks(s)
        last = tk[max(tk)] if tk else {}
        en = enemy(last, eid) or {}
        row = {"case": name, "boss": eid, "transitions": got, "lowestHpAfterHit": low, "conditionsMet": conds, "phaseAtEnd": en.get("phase")}
        ok = got == expect and all(conds) and bool(hits)
        if expect:  # R1 (GPT WP-11A review #4): the boss is still in the phase its last transition entered
            ok = ok and en.get("phase") == expect[-1][1]
        if name == "B9Solo":  # one phase: even at 10 % HP (below every other boss's threshold) there is no transition
            ok = ok and low / 100.0 <= 0.1 and en.get("phase") == "a1"
        row["pass"] = ok
        rows.append(row)
    thresholds = sorted({c for r in rows for _, _, c in r["transitions"] if c and c.startswith("HpBelow")})
    return {"TC-08-09": {"cases": rows, "hpThresholds": thresholds,
                         "note": "phase count and transitions per boss data (A 1, B 2, C 3); thresholds differ per boss (no common value)",
                         "pass": all(r["pass"] for r in rows) and len(thresholds) == 2}}


# ------------------------------------------------------------------ TC-08-12
EXPECT = {  # state -> case -> (event type, reason prefix, boss state after)
    "g1": {"Phys": ("hit", "Stagger(R-06-04)", "Stagger"), "Magic": ("hit", "MagicStagger+Knockback", "Stagger"), "Parry": ("guard.parry", "Parry", None),
           "Simul": ("dodge", "SimultaneousHit", "DodgeReact"), "Launch": ("hit", "Launched:Launchable", "Launched")},
    "g2": {"Phys": ("hit", "SuperArmor:DamageOnly", "AttackStartup"), "Magic": ("hit", "MagicBreaksArmor", "Stagger"), "Parry": ("guard.parry", "Parry", None),
           "Simul": ("dodge", "SimultaneousHit", "DodgeReact"), "Launch": ("hit", "SuperArmor:DamageOnly", "AttackStartup")},
    "g3": {"Phys": ("hit", "Stagger(R-06-04)", "Stagger"), "Magic": ("hit", "MagicStagger+Knockback", "Stagger"), "Parry": ("guard.parry", "Parry", None),
           "Simul": ("dodge", "SimultaneousHit", "DodgeReact"), "Launch": ("hit", "LaunchImmune:DamageAndCount", "Stagger")},
}
PHASE = {"g1": (False, "Launchable"), "g2": (True, "Launchable"), "g3": (False, "Immune")}


def tc0812(seg, ev):
    rows = []
    for st, cases in EXPECT.items():
        for c, (typ, reason, after) in cases.items():
            name = f"G{st}{c}"
            b = f"x{st}{c}"
            s = seg.get(name, [])
            tk = ticks(s)
            ph = [e for e in of(ev, "boss.phase") if e.get("actorId") == b]
            phase = ph[-1] if ph else None
            res = next((e for e in s if e["type"] == typ and (e.get("targetId") == b) and str(e.get("reason", "")).startswith(reason)), None)
            # the boss attack this case lands on: its last startup before the result (the await marks the segment after that startup)
            startup = ([e for e in of(ev, "enemy.state") if res and e.get("actorId") == b and e.get("stateAfter") == "AttackStartup" and e["eventId"] < res["eventId"]] or [None])[-1]
            inst = startup and startup.get("attackInstanceId")
            row = {"case": name, "phase": phase and phase["stateAfter"], "phaseSuperArmor": phase and phase.get("superArmor"), "phaseLaunch": phase and phase.get("launch"),
                   "result": res and (res["type"], res["simulationTick"], res.get("reason"), res.get("stateAfter"))}
            ok = phase is not None and phase["stateAfter"] == st and (phase.get("superArmor"), phase.get("launch")) == PHASE[st] and res is not None and startup is not None
            if ok:
                t = res["simulationTick"]
                en = enemy(tk.get(t, {}), b) or {}
                row["phaseOnTick"] = en.get("phase")
                ok = en.get("phase") == st
                # the case lands while the boss's own attack is in startup (or active for parry / simultaneous)
                if typ == "hit":
                    ok = ok and res.get("stateBefore") == "AttackStartup" and res.get("stateAfter") == after
                    # R1 (GPT WP-11A review #5): HP really drops, and the boss shows hpAfter on that tick
                    ok = ok and res.get("hpAfter", 1e9) < res.get("hpBefore", -1) and en.get("hp") is not None and abs(en["hp"] - res["hpAfter"]) <= 0.01
                    acts = [e for e in of(ev, "enemy.state") if e.get("actorId") == b and e.get("attackInstanceId") == inst and e.get("stateAfter") == "AttackActive"]
                    row["bossAttackActiveAfter"] = bool(acts)
                    ok = ok and (bool(acts) == (after == "AttackStartup"))  # armor keeps the attack; every other result cancels it
                    if c == "Magic":
                        k = next((x for x in of(s, "enemy.knockback") if x.get("actorId") == b and x.get("startTick", -1) > t), None)
                        row["knockback"] = k and (k.get("startVel"), k.get("displacement"))
                        ok = ok and k is not None and attr_ok(k, "Hit:magic.fire", res.get("attackInstanceId")) and k["displacement"] > 0 and knock_consistent(k, ev, b)
                    if c == "Launch":
                        ok = ok and res.get("attackId") == "launcher"
                elif typ == "guard.parry":
                    ok = ok and res.get("enemyAttackInstanceId") == inst and res.get("rbToHit", 1) <= res.get("parryWindow", 0)
                    k = next((x for x in of(s, "enemy.knockback") if x.get("actorId") == b and x.get("startTick", -1) >= t), None)
                    # parried boss: knocked back and unguarded (Stagger on that tick; its attack does not go on)
                    acts = [e for e in of(ev, "enemy.state") if e.get("actorId") == b and e.get("attackInstanceId") == inst and e.get("stateAfter") == "AttackRecovery"]
                    row.update({"knockback": k and (k.get("startVel"), k.get("displacement")), "bossOnTick": en.get("state"), "attackRecoveredAfter": bool(acts)})
                    ok = ok and k is not None and attr_ok(k, "Parry", inst) and k["displacement"] > 0 and en.get("state") == "Stagger" and not acts
                else:
                    hp0 = (tk.get(t - 1, {}).get("hp"), (enemy(tk.get(t - 1, {}), b) or {}).get("hp"))
                    hp1 = (tk.get(t, {}).get("hp"), (enemy(tk.get(t, {}), b) or {}).get("hp"))
                    row.update({"damage": res.get("damage"), "hpBeforeAfter": [hp0, hp1]})
                    ok = ok and res.get("enemyAttackInstanceId") == inst and res.get("damage") == 0 and hp0 == hp1 and None not in hp0 \
                        and res.get("stateAfter") == "DodgeReact" and en.get("state") == "DodgeReact" and res.get("enemyArmor") == (st == "g2")
            row["pass"] = ok
            rows.append(row)
    return {"TC-08-12": {"cases": rows, "note": "gauntlet test boss: g1 normal (launchable), g2 super armor, g3 launch immune; transitions by events only (CF-09 not exercised)",
                         "pass": len(rows) == 15 and all(r["pass"] for r in rows)}}


# ------------------------------------------------------------------ TC-UNITY-02
PLAYER_HALF, BAG_HALF = 0.3, 0.4  # WP-01 movement body width 0.6, WP-02 bag 0.8
TRACK_PLAYER = ("walkRun", "air", "airDash", "dive", "knockback")
TRACK_ENEMY = ("air", "knockback")


def track_table(s):
    out = {}
    for part in str(s).split(";"):
        if not part:
            continue
        k, v = part.split(":")
        f = v.split("/")
        out[k] = {"frames": int(f[0]), "renderPen": float(f[1]), "simPen": float(f[2]), "size": float(f[3]), "outside": float(f[4]), "step": float(f[5])}
    return out


def tcu02(seg, ev):
    tc = {e["actorId"]: track_table(e.get("classes")) for e in of(ev, "view.trackCheck")}
    p, en = tc.get("player", {}), tc.get("enemies", {})
    zero = lambda t: all(c["renderPen"] == 0 and c["simPen"] == 0 and c["size"] == 0 and c["outside"] <= 1e-4 for c in t.values())
    rows = []
    for name in ("U2WallRun", "U2WallDash", "U2WallDive"):
        s = seg.get(name, [])
        sp = next((e for e in of(s, "motor.place") if e.get("spawnId") == "U2WallMove"), None)
        tk = ticks(s)
        limit = sp["position"][0] + 5 - PLAYER_HALF if sp else None  # wall min x - half width
        xs = [tk[t]["x"] for t in sorted(tk)]
        motion = {"U2WallRun": ("Run", None), "U2WallDash": ("AirDash", "airDash"), "U2WallDive": ("Dive", "dive")}[name]
        at_wall = [tk[t] for t in sorted(tk) if limit and abs(tk[t]["x"] - limit) <= 1e-3]
        in_motion = [t for t in at_wall if t.get("stateAfter") == motion[0]]
        start = [e for e in of(s, "attack.start") if e.get("attackId") == motion[1]] if motion[1] else [1]
        row = {"case": name, "wallContactX": limit, "maxX": max(xs) if xs else None, "contactTicksInMotion": len(in_motion), "motionStarted": bool(start)}
        row["pass"] = limit is not None and bool(xs) and max(xs) <= limit + 1e-3 and len(in_motion) >= 1 and bool(start)
        rows.append(row)
    s = seg.get("U2WallKnock", [])
    sp = next((e for e in of(s, "motor.place") if e.get("spawnId") == "U2WallKnock"), None)
    tk = ticks(s)
    hit = next((e for e in of(s, "hit.player")), None)
    limit = sp["position"][0] - 0.35 + PLAYER_HALF if sp else None
    xs = [tk[t]["x"] for t in sorted(tk) if hit and t >= hit["simulationTick"]]
    row = {"case": "U2WallKnock", "wallContactX": limit, "minX": min(xs) if xs else None,
           "knockbackTicksAtWall": len([t for t in sorted(tk) if hit and limit is not None and t >= hit["simulationTick"]
                                        and abs(tk[t]["x"] - limit) <= 1e-3 and tk[t].get("stateAfter") == "HitStun"])}
    row["pass"] = hit is not None and bool(xs) and min(xs) >= limit - 1e-3 and row["knockbackTicksAtWall"] >= 1
    rows.append(row)
    s = seg.get("U2WallEnemy", [])
    sp = next((e for e in of(s, "motor.place") if e.get("spawnId") == "U2WallEnemy"), None)
    k = next((x for x in of(s, "enemy.knockback") if x.get("actorId") == "ub"), None)
    limit = sp["position"][0] + 2 - BAG_HALF if sp else None
    row = {"case": "U2WallEnemy", "wallContactX": limit, "knockback": k and {f: k.get(f) for f in ("startX", "endX", "displacement", "startVel", "blocked", "attribution")}}
    # positive control for the R2 'blocked' flag: the wall stops the bag short of the free 0.468 m, and the record says so
    row["pass"] = k is not None and limit is not None and abs(k["endX"] - limit) <= 1e-3 and k["displacement"] < 0.4 and k.get("blocked") is True \
        and k.get("attribution") == "Ambiguous" and k.get("velocitySets") == 1 and knock_consistent(k, ev, "ub")  # R1 (GPT WP-11A review #7)
    rows.append(row)
    view = {"player": p, "enemies": en, "playerClassesPresent": [c for c in TRACK_PLAYER if p.get(c, {}).get("frames", 0) > 0],
            "enemyClassesPresent": [c for c in TRACK_ENEMY if en.get(c, {}).get("frames", 0) > 0]}
    view_ok = bool(p) and bool(en) and zero(p) and zero(en) and len(view["playerClassesPresent"]) == len(TRACK_PLAYER) and len(view["enemyClassesPresent"]) == len(TRACK_ENEMY)
    # R1 (GPT WP-11A review #8): the rendered depth evidence is present: player and enemies on one plane with zero error
    pc = {e["reason"].split("(")[0]: e for e in of(ev, "view.planeCheck")}
    pp, pe = pc.get("RenderedPlayerDepth"), pc.get("RenderedEnemyDepth")
    view["planeCheck"] = pp is not None and pe is not None and pp.get("frames", 0) > 0 and pe.get("samples", 0) > 0 \
        and pp.get("maxAbsZError") == 0 and pe.get("maxAbsZError") == 0 and pp.get("planeZ") == pe.get("planeZ")
    view_ok = view_ok and view["planeCheck"]
    return {"TC-UNITY-02": {"walls": rows, "view": view,
                            "note": "rendered vs judged body per motion class (view.trackCheck: penetration into solids, size, outside the tick segment); planeZ is TC-01-01",
                            "pass": all(r["pass"] for r in rows) and view_ok}}


# ------------------------------------------------------------------ TC-UNITY-07
def tcu07(seg, ev):
    # whole run: every player attack resolves only while it is alive, at most once per (attack, target, hit event), and its end
    # record counts exactly the hits logged for it; every enemy hit on the player comes from that enemy's active attack instance.
    starts = {e["attackInstanceId"]: e["simulationTick"] for e in ev if e.get("actorId") == "player" and e["type"] in ("attack.start", "magic.cast")}
    ends = {e["attackInstanceId"]: e for e in of(ev, "attack.end")}
    hits = [e for e in of(ev, "hit") if e.get("actorId") == "player"]
    keys = [(e["attackInstanceId"], e["targetId"], e.get("hitEventId")) for e in hits]
    alive = [e for e in hits if e["attackInstanceId"] in starts and e["attackInstanceId"] in ends
             and starts[e["attackInstanceId"]] <= e["simulationTick"] <= ends[e["attackInstanceId"]]["simulationTick"]]
    count_ok = all(ends[i].get("hits") == sum(1 for e in hits if e["attackInstanceId"] == i) for i in ends)
    # R1 (GPT WP-11A review #9): a timed (not motor-driven, not magic) hit lies in its attack's active window from the run data, by the
    # logged motion progress, and the tick count since the start agrees with that progress (one tick of slack)
    defs = attack_defs(run_data(ev))
    def in_window(h):
        d = defs.get(h.get("attackId"))
        if h.get("attackKind") in ("AirDash", "Dive") or h.get("damageType") == "Magic" or h.get("magicId"):
            return True
        if not d or h["attackInstanceId"] not in starts or not isinstance(h.get("motionProgress"), (int, float)):
            return False
        su, ac, rc = d
        el = h["motionProgress"] * (su + ac + rc)
        by_ticks = (h["simulationTick"] - starts[h["attackInstanceId"]] + 1) * DT
        return su - 1e-3 <= el <= su + ac + 1e-3 and abs(el - by_ticks) <= DT + 1e-3
    window = [e for e in hits if in_window(e)]
    skips = [e for e in of(ev, "hit.skip") if e.get("actorId") == "player"]
    skip_alive = all(e.get("attackInstanceId") in starts and e.get("attackInstanceId") in ends
                     and starts[e["attackInstanceId"]] <= e["simulationTick"] <= ends[e["attackInstanceId"]]["simulationTick"] for e in skips)
    est = {}
    for e in of(ev, "enemy.state"):
        est.setdefault(e.get("actorId"), []).append(e)

    def enemy_active(h):
        rec = [e for e in est.get(h.get("actorId"), []) if e.get("attackInstanceId") == h.get("enemyAttackInstanceId") and e["eventId"] < h["eventId"]]
        return bool(rec) and rec[-1].get("stateAfter") == "AttackActive"
    phit = of(ev, "hit.player")
    glob = {"playerHits": len(hits), "uniqueHitKeys": len(set(keys)) == len(keys), "hitsWhileAttackAlive": len(alive), "hitsInActiveWindow": len(window), "endCountsMatch": count_ok,
            "skipsWhileAttackAlive": skip_alive, "enemyHitsOnPlayer": len(phit), "enemyHitsFromActiveInstance": sum(1 for h in phit if enemy_active(h))}
    glob_ok = glob["uniqueHitKeys"] and len(alive) == len(hits) and count_ok and skip_alive and glob["enemyHitsFromActiveInstance"] == len(phit) and len(hits) > 0 \
        and len(window) == len(hits) and bool(defs)
    rows = []
    s = seg.get("U7Multi", [])
    a = next((e for e in of(s, "attack.start")), None)
    mh = [e for e in of(s, "hit") if a and e["attackInstanceId"] == a["attackInstanceId"]]
    rows.append({"case": "U7Multi", "targets": sorted(e["targetId"] for e in mh), "endHits": ends.get(a and a["attackInstanceId"], {}).get("hits"),
                 "pass": a is not None and sorted(e["targetId"] for e in mh) == ["m1", "m2", "m3", "m4"] and ends.get(a["attackInstanceId"], {}).get("hits") == 4})
    s = seg.get("U7Cancel", [])
    cast = next((e for e in of(s, "magic.cast")), None)
    ph = next((e for e in of(s, "hit.player") if cast and e["simulationTick"] >= cast["simulationTick"]), None)
    end = ends.get(cast and cast["attackInstanceId"])
    mc = next((e for e in of(s, "magic.cancel") if cast and e.get("attackInstanceId") == cast["attackInstanceId"]), None)
    late = [e for e in s if cast and e.get("attackInstanceId") == cast["attackInstanceId"] and e["type"] in ("magic.effect", "hit")]
    rows.append({"case": "U7Cancel", "castTick": cast and cast["simulationTick"], "hitOnPlayerTick": ph and ph["simulationTick"],
                 "end": end and (end["simulationTick"], end["reason"], end.get("hitboxRemoved")), "magicCancel": mc and (mc["simulationTick"], mc.get("effectApplied")),
                 "effectsOrHitsOfThatCast": len(late),
                 "pass": cast is not None and ph is not None and end is not None and end["simulationTick"] == ph["simulationTick"] and end.get("hitboxRemoved") is True
                 and mc is not None and mc.get("effectApplied") is False and not late
                 # R1 (GPT WP-11A review #10): the cancel is on the hit tick and names the hit as its cause; the core cancels first (attack.end,
                 # magic.cancel) and then records that hit on the player, so the hit is the next hit.player after the cancel on the same tick
                 and mc["simulationTick"] == ph["simulationTick"] == end["simulationTick"] and end["eventId"] < mc["eventId"] < ph["eventId"]
                 and not [x for x in of(s, "hit.player") if mc["eventId"] < x["eventId"] < ph["eventId"]] and str(mc.get("reason", "")).startswith("PlayerHit")})
    s = seg.get("U7Wakeup", [])
    seq = [(e["type"], e.get("stateAfter") if e["type"] == "hit.skip" else e.get("stateBefore"), e["attackInstanceId"]) for e in s
           if e["type"] in ("hit", "hit.skip") and e.get("targetId") == "uw"]
    kinds = [(t, st) for t, st, _ in seq]
    rows.append({"case": "U7Wakeup", "sequence": seq,
                 "pass": kinds == [("hit", "Idle"), ("hit.skip", "Down"), ("hit.skip", "Wakeup"), ("hit", "Idle")] and len({i for _, _, i in seq}) == 4})
    return {"TC-UNITY-07": {"wholeRun": glob, "cases": rows, "pass": glob_ok and all(r["pass"] for r in rows)}}


def check(ev):
    seg = segments(ev)
    out = {}
    out.update(tc0504(seg))
    out.update(tc0802(seg))
    out.update(tc0805(seg))
    out.update(tc0809(seg))
    out.update(tc0812(seg, ev))
    out.update(tcu02(seg, ev))
    out.update(tcu07(seg, ev))
    return out


# ------------------------------------------------------------------ self-tests
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

    def first(m, scn, typ, pred=lambda e: True):
        return next(e for e in segments(m)[scn] if e["type"] == typ and pred(e))

    def tick_of(m, t):
        return next(e for e in m if e["type"] == "tick" and e["simulationTick"] == t)

    # TC-05-04
    mutate("05-04 jump during HitStun executes", "TC-05-04", lambda m: first(m, "H5Kb_Jump", "input.reject").update(type="motor.jump", reason="Jump:Standing"))
    mutate("05-04 pad attack refused with another lock", "TC-05-04", lambda m: first(m, "H5Pad_Attack", "input.reject").update(reason="ActionLock:GroundAttack"))
    mutate("05-04 refusal names another press", "TC-05-04", lambda m: first(m, "H5Kb_Guard", "input.reject").update(inputEventId=-1))
    def move_steers(m):
        h = first(m, "H5Kb_Move", "hit.player")["simulationTick"]
        tick_of(m, h + 8)["x"] -= 0.05
    mutate("05-04 held Left moves the player during HitStun", "TC-05-04", move_steers)
    mutate("05-04 ring does not open during HitStun", "TC-05-04", lambda m: m.remove(first(m, "H5Pad_Ring", "ring.open")))
    mutate("05-04 special never executes after the end", "TC-05-04",
           lambda m: [e.update(attackId="other") for e in segments(m)["H5Kb_Special"] if e["type"] == "attack.start"])
    mutate("05-04 keyboard case pressed on the pad", "TC-05-04",
           lambda m: first(m, "H5Kb_Attack", "input.route", lambda e: e.get("edge") == "Press").update(device=PAD))
    mutate("05-04 stun ends early (a free tick inside)", "TC-05-04",
           lambda m: tick_of(m, first(m, "H5Pad_Jump", "hit.player")["simulationTick"] + 5).update(stateAfter="Idle"))
    # TC-08-02
    def late_first(m):
        e = first(m, "A2Ready", "enemy.state", lambda e: e.get("stateAfter") == "AttackStartup"); e["simulationTick"] += 3
    mutate("08-02 ready enemy waits after entering range", "TC-08-02", late_first)
    def attack_in_stagger(m):
        s = segments(m)["A2Stagger"]
        st = next(e for e in s if e["type"] == "enemy.state" and e.get("stateAfter") == "Stagger" or (e["type"] == "hit" and e.get("stateAfter") == "Stagger"))
        t = st["simulationTick"] + 3
        x = copy.deepcopy(first(m, "A2Stagger", "enemy.state", lambda e: e.get("stateAfter") == "AttackStartup"))
        x.update(simulationTick=t, stateBefore="Stagger", eventId=st["eventId"] + 0.5)
        m.insert(m.index(st) + 1, x)
    mutate("08-02 attack starts during stagger", "TC-08-02", attack_in_stagger)
    mutate("08-02 no re-decision after wakeup", "TC-08-02",
           lambda m: [m.remove(e) for e in [e for e in segments(m)["A2Down"] if e["type"] in ("ai.blocked",) and e.get("actorId") == "pd"]])
    def dead_attacks(m):
        d = first(m, "A2Dead", "hit", lambda e: e.get("stateAfter") == "Dead")
        x = copy.deepcopy(first(m, "A2Dead", "enemy.state", lambda e: e.get("stateAfter") == "AttackStartup"))
        x.update(simulationTick=d["simulationTick"] + 40, eventId=d["eventId"] + 0.5, stateBefore="Idle")
        m.insert(m.index(d) + 1, x)
    mutate("08-02 dead enemy attacks", "TC-08-02", dead_attacks)
    # TC-08-05
    mutate("08-05 jump outside the window", "TC-08-05", lambda m: m.insert(m.index(first(m, "J5NearOut", "marker")) + 1,
           dict(first(m, "J5NearIn", "ai.jump"), actorId="jn2", distance=6, eventId=first(m, "J5NearOut", "marker")["eventId"] + 0.5)))
    mutate("08-05 jump speed differs from the data", "TC-08-05", lambda m: first(m, "J5FarIn", "ai.jump").update(speedX=5))
    def far_air_attack(m):
        j = first(m, "J5FarIn", "ai.jump")
        x = copy.deepcopy(first(m, "J5NearIn", "ai.decide", lambda e: e["reason"].startswith("InAirAttackRange")))
        x.update(actorId="jf1", simulationTick=j["simulationTick"] + 5, eventId=j["eventId"] + 0.5, distance=3.4)
        m.insert(m.index(j) + 1, x)
    mutate("08-05 air attack out of its range", "TC-08-05", far_air_attack)
    def flat_jump(m):
        j = first(m, "J5NearIn", "ai.jump")
        for e in segments(m)["J5NearIn"]:
            if e["type"] == "tick" and e["simulationTick"] > j["simulationTick"]:
                e["enemies"] = re.sub(r"(jn1:\w+@-?[\d.]+,)(-?[\d.]+)", lambda x: x.group(1) + ("%.2f" % min(float(x.group(2)), 0.3)), e["enemies"])
    mutate("08-05 near jump as low as the far one", "TC-08-05", flat_jump)
    # TC-08-09
    def solo_transitions(m):
        h = first(m, "B9Solo", "hit", lambda e: e.get("targetId") == "ba")
        x = dict(first(m, "B9Duo", "boss.phase"), actorId="ba", stateBefore="a1", stateAfter="a2", condition="HpBelow:0.5", eventId=h["eventId"] + 0.5)
        m.insert(m.index(h) + 1, x)
    mutate("08-09 one-phase boss transitions", "TC-08-09", solo_transitions)
    mutate("08-09 two-phase boss uses the warden threshold", "TC-08-09", lambda m: first(m, "B9Duo", "boss.phase").update(condition="HpBelow:0.6"))
    mutate("08-09 warden p3 without two crushes", "TC-08-09", lambda m: first(m, "B9Warden", "boss.pattern", lambda e: e["stateAfter"] == "crush").update(stateAfter="shot2"))
    # TC-08-12
    mutate("08-12 armor phase physical hit staggers", "TC-08-12", lambda m: first(m, "Gg2Phys", "hit").update(reason="Stagger(R-06-04)", stateAfter="Stagger"))
    mutate("08-12 launch-immune boss launched", "TC-08-12", lambda m: first(m, "Gg3Launch", "hit").update(reason="Launched:Launchable(R-06-08)", stateAfter="Launched"))
    mutate("08-12 normal boss magic knockback from two sources", "TC-08-12", lambda m: first(m, "Gg1Magic", "enemy.knockback").update(velocitySets=2, attribution="Ambiguous"))
    mutate("08-12 parry on another attack", "TC-08-12", lambda m: first(m, "Gg2Parry", "guard.parry").update(enemyAttackInstanceId=-1))
    mutate("08-12 simultaneous hit costs HP", "TC-08-12", lambda m: tick_of(m, first(m, "Gg3Simul", "dodge")["simulationTick"]).update(hp=1))
    mutate("08-12 case in the wrong phase", "TC-08-12", lambda m: [e.update(stateAfter="g1") for e in m if e["type"] == "boss.phase" and e.get("actorId") == "xg2Magic"])
    def armor_keeps(m):
        h = first(m, "Gg2Magic", "hit")
        inst = [e for e in m if e["type"] == "enemy.state" and e.get("actorId") == "xg2Magic" and e.get("stateAfter") == "AttackStartup" and e["eventId"] < h["eventId"]][-1]["attackInstanceId"]
        x = dict(first(m, "Gg2Phys", "enemy.state", lambda e: e.get("stateAfter") == "AttackActive"), actorId="xg2Magic", attackInstanceId=inst, eventId=h["eventId"] + 0.5)
        m.insert(m.index(h) + 1, x)
    mutate("08-12 armor keeps its attack after magic", "TC-08-12", armor_keeps)
    # TC-UNITY-02
    def view_pen(m):
        e = next(x for x in m if x["type"] == "view.trackCheck" and x["actorId"] == "player")
        e["classes"] = re.sub(r"(airDash:\d+/)[\d.]+", lambda x: x.group(1) + "0.0400", e["classes"])
    mutate("U-02 drawn player enters the wall during the dash", "TC-UNITY-02", view_pen)
    def view_lag(m):
        e = next(x for x in m if x["type"] == "view.trackCheck" and x["actorId"] == "player")
        e["classes"] = re.sub(r"(knockback:\d+/[\d.]+/[\d.]+/[\d.]+/)[\d.]+", lambda x: x.group(1) + "0.0300", e["classes"])
    mutate("U-02 drawn player outside its tick segment in knockback", "TC-UNITY-02", view_lag)
    mutate("U-02 no enemy knockback frames", "TC-UNITY-02",
           lambda m: next(x for x in m if x["type"] == "view.trackCheck" and x["actorId"] == "enemies").update(classes="still:10/0.0000/0.0000/0.0000/0.0000/0.0000"))
    def through_wall(m):
        s = segments(m)["U2WallDash"]
        t = [e for e in s if e["type"] == "tick" and e.get("stateAfter") == "AirDash"][-1]
        t["x"] += 0.2
    mutate("U-02 air dash passes the wall", "TC-UNITY-02", through_wall)
    mutate("U-02 wall-blocked bag knockback not flagged", "TC-UNITY-02", lambda m: first(m, "U2WallEnemy", "enemy.knockback").update(blocked=False, attribution="SingleSource"))
    mutate("U-02 knockback stops short of the wall", "TC-UNITY-02",
           lambda m: [e.update(x=e["x"] + 0.05) for e in segments(m)["U2WallKnock"] if e["type"] == "tick" and e["simulationTick"] > first(m, "U2WallKnock", "hit.player")["simulationTick"]])
    # TC-UNITY-07
    def dup_hit(m):
        h = first(m, "U7Multi", "hit")
        x = dict(h, simulationTick=h["simulationTick"] + 2, eventId=h["eventId"] + 0.5)
        m.insert(m.index(h) + 1, x)
    mutate("U-07 same target hit twice in one motion", "TC-UNITY-07", dup_hit)
    def late_hit(m):
        h = first(m, "U7Wakeup", "hit", lambda e: e.get("stateBefore") == "Idle" and e["eventId"] > first(m, "U7Wakeup", "hit.skip")["eventId"])
        end = next(e for e in m if e["type"] == "attack.end" and e["attackInstanceId"] == h["attackInstanceId"])
        h["simulationTick"] = end["simulationTick"] + 1
    mutate("U-07 hit after its attack ended", "TC-UNITY-07", late_hit)
    mutate("U-07 cancelled cast still applies its effect", "TC-UNITY-07", lambda m: first(m, "U7Cancel", "magic.cancel").update(effectApplied=True))
    mutate("U-07 end record counts another number of hits", "TC-UNITY-07", lambda m: first(m, "U7Multi", "attack.end").update(hits=3))
    mutate("U-07 enemy hit from an attack instance that is not active", "TC-UNITY-07", lambda m: first(m, "U7Cancel", "hit.player").update(enemyAttackInstanceId=-7))
    mutate("U-07 waking target hit", "TC-UNITY-07", lambda m: first(m, "U7Wakeup", "hit.skip", lambda e: e.get("stateAfter") == "Wakeup").update(type="hit", stateBefore="Wakeup"))
    # GPT WP-11A review section 4 (#1-#10)
    mutate("GPT-11A 05-04 accepted attack credited to an unrelated input", "TC-05-04", lambda m: first(m, "H5Kb_Attack", "attack.start").update(inputEventId=90000))
    mutate("GPT-11A 08-02 cooldown block reason replaced", "TC-08-02", lambda m: first(m, "A2Ready", "ai.blocked").update(reason="OutOfRange"))
    mutate("GPT-11A 08-05 air attack decision without startup", "TC-08-05",
           lambda m: m.remove(first(m, "J5NearIn", "enemy.state", lambda e: e.get("actorId") == "jn1" and e.get("stateAfter") == "AttackStartup")))
    def wrong_phase(m):
        e = [x for x in segments(m)["B9Duo"] if x["type"] == "tick"][-1]
        e["enemies"] = re.sub(r"(bb:[^|]+ ph:)b2", r"\g<1>b1", e["enemies"])
    mutate("GPT-11A 08-09 last phase contradicts the transition", "TC-08-09", wrong_phase)
    mutate("GPT-11A 08-12 armor hit claims no damage", "TC-08-12", lambda m: first(m, "Gg2Phys", "hit", lambda e: e["targetId"] == "xg2Phys").update(hpAfter=1000))
    mutate("GPT-11A 08-12 magic knockback end x 0", "TC-08-12", lambda m: first(m, "Gg2Magic", "enemy.knockback", lambda e: e["actorId"] == "xg2Magic").update(endX=0))
    mutate("GPT-11A U-02 wall knockback displacement -4", "TC-UNITY-02", lambda m: first(m, "U2WallEnemy", "enemy.knockback").update(displacement=-4))
    mutate("GPT-11A U-02 rendered depth evidence removed", "TC-UNITY-02", lambda m: [m.remove(e) for e in [x for x in m if x["type"] == "view.planeCheck"]])
    def startup_hit(m):
        s = segments(m)["U7Multi"]
        hit = next(e for e in s if e["type"] == "hit"); a = next(e for e in s if e["type"] == "attack.start")
        hit["simulationTick"] = a["simulationTick"]; hit["motionProgress"] = 0
        m.remove(hit); m.insert(m.index(a) + 1, hit)
        for i, e in enumerate(m, m[0]["eventId"]):
            e["eventId"] = i
    mutate("GPT-11A U-07 a hit moved into startup", "TC-UNITY-07", startup_hit)
    mutate("GPT-11A U-07 cancel record on another tick", "TC-UNITY-07", lambda m: first(m, "U7Cancel", "magic.cancel").update(simulationTick=10080))
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    import os
    DATA_DIRS[:] = [os.path.dirname(os.path.abspath(args[0]))]  # R1: the run data beside the log
    ev = load(args[0])
    out = {"log": args[0], **check(ev)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(ev)
        out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
