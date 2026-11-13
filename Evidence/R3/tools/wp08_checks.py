"""WP-08 log checks: P0-06 TC-06-11..18, P0-07 TC-07-01..07 and TC-OPS-02; TC-07-04 parry-window runs.

Usage:
  python Tools/wp08_checks.py <main run>.ndjson [--selftest]
  python Tools/wp08_checks.py --pw <main w=0.15>.ndjson <w=0.10>.ndjson <w=0.20>.ndjson [--selftest]

Sections are cut by scenario markers ("mark=" in the script). Times: one tick = 0.02 s (fixed 50 Hz).
"""
import copy
import json
import re
import sys

DT = 0.02
SCENARIOS = ["ArmorPhysical", "ArmorMagic", "ArmorAgain", "RollBeforeWindow", "RollInWindow", "RollAfterWindow", "NormalHitThenNextAttack",
             "MultiEnemyLock", "DodgeNormal", "DodgeMagic", "DodgeArmor", "AttackTheDead", "OneDiesOthersContinue", "FrontGuardFacingRight",
             "RearAttackWhileGuardingRight", "TurnLeftAndGuard", "Guard9", "Guard10", "Guard50", "ConsecutiveGuard", "AirGuard", "AirParry",
             "ParryThenAttack", "GuardNormal", "GuardUnblockable", "GuardBreak", "RbGroundNormal", "RbLauncher", "RbAirNormal", "RbDash", "RbDive",
             "RbDuringHitstun",
             # R1 (GPT WP-08 review): weak magic on super armor (WP08-F01), lethal guard / guard break / parry damage (WP08-F02)
             "ArmorWeakMagic", "ArmorWeakAgain", "LethalGuard", "LethalGuardBreak", "LethalParry",
             # R2 (GPT WP-06~08 R1 review section 1): two lethal hits on one tick (guarded / unguarded)
             "DoubleLethalGuard", "DoubleLethalHit",
             "ParryOffset22", "ParryOffset18", "ParryOffset12", "ParryOffset08", "ScriptEnd"]
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


def tick(s, n):
    return next((e for e in s if e["type"] == "tick" and e["simulationTick"] == n), {})


def enemy_at(s, n, eid):
    for m in ENEMY.finditer(str(tick(s, n).get("enemies", ""))):
        if m.group(1) == eid: return m.group(2), float(m.group(3))
    return None, None


def enemy_hp(s, n, eid):
    for m in ENEMY.finditer(str(tick(s, n).get("enemies", ""))):
        if m.group(1) == eid: return float(m.group(5))
    return None


def effective(h):
    return h.get("hpAfter") is not None and h["hpAfter"] < h["hpBefore"] and h.get("comboAfter") == h.get("comboBefore", -99) + 1



# R3 (GPT R2 review §4 #2/#3/#5): knockback attribution, one rule for every checker.
# On a log from the R2-diagnostics build (any enemy.knockback carries attribution, 7a9ca55+) every field is required;
# a log from an earlier build is reported as legacy and checked without them.
ATTR_FIELDS = ("attribution", "velocitySets", "firstSource", "lastSource", "firstAttackInstanceId", "lastAttackInstanceId", "aiMoveDuring", "blocked")
DIAG = {"r2": False}


def set_diag(ev):
    DIAG["r2"] = any(e["type"] == "enemy.knockback" and "attribution" in e for e in ev)
    return "R2 (attribution required)" if DIAG["r2"] else "legacy (pre-7a9ca55, no attribution fields)"


def attr_ok(k, h):
    if not DIAG["r2"]:
        return True
    src = "Hit:" + str(h.get("attackId"))
    return all(f in k for f in ATTR_FIELDS) and k["attribution"] == "SingleSource" and k["velocitySets"] == 1 \
        and k["firstSource"] == k["lastSource"] == src and k["firstAttackInstanceId"] == k["lastAttackInstanceId"] == h.get("attackInstanceId") \
        and k["aiMoveDuring"] is False and k["blocked"] is False


def press_of(s, action, before_tick, after_tick=-1):
    return [e for e in s if e["type"] == "input" and e.get("action") == action and e.get("edge") == "Press" and after_tick <= e["simulationTick"] <= before_tick]


def knock_disp(s, eid, h, ticks=40):
    """Full-precision enemy.knockback record that starts right after hit h (R1)."""
    k = next((x for x in s if x["type"] == "enemy.knockback" and x.get("actorId") == eid and h["simulationTick"] < x.get("startTick", -1) <= h["simulationTick"] + 8
              and x["simulationTick"] <= h["simulationTick"] + ticks), None)
    if not k: return None
    # R2 (GPT WP08-M01-R1): the record agrees with itself, with the hit and with the 2-decimal per-tick positions of its end ticks;
    # records with attribution (R2 build) must be single-source and name this hit's attack instance.
    _, x0 = enemy_at(s, k["startTick"] - 1, eid)
    _, x1 = enemy_at(s, k["simulationTick"], eid)
    kv = h.get("knockbackVel") or 0
    ok = x0 is not None and x1 is not None and abs(k["displacement"] - (k["endX"] - k["startX"])) <= 1e-4 \
        and abs(k["startVel"] - kv) <= 1e-4 and kv != 0 and k["displacement"] * kv > 0 \
        and abs(k["startX"] - x0) <= 0.0051 and abs(k["endX"] - x1) <= 0.0051
    ok = ok and attr_ok(k, h)  # R3: every attribution field, consistent with the hit
    return abs(k["displacement"]) if ok else -1.0


def armor_magic(seg, first, again, eid):
    s = seg.get(first, [])
    mh = next((e for e in of(s, "hit") if e.get("damageType") == "Magic" and e["targetId"] == eid), None)
    cast = next((e for e in of(s, "magic.cast") if mh and e.get("attackInstanceId") == mh.get("attackInstanceId")), None)
    cancelled = mh is not None and not [e for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateAfter") == "AttackActive" and e["simulationTick"] < mh["simulationTick"] + 20]
    hit_by_that = [e for e in of(s, "hit.player") if e.get("actorId") == eid and mh and e["simulationTick"] < mh["simulationTick"] + 20]
    disp = knock_disp(s, eid, mh) if mh else None
    s2 = seg.get(again, [])
    h2 = next((e for e in of(s2, "hit") if e["targetId"] == eid), None)
    return {"magicHit": (mh["reason"], mh["stateBefore"], mh["stateAfter"], mh.get("knockbackVel")) if mh else None, "displacement": disp,
            "armorAttackCancelled": cancelled, "nextArmorAttackPhysicalHit": (h2["targetId"], h2["reason"], h2["stateBefore"], h2["stateAfter"]) if h2 else None,
            "pass": mh is not None and cast is not None and mh["reason"].startswith("MagicBreaksArmor") and mh["stateBefore"].startswith("Attack") and mh["stateAfter"] == "Stagger"
            and abs(mh.get("knockbackVel") or 0) > 0 and (disp or 0) > 0 and cancelled and not hit_by_that and mh["hpAfter"] < mh["hpBefore"]
            and h2 is not None and h2["reason"].startswith("SuperArmor:DamageOnly") and h2["stateBefore"].startswith("Attack") and effective(h2)}


def attack_start(s, eid):
    return next((e["simulationTick"] for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateAfter") == "AttackStartup"), None)


def check(ev):
    diag = set_diag(ev)
    seg = segsegs = segments(ev)
    out = {"knockbackDiagnostics": diag}

    # ---- TC-06-11: physical hit during a super-armor attack: HP and combo go up, no stagger / knockback, the attack goes on (R-06-11)
    s = seg.get("ArmorPhysical", [])
    h = next(iter(of(s, "hit")), None)
    act = [e for e in of(s, "enemy.state") if e.get("actorId") == "a1" and e.get("stateAfter") == "AttackActive"]
    landed = [e for e in of(s, "hit.player") if e.get("actorId") == "a1"]
    out["TC-06-11"] = {"hit": (h["reason"], h["stateBefore"], h["stateAfter"], h["hpBefore"] - h["hpAfter"], h.get("comboAfter"), h.get("knockbackVel")) if h else None,
                       "attackContinued": bool(act and h and act[0]["simulationTick"] > h["simulationTick"]), "armorAttackLanded": len(landed),
                       "pass": h is not None and h["targetId"] == "a1" and effective(h) and all(e.get("actorId") == "a1" for e in act + landed) and h["reason"].startswith("SuperArmor:DamageOnly") and h["hpAfter"] < h["hpBefore"] and h.get("comboAfter", 0) >= 1
                       and h.get("knockbackVel") == 0 and h["stateAfter"] == h["stateBefore"] and bool(act) and act[0]["simulationTick"] > h["simulationTick"] and bool(landed)}

    # ---- TC-06-12: magic during the armor attack: stagger + knockback + cancel; the next armor attack is armored again (R-06-12)
    base12 = armor_magic(seg, "ArmorMagic", "ArmorAgain", "a2")
    weak12 = armor_magic(seg, "ArmorWeakMagic", "ArmorWeakAgain", "aw")
    out["TC-06-12"] = {"scale1_a2": base12, "weakScale0.1_aw(WP08-F01)": weak12,
                       "pass": base12["pass"] and weak12["pass"] and weak12["magicHit"] is not None and abs(weak12["magicHit"][3]) < 0.6}

    # ---- TC-06-13: roll invulnerability only inside its window; no automatic invulnerability after a normal hit (R-06-13)
    rows = []
    for name, want in (("RollBeforeWindow", "hit"), ("RollInWindow", "skip"), ("RollAfterWindow", "hit")):
        s = seg.get(name, [])
        roll = next((e for e in of(s, "motor.state") if e.get("stateAfter") == "Roll"), None)
        res = next((e for e in s if e["type"] in ("hit.player", "hit.skip") and e.get("actorId") == "r1"), None)
        # roll time the simulation uses at the hit = (hit tick - roll start tick + 1) ticks; a skip also logs it as rollElapsed
        el = None
        if roll and res:
            el = round(res["rollElapsed"], 3) if res["type"] == "hit.skip" and "rollElapsed" in res else round((res["simulationTick"] - roll["simulationTick"] + 1) * DT, 3)
        ended = roll and res and [e for e in of(s, "motor.state") if e.get("stateBefore") == "Roll" and roll["simulationTick"] <= e["simulationTick"] < res["simulationTick"]]
        st_at_hit = "Roll" if roll and res and roll["simulationTick"] <= res["simulationTick"] and not ended else None
        got = None if res is None else ("skip" if res["type"] == "hit.skip" else "hit")
        side = {"RollBeforeWindow": lambda x: x < 0.05, "RollInWindow": lambda x: 0.05 <= x <= 0.30, "RollAfterWindow": lambda x: x > 0.30}[name]
        reason_ok = res is not None and (res["reason"] == "PlayerInvincible:RollWindow(R-06-13)" if want == "skip" else res["reason"].startswith("Hit"))
        tick_el = round((res["simulationTick"] - roll["simulationTick"] + 1) * DT, 3) if roll and res else None
        rows.append({"case": name, "rollElapsedAtHit": el, "playerState": st_at_hit, "result": got, "reason": res["reason"] if res else None,
                     "pass": got == want and st_at_hit == "Roll" and el is not None and side(el) and reason_ok
                     and (want != "skip" or abs(el - tick_el) <= 0.011)})
    s = seg.get("NormalHitThenNextAttack", [])
    hp = [e for e in of(s, "hit.player") if e.get("actorId") == "r1"]
    sk = [e for e in of(s, "hit.skip") if e.get("actorId") == "r1"]
    rows.append({"case": "NormalHitThenNextAttack", "hits": [(e["simulationTick"], e.get("extraInvulnerability")) for e in hp], "skips": len(sk),
                 "pass": len(hp) >= 2 and not sk and all(e.get("extraInvulnerability", "").startswith("none") for e in hp)})
    out["TC-06-13"] = {"cases": rows, "rollWindow": "0.05..0.30 s (rollInvulnStart/End)", "pass": all(r["pass"] for r in rows)}

    # ---- TC-06-14: several enemies in a row: report the lock, escape time and damage; no added protection (R-06-14, observation only)
    s = seg.get("MultiEnemyLock", [])
    hp = of(s, "hit.player")
    ticks = [e for e in s if e["type"] == "tick"]
    runs, cur = [], []
    for t in ticks:
        if t.get("stateAfter") == "HitStun": cur.append(t["simulationTick"])
        elif cur: runs.append(cur); cur = []
    if cur: runs.append(cur)
    longest = max([len(r) for r in runs] or [0])
    walk = next((t["simulationTick"] for t in ticks if t.get("stateAfter") == "Walk"), None)
    dmg = sum(e["hpBefore"] - e["hpAfter"] for e in hp)
    out["TC-06-14"] = {"hits": len(hp), "hitTicks": sorted({e["simulationTick"] for e in hp}), "lockRuns(ticks)": [len(r) for r in runs],
                       "longestLockSeconds": round(longest * DT, 2), "firstWalkTick": walk, "damageTotal": dmg,
                       "addedProtection": [e["reason"] for e in of(s, "hit.skip")],
                       "note": "observation only; the pass criterion for feel is undecided (R-06-14)",
                       "attackers": sorted({e.get("actorId") for e in hp}),
                       "pass": len(hp) >= 3 and longest > 0 and walk is not None and not of(s, "hit.skip") and {e.get("actorId") for e in hp} == {"x1", "x2", "x3"}
                       and all(e.get("extraInvulnerability", "").startswith("none") for e in hp)}

    # ---- TC-06-15 / 16: same-tick hits -> Dodge both sides, no HP change, no count, chain ends, both reactions (R-06-15/16)
    def chain_alive_before(eid):
        last = None
        for e in ev:
            if e["eventId"] >= eid: break
            if e["type"] in ("combo.begin", "combo.end"): last = e["type"]
        return last == "combo.begin"

    def dodge(name, eid):
        s = seg.get(name, [])
        d = next(iter(of(s, "dodge")), None)
        if d is None: return {"case": name, "pass": False}
        n = d["simulationTick"]
        same = [e for e in s if e["type"] in ("hit", "hit.player") and e["simulationTick"] == n]
        en_state = [enemy_at(s, n, eid)[0]]
        ce = [e for e in of(s, "combo.end") if e["simulationTick"] == n]
        before, after = tick(s, n - 1), tick(s, n)
        alive = chain_alive_before(d["eventId"])
        return {"case": name, "tick": n, "damage": d.get("damage"), "playerHp": (before.get("hp"), after.get("hp")), "comboBefore": d.get("comboBefore"),
                "playerState": after.get("stateAfter"), "enemyState": en_state, "chainAliveBefore": alive, "chainEnd": [e["reason"] for e in ce],
                "enemyArmor": d.get("enemyArmor"), "hitsSameTick": len(same),
                "enemyHp": (enemy_hp(s, n - 1, eid), enemy_hp(s, n, eid)), "attackIdsLinked": linked(s, d, eid),
                "pass": d.get("damage") == 0 and before.get("hp") == after.get("hp") and not same and after.get("stateAfter") == "DodgeReact"
                and en_state[0] == "DodgeReact" and ((bool(ce) and ce[0]["reason"] == "Dodge") if alive else not ce)
                and enemy_hp(s, n - 1, eid) is not None and enemy_hp(s, n - 1, eid) == enemy_hp(s, n, eid) and linked(s, d, eid)}

    def linked(s, d, eid):
        pa = d.get("playerAttackInstanceId")
        ea = d.get("enemyAttackInstanceId")
        p_ok = any(e.get("attackInstanceId") == pa and e["eventId"] < d["eventId"] for e in s if e["type"] in ("attack.start", "magic.cast"))
        e_ok = any(e.get("actorId") == eid and e.get("stateAfter") == "AttackStartup" and e.get("attackInstanceId") == ea for e in of(s, "enemy.state"))
        return p_ok and e_ok
    d1 = dodge("DodgeNormal", "dd1")
    out["TC-06-15"] = {"case": d1, "pass": d1["pass"]}
    dm, da = dodge("DodgeMagic", "dm1"), dodge("DodgeArmor", "da1")
    out["TC-06-16"] = {"magicVsNormal": dm, "normalVsSuperArmor": da, "pass": dm["pass"] and da["pass"] and da.get("enemyArmor") is True}

    # ---- TC-06-17: no more hits or combo on the dead target; motion -> vanish -> removed (R-06-17)
    s = seg.get("AttackTheDead", [])
    death = next((e for e in of(s, "hit") if e["reason"].startswith("Death")), None)
    later = [e for e in of(s, "hit") if death and e["targetId"] == "w1" and e["eventId"] > death["eventId"]]
    van = next((e for e in of(s, "enemy.vanish") if e.get("actorId") == "w1"), None)
    rem = next((e for e in of(s, "enemy.removed") if e.get("actorId") == "w1"), None)
    starts_after = [e for e in of(s, "attack.start") if death and e["eventId"] > death["eventId"]]
    out["TC-06-17"] = {"deathTick": death["simulationTick"] if death else None, "attacksAfterDeath": len(starts_after), "hitsAfterDeath": len(later),
                       "vanishTick": van["simulationTick"] if van else None, "removedTick": rem["simulationTick"] if rem else None,
                       "pass": death is not None and len(starts_after) >= 3 and not later and van is not None and rem is not None
                       and death["simulationTick"] < van["simulationTick"] < rem["simulationTick"]
                       and any(e["simulationTick"] < van["simulationTick"] for e in starts_after) and any(van["simulationTick"] <= e["simulationTick"] < rem["simulationTick"] for e in starts_after)}

    # ---- TC-06-18: only A's link ends; the chain keeps counting B and C (R-06-18, R-04-10)
    s = seg.get("OneDiesOthersContinue", [])
    death = next((e for e in of(s, "hit") if e["targetId"] == "c1" and e["reason"].startswith("Death")), None)
    link = of(s, "combo.linkEnd")
    after = [e for e in of(s, "hit") if death and e["eventId"] > death["eventId"]]
    ends = [e for e in of(s, "combo.end") if death and e["eventId"] > death["eventId"]]
    chain_ids = {e.get("chainId") for e in of(s, "hit")}
    out["TC-06-18"] = {"linkEnd": [(e["targetId"], e.get("chainAlive")) for e in link], "hitsAfterDeath": [(e["targetId"], e.get("comboAfter")) for e in after],
                       "chainEnd": [(e["reason"], e.get("comboHits")) for e in ends], "chains": sorted(chain_ids),
                       "pass": death is not None and len(link) == 1 and link[0]["targetId"] == "c1" and link[0].get("chainAlive") is True
                       and len(after) >= 4 and {e["targetId"] for e in after} == {"c2", "c3"} and len(chain_ids) == 1
                       and [e.get("comboAfter") for e in after] == sorted(e.get("comboAfter") for e in after) and all(effective(e) for e in after)
                       and all(b.get("comboAfter") == a.get("comboAfter") + 1 for a, b in zip(after, after[1:]))
                       and (not ends or ends[0]["reason"] == "Timeout" and ends[0]["comboHits"] == max(e.get("comboAfter") for e in after))}

    # ---- TC-07-01: front only; the rear attacker hits; after turning, the same attacker is guarded (R-07-01)
    a = next(iter(of(seg.get("FrontGuardFacingRight", []), "guard.block")), None)
    b = next(iter(of(seg.get("RearAttackWhileGuardingRight", []), "hit.player")), None)
    c = next(iter(of(seg.get("TurnLeftAndGuard", []), "guard.block")), None)
    fc = lambda s, e: tick(s, e["simulationTick"] - 1).get("facing") if e else None
    sb = seg.get("RearAttackWhileGuardingRight", [])
    behind = b is not None and enemy_at(sb, b["simulationTick"] - 1, b.get("actorId"))[1] is not None \
        and (enemy_at(sb, b["simulationTick"] - 1, b.get("actorId"))[1] - tick(sb, b["simulationTick"] - 1).get("x", 0)) * tick(sb, b["simulationTick"] - 1).get("facing", 0) < 0
    out["TC-07-01"] = {"frontGuard": (a.get("targetId"), a["reason"], fc(seg.get("FrontGuardFacingRight", []), a)) if a else None,
                       "rearHit": (b.get("actorId"), b["stateBefore"], b["reason"], b.get("front"), b["hpBefore"] - b["hpAfter"]) if b else None,
                       "afterTurn": (c.get("targetId"), c["reason"], fc(seg.get("TurnLeftAndGuard", []), c)) if c else None,
                       "pass": a is not None and a["reason"].startswith("FrontGuard") and fc(seg["FrontGuardFacingRight"], a) == 1
                       and b is not None and b["stateBefore"] == "Guard" and b["reason"].startswith("HitFromBehind") and b.get("front") is False and b["hpAfter"] < b["hpBefore"] and behind
                       and c is not None and c.get("targetId") == b.get("actorId") and c["reason"].startswith("FrontGuard") and fc(seg["TurnLeftAndGuard"], c) == -1}

    # ---- TC-07-02 / TC-OPS-02: D = 9 / 10 / 50 -> 0 / 1 / 5 (test D supplier, policy logged)
    rows = []
    for name, d, g in (("Guard9", 9, 0), ("Guard10", 10, 1), ("Guard50", 50, 5)):
        e = next(iter(of(seg.get(name, []), "guard.block")), None)
        rows.append({"case": name, "damageD": e.get("damageD") if e else None, "damageTaken": e.get("damageTaken") if e else None,
                     "hp": (e["hpBefore"], e["hpAfter"]) if e else None, "damagePolicy": e.get("damagePolicy") if e else None, "guardRule": e.get("guardRule") if e else None,
                     "pass": e is not None and e.get("damageD") == d and e.get("damageTaken") == g and e["hpBefore"] - e["hpAfter"] == g
                     and str(e.get("damagePolicy", "")).startswith("TEST-DMG-01") and str(e.get("guardRule", "")).startswith("D*0.1")})
    lethal = []
    for name, cause, typ in (("LethalGuard", "DefendedHit:Guard", "guard.block"), ("LethalGuardBreak", "DefendedHit:GuardBreak", "guard.block"), ("LethalParry", "DefendedHit:Parry", "guard.parry")):
        sl = seg.get(name, [])
        g = next(iter(of(sl, typ)), None)
        dth = of(sl, "player.death")
        flow = [e for e in of(sl, "death.flow") if e.get("stateAfter") == "Motion"]
        rev = next(iter(of(sl, "death.revive")), None)
        starts = [e for e in of(sl, "attack.start") if dth and e["eventId"] > dth[0]["eventId"] and (rev is None or e["eventId"] < rev["eventId"])]
        presses = [e for e in sl if e["type"] == "input" and e.get("action") == "Attack" and e.get("edge") == "Press" and dth and e["eventId"] > dth[0]["eventId"] and (rev is None or e["eventId"] < rev["eventId"])]
        lethal.append({"case": name, "defense": (g["reason"], g.get("hpBefore"), g.get("hpAfter"), g.get("damageTaken")) if g else None,
                       "deaths": [(e["simulationTick"], e.get("cause")) for e in dth], "attackPressesAfterDeath": len(presses), "attackStartsAfterDeath": len(starts),
                       "pass": g is not None and g.get("hpBefore") == 1 and g.get("hpAfter") == 0 and g.get("damageTaken") == 1 and len(dth) == 1
                       and dth[0]["simulationTick"] == g["simulationTick"] and dth[0].get("cause") == cause and dth[0]["eventId"] > g["eventId"]
                       and len(presses) >= 1 and not starts and tick(sl, g["simulationTick"]).get("stateAfter") == "Dead"
                       # R2 (GPT WP08-M01-R1): the death names the defended attacker, and the death UI begins (Motion) on that tick
                       and dth[0].get("targetId") == g.get("targetId") and len(flow) == 1 and flow[0]["simulationTick"] == dth[0]["simulationTick"]
                       and flow[0]["eventId"] > dth[0]["eventId"]})
    out["WP08-F02"] = {"cases": lethal, "pass": len(lethal) == 3 and all(r["pass"] for r in lethal)}

    # ---- R2 WP08-F02-R1: two enemies hit on the same tick and the first hit kills; the later candidate is only logged as ignored
    dbl = []
    for name, typ, cause in (("DoubleLethalGuard", "guard.block", "DefendedHit:Guard"), ("DoubleLethalHit", "hit.player", None)):
        sl = seg.get(name, [])
        dth = of(sl, "player.death")
        rev = next(iter(of(sl, "death.revive")), None)
        live = [e for e in sl if rev is None or e["eventId"] < rev["eventId"]]
        hits = [e for e in live if e["type"] in ("guard.block", "guard.parry", "hit.player")]
        ign = of(live, "hit.player.ignored")
        flow = [e for e in of(live, "death.flow") if e.get("stateAfter") == "Motion"]
        d = dth[0] if len(dth) == 1 else None
        first_hit = hits[0] if len(hits) == 1 else None
        attacker = (first_hit.get("targetId") if typ.startswith("guard") else first_hit.get("actorId")) if first_hit else None
        starts = [e for e in of(live, "attack.start") if d and e["eventId"] > d["eventId"]]
        # R3 (GPT R2 review §4 #4): the ignored candidate is a real second attacker whose own attack instance is active on that tick
        ig = ign[0] if len(ign) == 1 else None
        ist = [e for e in sl if ig and e["type"] == "enemy.state" and e.get("actorId") == ig.get("actorId") and e.get("attackInstanceId") == ig.get("enemyAttackInstanceId")
               and e["simulationTick"] <= ig["simulationTick"]]
        ig_active = bool(ist) and ist[-1].get("stateAfter") == "AttackActive"
        ig_ok = ig is not None and ig_active and ig.get("stateBefore") == ig.get("stateAfter") == "Dead" and ig.get("hp") == 0
        pr = [e for e in live if e["type"] == "input" and e.get("action") == "Attack" and e.get("edge") == "Press" and d and e["eventId"] > d["eventId"]]
        dbl.append({"case": name, "deaths": [(e["simulationTick"], e.get("targetId"), e.get("cause")) for e in dth],
                    "hits": [(e["type"], e["simulationTick"], e.get("actorId"), e.get("targetId")) for e in hits],
                    "ignored": [(e["simulationTick"], e.get("actorId"), e.get("enemyAttackInstanceId"), e.get("stateBefore"), e.get("stateAfter"), e.get("hp")) for e in ign],
                    "ignoredAttackActive": ig_active, "deathMotion": len(flow),
                    "attackPressesAfterDeath": len(pr), "attackStartsAfterDeath": len(starts),
                    "pass": d is not None and first_hit is not None and first_hit["type"] == typ and first_hit["simulationTick"] == d["simulationTick"]
                    and d.get("targetId") == attacker and d.get("cause") == cause and len(ign) == 1 and ign[0]["simulationTick"] == d["simulationTick"]
                    and ign[0]["eventId"] > d["eventId"] and ign[0].get("actorId") not in (None, attacker) and ign[0].get("stateBefore") == "Dead"
                    and len(flow) == 1 and flow[0]["simulationTick"] == d["simulationTick"] and len(pr) >= 1 and not starts and ig_ok})
    out["WP08-F02-R1"] = {"cases": dbl, "pass": len(dbl) == 2 and all(r["pass"] for r in dbl)}
    out["TC-07-02"] = {"cases": rows, "pass": all(r["pass"] for r in rows)}
    out["TC-OPS-02"] = {"same": "TC-07-02 rows; D comes from the test supplier TEST-DMG-01 and is logged with each guard", "pass": out["TC-07-02"]["pass"]}

    # ---- TC-07-03: consecutive ground guard; guard and parry in the air (R-07-03)
    s = seg.get("ConsecutiveGuard", [])
    cg = of(s, "guard.block")
    broke = [e for e in of(s, "motor.state") if e.get("stateBefore") == "Guard" and cg and cg[0]["simulationTick"] <= e["simulationTick"] <= cg[-1]["simulationTick"]]
    ag = next(iter(of(seg.get("AirGuard", []), "guard.block")), None)
    ap = next(iter(of(seg.get("AirParry", []), "guard.parry")), None)
    air = lambda s, e: (tick(s, e["simulationTick"] - 1).get("grounded") is False and tick(s, e["simulationTick"]).get("grounded") is False) if e else False
    sap = seg.get("AirParry", [])
    rbp = press_of(sap, "Guard", ap["simulationTick"]) if ap else []
    rb_ok = bool(rbp) and abs((ap["gameTime"] - rbp[-1]["gameTime"]) - ap.get("rbToHit", -1)) <= 0.011 if ap else False
    out["TC-07-03"] = {"consecutiveGround": [(e["simulationTick"], e.get("damageTaken")) for e in cg], "guardLeftBetween": len(broke),
                       "airGuard": (ag["simulationTick"], tick(seg["AirGuard"], ag["simulationTick"] - 1).get("y")) if ag else None,
                       "airParry": (ap["simulationTick"], tick(seg["AirParry"], ap["simulationTick"] - 1).get("y"), ap.get("rbToHit")) if ap else None,
                       "pass": len(cg) >= 2 and not broke and air(seg.get("AirGuard", []), ag) and air(seg.get("AirParry", []), ap) and rb_ok}

    # ---- TC-07-04 (main run part): with window 0.15, RB 0.22 / 0.18 s before the hit = guard, 0.12 / 0.08 = parry
    out["TC-07-04-main"] = parry_offsets(seg, 0.15)

    # ---- TC-07-05: parry stops the attack, knocks back, stuns; the player attacks at once (R-07-05)
    s = seg.get("ParryThenAttack", [])
    p = next(iter(of(s, "guard.parry")), None)
    st = next((e for e in of(s, "attack.start") if p and e["eventId"] > p["eventId"]), None)
    hit = next((e for e in of(s, "hit") if st and e.get("attackInstanceId") == st.get("attackInstanceId")), None)
    es = [e for e in of(s, "enemy.state") if p and e.get("actorId") == "pp" and e["simulationTick"] == p["simulationTick"]]
    x0, x1 = (enemy_at(s, p["simulationTick"] - 1, "pp")[1], enemy_at(s, p["simulationTick"] + 10, "pp")[1]) if p else (None, None)
    stopped = p is not None and not [e for e in of(s, "hit.player") if e.get("actorId") == "pp" and p["simulationTick"] <= e["simulationTick"] <= p["simulationTick"] + 5]
    out["TC-07-05"] = {"parryTick": p["simulationTick"] if p else None, "rbToHit": p.get("rbToHit") if p else None, "enemyStun": p.get("enemyStun") if p else None,
                       "enemyStateAtParry": [e["stateAfter"] for e in es], "enemyX": (x0, x1), "playerAttackStartAfter(ticks)": (st["simulationTick"] - p["simulationTick"]) if st and p else None,
                       "hit": (hit["targetId"], hit["stateBefore"], hit["reason"]) if hit else None,
                       "pass": p is not None and stopped and x0 is not None and x1 is not None and x1 > x0 and st is not None and st["simulationTick"] - p["simulationTick"] <= 3
                       and hit is not None and hit["stateBefore"] == "Stagger" and (hit["simulationTick"] - p["simulationTick"]) * DT < p.get("enemyStun", 0)
                       and hit["targetId"] == p.get("targetId") == "pp" and effective(hit)}

    # ---- TC-07-06: normal = guarded, unblockable = hit through the guard, guard break = guard released (R-07-06)
    n = next(iter(of(seg.get("GuardNormal", []), "guard.block")), None)
    u = next(iter(of(seg.get("GuardUnblockable", []), "hit.player")), None)
    gb = next(iter(of(seg.get("GuardBreak", []), "guard.block")), None)
    rel = next((e for e in of(seg.get("GuardBreak", []), "motor.state") if gb and e["simulationTick"] == gb["simulationTick"] and e.get("stateBefore") == "Guard"), None)
    out["TC-07-06"] = {"normal": (n["reason"], n.get("stateAfter"), n.get("damageTaken")) if n else None,
                       "unblockable": (u["stateBefore"], u["reason"], u["hpBefore"] - u["hpAfter"]) if u else None,
                       "guardBreak": (gb["reason"], gb.get("stateAfter"), gb.get("damageTaken"), rel.get("stateAfter") if rel else None) if gb else None,
                       "pass": n is not None and n["reason"].startswith("FrontGuard") and n.get("stateAfter") == "Guard"
                       and u is not None and u["stateBefore"] == "Guard" and u["reason"].startswith("Unblockable") and u["hpAfter"] < u["hpBefore"]
                       and gb is not None and gb["reason"].startswith("GuardBreak") and rel is not None and rel.get("stateAfter") != "Guard"
                       and gb.get("damageD") == 10 and gb.get("damageTaken") == 1 and gb["hpBefore"] - gb["hpAfter"] == 1 and n.get("damageTaken") == 1}

    # ---- TC-07-07: RB cancels ground normal / launcher / air normal; refused in dash, dive, hitstun; allowed after hitstun (R-07-07)
    rows = []
    for name in ("RbGroundNormal", "RbLauncher", "RbAirNormal"):
        sc = seg.get(name, [])
        e = next((x for x in of(sc, "attack.end") if x["reason"] == "GuardCancel"), None)
        a0 = next((x for x in of(sc, "attack.start") if e and x.get("attackInstanceId") == e.get("attackInstanceId")), None)
        rbp = press_of(sc, "Guard", e["simulationTick"], a0["simulationTick"]) if e and a0 else []
        rows.append({"case": name, "result": "GuardCancel" if e else None, "rbPressTick": rbp[0]["simulationTick"] if rbp else None,
                     "pass": e is not None and e.get("hitboxRemoved") is True and a0 is not None and len(rbp) == 1})
    for name, st in (("RbDash", "AirDash"), ("RbDive", "Dive")):
        e = [x for x in of(seg.get(name, []), "input.reject") if x.get("action") == "Guard" and x["reason"] == "ActionLock:" + st]
        gc = [x for x in of(seg.get(name, []), "attack.end") if x["reason"] == "GuardCancel"]
        rows.append({"case": name, "result": e[0]["reason"] if e else None, "pass": len(e) == 1 and not gc})
    s = seg.get("RbDuringHitstun", [])
    rj = [x for x in of(s, "input.reject") if x.get("action") == "Guard" and x["reason"] == "ActionLock:HitStun"]
    ok_after = [x for x in of(s, "motor.state") if x.get("stateAfter") == "Guard" and rj and x["simulationTick"] > rj[0]["simulationTick"]]
    rows.append({"case": "RbDuringHitstun", "result": (rj[0]["reason"] if rj else None, "guard after hitstun" if ok_after else None), "pass": bool(rj) and bool(ok_after)})
    out["TC-07-07"] = {"cases": rows, "pass": all(r["pass"] for r in rows)}
    return out


def parry_offsets(seg, window):
    rows = []
    for name, x in (("ParryOffset22", 0.22), ("ParryOffset18", 0.18), ("ParryOffset12", 0.12), ("ParryOffset08", 0.08)):
        s = seg.get(name, [])
        e = next((y for y in s if y["type"] in ("guard.block", "guard.parry")), None)
        got = None if e is None else ("parry" if e["type"] == "guard.parry" else "guard")
        want = "parry" if x <= window + 1e-6 else "guard"
        rbp = press_of(s, "Guard", e["simulationTick"]) if e else []
        rows.append({"case": name, "rbToHit": round(e["rbToHit"], 3) if e else None, "windowLogged": e.get("parryWindow") if e and got == "parry" else None,
                     "result": got, "expected": want,
                     "pass": e is not None and got == want and abs(e["rbToHit"] - x) <= 0.011 and bool(rbp)
                     and abs((e["gameTime"] - rbp[-1]["gameTime"]) - e["rbToHit"]) <= 0.011
                     and (got != "parry" or abs((e.get("parryWindow") or -1) - window) < 1e-6)})
    return {"window": window, "cases": rows, "pass": all(r["pass"] for r in rows)}


def pw_check(runs):
    res = []
    for ev in runs:
        seg = segments(ev)
        w = None
        for name in ("ParryOffset22", "ParryOffset18", "ParryOffset12", "ParryOffset08"):
            p = next((e for e in seg.get(name, []) if e["type"] == "guard.parry"), None)
            if p: w = p.get("parryWindow"); break
        res.append(parry_offsets(seg, round(w, 3) if w is not None else -1))
    ws = sorted(r["window"] for r in res)
    # every parry record in a run carries the same window (GPT WP-08 review M01: one record changed to 0.01 passed)
    for ev, r in zip(runs, res):
        wins = {round(e.get("parryWindow"), 3) for e in ev if e["type"] == "guard.parry"}
        r["parryWindowsInRun"] = sorted(wins)
        if len(wins) != 1: r["pass"] = False
    return {"TC-07-04": {"runs": res, "windows": ws, "pass": ws == [0.1, 0.15, 0.2] and all(r["pass"] for r in res)}}


def selftest(ev):
    base = check(ev)
    res = [{"case": "original", "pass": {k: v["pass"] for k, v in base.items() if isinstance(v, dict)}}]

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

    mutate("TC-06-11 armor hit staggers", "TC-06-11", lambda m: first(m, "ArmorPhysical", "hit").__setitem__("stateAfter", "Stagger"))
    mutate("TC-06-12 magic does not cancel the armor attack", "TC-06-12", lambda m: first(m, "ArmorMagic", "hit").__setitem__("reason", "SuperArmor:DamageOnly(R-06-11)"))
    mutate("TC-06-13 hit inside the roll window", "TC-06-13", lambda m: first(m, "RollInWindow", "hit.skip").__setitem__("type", "hit.player"))
    mutate("TC-06-13 skip after the roll window", "TC-06-13", lambda m: first(m, "RollAfterWindow", "hit.player").__setitem__("type", "hit.skip"))
    mutate("TC-06-14 a protection skip appears", "TC-06-14", lambda m: first(m, "MultiEnemyLock", "hit.player").__setitem__("type", "hit.skip"))

    def dodge_damage(m):
        d = first(m, "DodgeNormal", "dodge")
        t = first(m, "DodgeNormal", "tick", lambda e: e["simulationTick"] == d["simulationTick"])
        t["hp"] = t["hp"] - 10
    mutate("TC-06-15 HP changes on a Dodge", "TC-06-15", dodge_damage)
    mutate("TC-06-16 armor Dodge without armor", "TC-06-16", lambda m: first(m, "DodgeArmor", "dodge").__setitem__("enemyArmor", False))

    def hit_dead(m):
        s = segments(m)["AttackTheDead"]
        d = next(e for e in s if e["type"] == "hit" and e["reason"].startswith("Death"))
        h = copy.deepcopy(d)
        h["eventId"] += 0.5
        h["reason"] = "Stagger(R-06-04)"
        m.insert(m.index(d) + 1, h)
    mutate("TC-06-17 dead target hit again", "TC-06-17", hit_dead)
    mutate("TC-06-18 A's death ends the whole chain", "TC-06-18", lambda m: first(m, "OneDiesOthersContinue", "combo.linkEnd").__setitem__("chainAlive", False))
    mutate("TC-07-01 rear attack guarded", "TC-07-01", lambda m: first(m, "RearAttackWhileGuardingRight", "hit.player").__setitem__("reason", "Hit"))
    mutate("TC-07-02 D=9 takes 1", "TC-07-02", lambda m: first(m, "Guard9", "guard.block").__setitem__("damageTaken", 1))

    def ground_air(m):
        g = first(m, "AirParry", "guard.parry")
        t = first(m, "AirParry", "tick", lambda e: e["simulationTick"] == g["simulationTick"] - 1)
        t["grounded"] = True
    mutate("TC-07-03 'air' parry on the ground", "TC-07-03", ground_air)
    mutate("TC-07-04 RB 0.18 s before = parry at window 0.15", "TC-07-04-main", lambda m: first(m, "ParryOffset18", "guard.block").__setitem__("type", "guard.parry"))

    def late_attack(m):
        p = first(m, "ParryThenAttack", "guard.parry")
        a = first(m, "ParryThenAttack", "attack.start", lambda e: e["eventId"] > p["eventId"])
        a["simulationTick"] += 10
    mutate("TC-07-05 attack only well after the parry", "TC-07-05", late_attack)
    mutate("TC-07-06 unblockable guarded", "TC-07-06", lambda m: first(m, "GuardUnblockable", "hit.player").__setitem__("reason", "Hit"))
    mutate("TC-07-07 RB cancels the dash", "TC-07-07", lambda m: first(m, "RbDash", "input.reject", lambda e: e.get("action") == "Guard").__setitem__("type", "input"))

    # GPT WP-08 review M01: the 17 changes it found passing (one of them also in the 3-run check, see --pw)
    mutate("M01 06-11 first physical hit on another target", "TC-06-11", lambda m: first(m, "ArmorPhysical", "hit").__setitem__("targetId", "zz"))
    mutate("M01 06-12 armor-restored check on another enemy", "TC-06-12", lambda m: first(m, "ArmorAgain", "hit").__setitem__("targetId", "zz"))

    def roll_before_is_after(m):
        s = segments(m)["RollBeforeWindow"]
        r = first(m, "RollBeforeWindow", "motor.state", lambda e: e.get("stateAfter") == "Roll")
        h = next(e for e in s if e["type"] == "hit.player" and e.get("actorId") == "r1")
        r["simulationTick"] = h["simulationTick"] - 17
    mutate("M01 06-13 'before' roll moved so the hit is after the window", "TC-06-13", roll_before_is_after)
    mutate("M01 06-13 skip reason is revive invulnerability", "TC-06-13", lambda m: first(m, "RollInWindow", "hit.skip").__setitem__("reason", "PlayerInvincible:Revive(EXP-DEATH-03)"))

    def one_attacker(m):
        for e in segments(m)["MultiEnemyLock"]:
            if e["type"] == "hit.player": e["actorId"] = "x1"
    mutate("M01 06-14 all hits by one attacker", "TC-06-14", one_attacker)

    def dodge_enemy_hp(m):
        d = first(m, "DodgeNormal", "dodge")
        t = first(m, "DodgeNormal", "tick", lambda e: e["simulationTick"] == d["simulationTick"])
        t["enemies"] = re.sub(r"(dd1:[A-Za-z]+@-?[\d.]+,-?[\d.]+ hp)(-?[\d.]+)", lambda q: q.group(1) + "%d" % (float(q.group(2)) - 10), t["enemies"])
    mutate("M01 06-15 enemy HP drops on the Dodge tick", "TC-06-15", dodge_enemy_hp)
    mutate("M01 06-16 magic Dodge with an unknown player attack id", "TC-06-16", lambda m: first(m, "DodgeMagic", "dodge").__setitem__("playerAttackInstanceId", 99999))

    def starts_after_removal(m):
        s = segments(m)["AttackTheDead"]
        rem = next(e for e in s if e["type"] == "enemy.removed")
        van = next(e for e in s if e["type"] == "enemy.vanish")
        for e in s:
            if e["type"] == "attack.start" and van["simulationTick"] <= e["simulationTick"] < rem["simulationTick"]: e["simulationTick"] = rem["simulationTick"] + 3
    mutate("M01 06-17 attacks during the vanish moved after removal", "TC-06-17", starts_after_removal)

    def combo_fixed(m):
        s = segments(m)["OneDiesOthersContinue"]
        d = next(e for e in s if e["type"] == "hit" and e["targetId"] == "c1" and e["reason"].startswith("Death"))
        for e in s:
            if e["type"] == "hit" and e["eventId"] > d["eventId"]: e["comboAfter"] = 4
    mutate("M01 06-18 combo of the later hits fixed at 4", "TC-06-18", combo_fixed)

    def rear_flipped(m):
        b = first(m, "RearAttackWhileGuardingRight", "hit.player")
        t = first(m, "RearAttackWhileGuardingRight", "tick", lambda e: e["simulationTick"] == b["simulationTick"] - 1)
        t["facing"] = -1
    mutate("M01 07-01 rear-hit facing reversed", "TC-07-01", rear_flipped)
    mutate("M01 OPS-02 guard rule field removed", "TC-OPS-02", lambda m: first(m, "Guard10", "guard.block").pop("guardRule"))

    def air_parry_ground(m):
        g = first(m, "AirParry", "guard.parry")
        first(m, "AirParry", "tick", lambda e: e["simulationTick"] == g["simulationTick"]).__setitem__("grounded", True)
    mutate("M01 07-03 air parry tick on the ground", "TC-07-03", air_parry_ground)

    def rb_after_parry(m):
        g = first(m, "AirParry", "guard.parry")
        for e in segments(m)["AirParry"]:
            if e["type"] == "input" and e.get("action") == "Guard" and e.get("edge") == "Press" and e["simulationTick"] <= g["simulationTick"]:
                e["simulationTick"] = g["simulationTick"] + 2
                e["gameTime"] = g["gameTime"] + 0.04
    mutate("M01 07-03 RB press moved after the parry", "TC-07-03", rb_after_parry)
    mutate("M01 07-04 one parry record with window 0.01", "TC-07-04-main", lambda m: first(m, "ParryOffset08", "guard.parry").__setitem__("parryWindow", 0.01))

    def counter_other(m):
        p = first(m, "ParryThenAttack", "guard.parry")
        h = first(m, "ParryThenAttack", "hit", lambda e: e["eventId"] > p["eventId"])
        h["targetId"] = "zz"
    mutate("M01 07-05 counter hit on another enemy", "TC-07-05", counter_other)

    def break_dmg(m):
        g = first(m, "GuardBreak", "guard.block")
        g["damageTaken"] = 10
        g["hpAfter"] = g["hpBefore"] - 10
    mutate("M01 07-06 guard-break damage 10", "TC-07-06", break_dmg)

    def no_rb_launcher(m):
        drop = {id(e) for e in segments(m)["RbLauncher"] if e["type"] == "input" and e.get("action") == "Guard" and e.get("edge") == "Press"}
        m[:] = [e for e in m if id(e) not in drop]
    mutate("M01 07-07 launcher cancel without its RB press", "TC-07-07", no_rb_launcher)

    # R1
    def weak_no_move(m):
        for e in segments(m)["ArmorWeakMagic"]:
            if e["type"] == "enemy.knockback": e["displacement"] = 0.0
    mutate("R1 06-12 weak magic on armor does not move", "TC-06-12", weak_no_move)
    mutate("R1 F02 guard death missing", "WP08-F02", lambda m: m.remove(first(m, "LethalGuard", "player.death")))

    def attack_after_death(m):
        d = first(m, "LethalParry", "player.death")
        a = copy.deepcopy(next(e for e in m if e["type"] == "attack.start"))
        a["eventId"] = d["eventId"] + 0.5
        a["simulationTick"] = d["simulationTick"] + 2
        m.insert(m.index(d) + 1, a)
    mutate("R1 F02 attack starts after a parry death", "WP08-F02", attack_after_death)

    # GPT WP-06~08 R1 review section 3 (WP08-M01-R1)
    def same_ends(m):
        k = first(m, "ArmorWeakMagic", "enemy.knockback"); k["endX"] = k["startX"]
    mutate("GPT R1 weak armor magic record starts and ends at one x", "TC-06-12", same_ends)
    def frozen(m):
        for e in m:
            if e["type"] == "tick": e["enemies"] = re.sub(r"(aw:\w+@)[-\d.]+", lambda x: x.group(1) + "0.00", e.get("enemies", ""))
    mutate("GPT R1 armor target never moves on any tick", "TC-06-12", frozen)
    mutate("GPT R1 lethal death names another attacker", "WP08-F02", lambda m: first(m, "LethalGuard", "player.death").__setitem__("targetId", "other"))
    mutate("GPT R1 death UI never begins", "WP08-F02",
           lambda m: m.remove(next(e for e in segments(m)["LethalGuard"] if e["type"] == "death.flow" and e.get("stateAfter") == "Motion")))

    # R2 double lethal hit (WP08-F02-R1)
    def second_death(m):
        i = first(m, "DoubleLethalGuard", "hit.player.ignored")
        i.update({"type": "player.death", "reason": "HP0->DeathFlow(R-06-19)", "targetId": i["actorId"]})
    mutate("R2 F02-R1 second attacker publishes a second death", "WP08-F02-R1", second_death)
    mutate("R2 F02-R1 ignored candidate removed", "WP08-F02-R1", lambda m: m.remove(first(m, "DoubleLethalHit", "hit.player.ignored")))
    mutate("R2 F02-R1 second candidate hits the dead player", "WP08-F02-R1",
           lambda m: first(m, "DoubleLethalHit", "hit.player.ignored").update({"type": "hit.player", "reason": "Hit"}))
    mutate("R2 F02-R1 ignored candidate is the killer itself", "WP08-F02-R1",
           lambda m: first(m, "DoubleLethalGuard", "hit.player.ignored").__setitem__("actorId", first(m, "DoubleLethalGuard", "player.death")["targetId"]))
    mutate("R2 F02-R1 ignored one tick later", "WP08-F02-R1",
           lambda m: first(m, "DoubleLethalHit", "hit.player.ignored").__setitem__("simulationTick", first(m, "DoubleLethalHit", "player.death")["simulationTick"] + 1))
    def kb_two_sources(m):
        k = first(m, "ArmorWeakMagic", "enemy.knockback"); k["attribution"] = "Ambiguous"; k["velocitySets"] = 2
    mutate("R2 06-12 weak armor magic interval fed by two sources", "TC-06-12", kb_two_sources)

    # GPT R2 review §4 (#3, #4)
    mutate("GPT-R2 06-12 SingleSource interval marked blocked", "TC-06-12", lambda m: first(m, "ArmorWeakMagic", "enemy.knockback").update(blocked=True))
    mutate("GPT-R2 F02-R1 unknown second attacker and HitStun", "WP08-F02-R1",
           lambda m: first(m, "DoubleLethalHit", "hit.player.ignored").update(actorId="never_spawned", stateAfter="HitStun"))
    # R3 additions
    mutate("R3 F02-R1 ignored names an attack instance that is not active", "WP08-F02-R1",
           lambda m: first(m, "DoubleLethalGuard", "hit.player.ignored").update(enemyAttackInstanceId=-5))
    mutate("R3 F02-R1 ignored candidate with HP left", "WP08-F02-R1", lambda m: first(m, "DoubleLethalHit", "hit.player.ignored").update(hp=3))
    mutate("R3 06-12 AI moved during the interval", "TC-06-12", lambda m: first(m, "ArmorWeakMagic", "enemy.knockback").update(aiMoveDuring=True))
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--pw" in sys.argv:
        runs = [load(p) for p in args]
        out = {"logs": args, **pw_check(runs)}
        if "--selftest" in sys.argv:
            m = copy.deepcopy(runs)
            for e in m[1]:  # the 0.10 run: RB 0.12 s before parries (window ignored)
                if e["type"] == "guard.block" and abs(e.get("rbToHit", 0) - 0.12) < 0.011: e["type"] = "guard.parry"; e["parryWindow"] = 0.1
            r = pw_check(m)["TC-07-04"]["pass"]
            out["selftest"] = [{"case": "window 0.10 parries RB 0.12 s before", "sectionPass": r, "pass": r is False}]
            m2 = copy.deepcopy(runs)
            last = [e for e in m2[0] if e["type"] == "guard.parry"][-1]
            last["parryWindow"] = 0.01
            r2 = pw_check(m2)["TC-07-04"]["pass"]
            out["selftest"].append({"case": "M01 one parry record of a run with window 0.01", "sectionPass": r2, "pass": r2 is False})
            out["selftest_all_pass"] = all(x["pass"] for x in out["selftest"])
    else:
        ev = load(args[0])
        out = {"log": args[0], **check(ev)}
        if "--selftest" in sys.argv:
            out["selftest"] = selftest(ev)
            out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"][1:])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
