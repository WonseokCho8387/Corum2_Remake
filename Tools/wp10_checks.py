"""WP-10 log checks: integration scenarios TC-INT-01, 03..08 (master 13.6).

Usage:
  python Tools/wp10_checks.py <main run>.ndjson <int07 run>.ndjson <int08 run>.ndjson [--selftest]
"""
import copy
import json
import re
import sys

DT = 0.02
SCENARIOS = ["TenMixedTargets", "DashJumpDiveWithInputs", "BossPhase2Setup", "BossArmorPhysical", "BossArmorMagic", "BossArmorSimultaneous", "MixedAiBattle",
             "TwoBossTransitions", "RegisterThenCancel",
             # R1 (GPT WP-10 review): weak magic on the boss super armor, the sentinel's s2 pattern
             "BossArmorWeakMagic", "SentinelPhase2Pattern", "QuestDiscardRefused", "NormalDiscardDone", "RegisterNeutralDpad", "AfterClose",
             "WakeupTargetAttack", "DeadTargetAttack", "DeathRevive", "DeathDecline", "SelectLobby", "SelectLoadSave", "SelectCheckpoint", "ScriptEnd"]
ENEMY = re.compile(r"([^:|]+):([A-Za-z]+)@(-?[\d.]+),(-?[\d.]+) hp(-?[\d.]+)(?: ph:(\w+))?")
ATTACK_STATES = ("AttackStartup", "AttackActive")


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


def enemies_at(t):
    return {m.group(1): (m.group(2), float(m.group(3)), float(m.group(5)), m.group(6)) for m in ENEMY.finditer(str(t.get("enemies", "")))}


def check_main(ev):
    seg = segments(ev)
    out = {}

    # ---- TC-INT-01: one chain on 10 mixed targets; only real hits count; dead targets end only their own link
    s = seg.get("TenMixedTargets", [])
    hits = of(s, "hit")
    chains = {h.get("chainId") for h in hits}
    seq = [h.get("comboAfter") for h in hits]
    end = next(iter(of(s, "combo.end")), {})
    deaths = [h for h in hits if h["reason"].startswith("Death")]
    dead_ids = {h["targetId"] for h in deaths}
    after_death = [h for h in hits if h["targetId"] in dead_ids and h["eventId"] > next(d["eventId"] for d in deaths if d["targetId"] == h["targetId"])]
    links = [e["targetId"] for e in of(s, "combo.linkEnd")]
    immune = [h for h in hits if h["targetId"] in ("i0", "i1") and h.get("attackId") == "launcher"]
    steps = [a.get("attackId") for a in of(s, "attack.start")]
    out["TC-INT-01"] = {"attacks": steps, "hitEvents": len(hits), "comboEnd": (end.get("reason"), end.get("attackChainStep"), end.get("comboHits")),
                        "dead": sorted(dead_ids), "linkEnds": sorted(links), "hitsOnDeadAfterDeath": len(after_death),
                        "launchImmune": [(h["targetId"], h["reason"], h["hpBefore"] - h["hpAfter"]) for h in immune],
                        "allHitsDamage": all(h.get("hpAfter") is not None and h["hpAfter"] < h["hpBefore"] for h in hits),
                        # R1 (GPT WP10-M01): every counted hit really damages; the 10 attacks are the declared standard chain
                        "pass": all(h.get("hpAfter") is not None and h["hpAfter"] < h["hpBefore"] for h in hits)
                        and steps == ["ground1", "ground2", "ground3", "ground4", "launcher", "air1", "air2", "air3", "air4", "dive"]
                        and all(h.get("attackInstanceId") in {a["attackInstanceId"] for a in of(s, "attack.start")} for h in hits)
                        and len(chains) == 1 and seq == list(range(1, len(hits) + 1)) and end.get("comboHits") == len(hits) and end.get("attackChainStep") == 10
                        and end.get("reason") == "Timeout" and dead_ids == {"w0", "w1", "w2"} and sorted(links) == ["w0", "w1", "w2"] and not after_death
                        and len(immune) == 2 and all(h["reason"].startswith("LaunchImmune") and h["hpAfter"] < h["hpBefore"] for h in immune)}

    # ---- TC-INT-03: dash -> remaining extra jump -> dive; RB refused in dash and dive, attack reserved for landing, run on landing
    s = seg.get("DashJumpDiveWithInputs", [])
    uses = [e["reason"] for e in of(s, "air.use")]
    rej = [(e.get("action"), e["reason"]) for e in of(s, "input.reject")]
    res = of(s, "attack.reserved")
    land = next(iter(of(s, "motor.land")), None)
    rx = next(iter(of(s, "attack.reservationExecuted")), None)
    gc = [e for e in of(s, "attack.end") if e["reason"] == "GuardCancel"]
    out["TC-INT-03"] = {"airUse": uses, "rejected": rej, "reserved": [e["simulationTick"] for e in res], "landTick": land["simulationTick"] if land else None,
                        "reservationExecuted": (rx["simulationTick"], rx["reason"]) if rx else None,
                        "pass": uses == ["AirDash", "ExtraJump", "Dive"] and ("Guard", "ActionLock:AirDash") in rej and ("Guard", "ActionLock:Dive") in rej
                        and ("Attack", "ActionLock:Dive") in rej and len(res) == 1 and land is not None and rx is not None
                        and rx["simulationTick"] == land["simulationTick"] and rx["reason"] == "attack:GroundNormal" and not gc
                        # R1 (GPT WP10-M01): the executed reservation is the reserved press, and that press really starts the ground attack on the landing tick
                        and rx.get("inputEventId") == res[0].get("inputEventId") is not None
                        and any(e["type"] == "input" and e.get("inputEventId") == res[0].get("inputEventId") and e.get("action") == "Attack" and e.get("edge") == "Press" for e in s)
                        and any(a.get("inputEventId") == rx.get("inputEventId") and a["simulationTick"] == land["simulationTick"] and a.get("attackId") == "ground1"
                                for a in of(s, "attack.start"))}

    # ---- TC-INT-04: boss super-armor phase (p2): physical alone = damage only, magic alone = break + cancel, simultaneous = Dodge
    def phase_at(s, eid, tick):
        t = next((e for e in s if e["type"] == "tick" and e["simulationTick"] == tick), {})
        return enemies_at(t).get(eid, (None, None, None, None))[3]
    rows = []
    s = seg.get("BossArmorPhysical", [])
    h = next(iter(of(s, "hit")), None)
    act = [e for e in of(s, "enemy.state") if e.get("actorId") == "wb" and e.get("stateAfter") == "AttackActive" and h and e["simulationTick"] > h["simulationTick"]]
    rows.append({"case": "physical", "phase": phase_at(s, "wb", h["simulationTick"]) if h else None, "hit": (h["reason"], h["stateBefore"], h["stateAfter"]) if h else None,
                 "attackContinued": bool(act),
                 "pass": h is not None and h["reason"].startswith("SuperArmor:DamageOnly") and h["stateBefore"] == "AttackStartup" and bool(act) and phase_at(s, "wb", h["simulationTick"]) == "p2"})
    def knock(s, eid, h):
        """R1 (GPT WP-10 review section 2): the hit's own single-source knockback interval, consistent with the hit and the ticks."""
        k = next((x for x in of(s, "enemy.knockback") if h and x.get("actorId") == eid and h["simulationTick"] < x.get("startTick", -1) <= h["simulationTick"] + 8), None)
        if not k: return None, None
        t0 = next((e for e in s if e["type"] == "tick" and e["simulationTick"] == k["startTick"] - 1), {})
        t1 = next((e for e in s if e["type"] == "tick" and e["simulationTick"] == k["simulationTick"]), {})
        x0, x1 = enemies_at(t0).get(eid, (None, None))[1], enemies_at(t1).get(eid, (None, None))[1]
        kv = h.get("knockbackVel") or 0
        ok = x0 is not None and x1 is not None and abs(k["displacement"] - (k["endX"] - k["startX"])) <= 1e-4 and abs(k["startVel"] - kv) <= 1e-4 \
            and kv != 0 and k["displacement"] * kv > 0 and abs(k["startX"] - x0) <= 0.0051 and abs(k["endX"] - x1) <= 0.0051 \
            and k.get("attribution") == "SingleSource" and k.get("firstAttackInstanceId") == h.get("attackInstanceId")
        return k, ok
    for case, scn, eid in (("magic", "BossArmorMagic", "wb3"), ("weakMagic(R1)", "BossArmorWeakMagic", "wb5")):
        s = seg.get(scn, [])
        h = next((e for e in of(s, "hit") if e.get("damageType") == "Magic"), None)
        nxt = next((e for e in of(s, "enemy.state") if h and e.get("actorId") == eid and e["eventId"] > h["eventId"]), None)
        cast = next((e for e in of(s, "magic.cast") if h and e.get("attackInstanceId") == h.get("attackInstanceId")), None)
        k, kok = knock(s, eid, h)
        rows.append({"case": case, "phase": phase_at(s, eid, h["simulationTick"]) if h else None, "hit": (h["reason"], h["stateBefore"], h["stateAfter"], h.get("knockbackVel")) if h else None,
                     "nextState": (nxt["stateBefore"], nxt["stateAfter"]) if nxt else None,
                     "knockback": (k["startVel"], k["displacement"], k.get("attribution")) if k else None,
                     "pass": h is not None and cast is not None and h["reason"].startswith("MagicBreaksArmor") and h["stateBefore"] == "AttackStartup" and h["stateAfter"] == "Stagger"
                     and h["hpAfter"] < h["hpBefore"] and nxt is not None and nxt["stateBefore"] == "Stagger" and phase_at(s, eid, h["simulationTick"]) == "p2"
                     and (h.get("knockbackVel") or 0) != 0 and bool(kok)})
    s = seg.get("BossArmorSimultaneous", [])
    d = next(iter(of(s, "dodge")), None)
    wb4_att = [e for e in of(s, "enemy.state") if e.get("actorId") == "wb4" and e.get("stateAfter") == "AttackStartup"]
    rows.append({"case": "simultaneous", "phase": phase_at(s, "wb4", d["simulationTick"]) if d else None,
                 "dodge": (d["targetId"], d.get("damage"), d.get("enemyArmor")) if d else None,
                 "pass": d is not None and d.get("damage") == 0 and d.get("enemyArmor") is True and phase_at(s, "wb4", d["simulationTick"]) == "p2"
                 # R1 (GPT WP10-M01): the Dodge is with wb4, whose attack was running, and neither HP changes on that tick
                 and d.get("targetId") == "wb4" and bool(wb4_att) and wb4_att[0]["simulationTick"] <= d["simulationTick"]
                 and phase_at(s, "wb4", d["simulationTick"]) is not None})
    out["TC-INT-04"] = {"cases": rows, "note": "the magic case uses a warden copy whose p2 attacks at speed 0.5 (startup 0.7 s), test data",
                        "pass": all(r["pass"] for r in rows)}

    # ---- TC-INT-05: four AI types together: no global attack limit, state constraints kept, lock times reported
    s = seg.get("MixedAiBattle", [])
    ticks = [e for e in s if e["type"] == "tick"]
    ids = ("ch", "fl", "jp", "rg")
    conc = [sum(1 for k in ids if enemies_at(t).get(k, ("",))[0] in ATTACK_STATES) for t in ticks]
    starts = [e for e in of(s, "enemy.state") if e.get("actorId") in ids and e.get("stateAfter") == "AttackStartup"]
    proj = of(s, "projectile.spawn")
    attackers = sorted({e["actorId"] for e in starts} | {e.get("actorId") for e in proj})
    runs, cur = [], []
    for t in ticks:
        if t.get("stateAfter") == "HitStun": cur.append(t["simulationTick"])
        elif cur: runs.append(cur); cur = []
    if cur: runs.append(cur)
    stun_ticks = {n for r in runs for n in r}
    acts_in_stun = [e for e in of(s, "attack.start") if e["simulationTick"] in stun_ticks and e["simulationTick"] - 1 in stun_ticks]
    out["TC-INT-05"] = {"enemyAttackStarts": len(starts), "projectiles": len(proj), "attackers": attackers, "maxConcurrentEnemyAttacks": max(conc or [0]),
                        "playerHits": len(of(s, "hit.player")), "guards": len(of(s, "guard.block")), "parries": len(of(s, "guard.parry")),
                        "rejectedInHitStun": len([e for e in of(s, "input.reject") if e["reason"] == "ActionLock:HitStun"]),
                        "lockRuns(ticks)": [len(r) for r in runs], "longestLockSeconds": round(max([len(r) for r in runs] or [0]) * DT, 2),
                        "note": "observation for feel (lock, pressure) is a user review item",
                        # R1 (GPT WP10-M01): at least one RB press in the fight is resolved as a guard or a parry (the defense really happened)
                        "pass": max(conc or [0]) >= 2 and len(attackers) >= 3 and not acts_in_stun and len(runs) > 0
                        and len(of(s, "guard.block")) + len(of(s, "guard.parry")) >= 1
                        and any(e["type"] == "input" and e.get("action") == "Guard" and e.get("edge") == "Press" for e in s)}

    # ---- TC-INT-06: two bosses: HP transition (warden) and event transition (sentinel), independent, each with its own presentation
    s = seg.get("TwoBossTransitions", []) + seg.get("SentinelPhase2Pattern", [])
    ph = [(e["simulationTick"], e["actorId"], e["stateBefore"], e["stateAfter"], e.get("condition")) for e in of(s, "boss.phase")]
    pres = [(e["simulationTick"], e["actorId"], e.get("phase")) for e in of(s, "presentation.start")]
    pats = [(e["simulationTick"], e["actorId"], e["stateBefore"], e["stateAfter"]) for e in of(s, "boss.pattern")]
    wb = [p for p in ph if p[1] == "wb2"]
    sn = [p for p in ph if p[1] == "sn"]
    ends = [(e["simulationTick"], e["actorId"], e.get("phase")) for e in of(s, "presentation.end")]
    # R1 (GPT WP10-M01 / INT-06): every transition of both bosses has its presentation start and end; warden p1->p2->p3;
    # after each transition the boss selects a pattern of the new phase AND that pattern really attacks (startup / projectile).
    def attacked(b, t):
        return any(e["simulationTick"] >= t and e.get("actorId") == b and (e["type"] == "projectile.spawn" or (e["type"] == "enemy.state" and e.get("stateAfter") == "AttackStartup"))
                   for e in s)
    after = []
    for (t, b, fr, to, c) in ph:
        end_t = next((r[0] for r in ends if r[1] == b and r[2] == to and r[0] >= t), None)
        sel = next((q for q in pats if q[1] == b and q[2] == to and end_t is not None and q[0] >= end_t), None)
        after.append({"boss": b, "phase": to, "transitionTick": t, "presentationEnd": end_t, "pattern": sel[3] if sel else None,
                      "attacked": bool(sel) and attacked(b, sel[0])})
    after_ok = bool(after) and all(a["pattern"] is not None and a["attacked"] for a in after)
    out["TC-INT-06"] = {"transitions": ph, "presentations": pres, "presentationEnds": ends, "afterTransition": after,
                        "pass": any(p[2] == "p1" and p[3] == "p2" and "Hp" in str(p[4]) for p in wb) and any(p[2] == "p2" and p[3] == "p3" for p in wb)
                        and any(p[2] == "s1" and p[3] == "s2" and "shift" in str(p[4]) for p in sn)
                        and all(any(r[0] == t and r[1] == b and r[2] == to for r in pres) for (t, b, fr, to, c) in ph) and after_ok}
    return out


def check_int07(ev):
    seg = segments(ev)
    rows = []
    s = seg.get("RegisterThenCancel", [])
    m = [(e["stateBefore"], e["stateAfter"], e["reason"]) for e in of(s, "ring.mode")]
    rows.append({"case": "RegisterThenCancel", "modes": m, "closed": len(of(s, "ring.close")), "itemUse": len(of(s, "item.use")),
                 "pass": ("Browse", "Registering", "TargetFixed(R-02-07)") in m and any(x[0] == "Registering" and "Cancelled" in x[2] for x in m) and not of(s, "ring.close") and not of(s, "item.use")})
    s = seg.get("QuestDiscardRefused", [])
    q = of(s, "ring.discard", "Blocked:Quest")
    rows.append({"case": "QuestDiscardRefused", "blocked": [(e.get("item"), e.get("count")) for e in q], "closed": len(of(s, "ring.close")),
                 "pass": bool(q) and all(e.get("item") == "key" for e in q) and not of(s, "ring.close")})
    s = seg.get("NormalDiscardDone", [])
    d = of(s, "ring.discard", "Confirmed")
    c = of(s, "ring.close", "Discarded")
    rows.append({"case": "NormalDiscardDone", "discarded": [(e.get("item"), e.get("count")) for e in d], "closed": [e["reason"] for e in c],
                 "pass": len(d) == 1 and d[0].get("item") != "key" and len(c) == 1
                 and d[0].get("countAfter") == d[0].get("countBefore", -99) - 1})  # R1 (GPT WP10-M01): exactly one item removed
    s = seg.get("RegisterNeutralDpad", [])
    ign = of(s, "ring.ignore", "Registering:StickNotReleased")
    rel = of(s, "ring.release")
    asg = of(s, "quickslot.assign")
    fixed = of(s, "ring.mode", "TargetFixed")
    cl = of(s, "ring.close", "Registered")
    rows.append({"case": "RegisterNeutralDpad", "ignoredBeforeRelease": len(ign), "assign": [(e["stateAfter"], e.get("slot"), e.get("itemConsumed")) for e in asg],
                 "closed": [e["reason"] for e in cl], "itemUse": len(of(s, "item.use")),
                 "pass": bool(ign) and bool(rel) and len(asg) == 1 and asg[0].get("itemConsumed") is False and ign[0]["eventId"] < rel[0]["eventId"] < asg[0]["eventId"]
                 and len(cl) == 1 and not of(s, "item.use")
                 # R1 (GPT WP10-M01): the registered item is the one fixed by Y
                 and len(fixed) == 1 and asg[0]["stateAfter"] == fixed[0].get("target") == asg[0].get("target")})
    ts = [e for e in ev if e["type"] == "time.scale"]
    s = seg.get("AfterClose", [])
    rows.append({"case": "AfterClose", "lastTimeScale": ts[-1]["stateAfter"] if ts else None, "attack": [e["reason"] for e in of(s, "attack.start")],
                 "pass": bool(ts) and ts[-1]["stateAfter"] == "1" and bool(of(s, "attack.start"))})
    return {"TC-INT-07": {"cases": rows, "pass": all(r["pass"] for r in rows)}}


def check_int08(ev):
    seg = segments(ev)
    rows = []
    s = seg.get("WakeupTargetAttack", [])
    sk = [e for e in of(s, "hit.skip") if e.get("stateAfter") == "Wakeup"]
    hw = [e for e in of(s, "hit") if e.get("stateBefore") == "Wakeup"]
    # R1 (GPT WP10-M01): each refusal names the enemy that is in Wakeup on that tick (from its own state records)
    def waking(eid, t):
        on = [e["simulationTick"] for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateAfter") == "Wakeup"]
        off = [e["simulationTick"] for e in of(s, "enemy.state") if e.get("actorId") == eid and e.get("stateBefore") == "Wakeup"]
        return any(a <= t < (min([b for b in off if b > a] or [10 ** 9])) for a in on)
    rows.append({"case": "WakeupTargetAttack", "skips": [(e["simulationTick"], e.get("targetId")) for e in sk], "hitsDuringWakeup": len(hw),
                 "pass": len(sk) >= 1 and not hw and all(waking(e.get("targetId"), e["simulationTick"]) for e in sk)})
    s = seg.get("DeadTargetAttack", [])
    dh = next((e for e in of(s, "hit") if e["reason"].startswith("Death")), None)
    later = [e for e in of(s, "hit") if dh and e["targetId"] == dh["targetId"] and e["eventId"] > dh["eventId"]]
    rows.append({"case": "DeadTargetAttack", "dead": dh["targetId"] if dh else None, "hitsAfterDeath": len(later), "pass": dh is not None and not later})
    s = seg.get("DeathRevive", [])
    flow = [(e["stateBefore"], e["stateAfter"]) for e in of(s, "death.flow")]
    rv = of(s, "death.revive")
    inv = [e for e in of(s, "hit.skip") if "Revive" in e["reason"]]
    rows.append({"case": "DeathRevive", "flow": flow, "revive": [(e.get("itemCountBefore"), e.get("itemCountAfter")) for e in rv], "reviveInvulnerableSkips": len(inv),
                 # R1 (GPT WP10-M01): the feather was owned (1 -> 0) and the revive invulnerability is shown by a refused enemy hit on the player
                 "pass": ("None", "Motion") in flow and ("Motion", "RevivePrompt") in flow and len(rv) == 1 and rv[0].get("itemCountAfter") == 0
                 and rv[0].get("itemCountBefore") == 1 and len(inv) >= 1 and all(e.get("targetId") == "player" and e["eventId"] > rv[0]["eventId"] for e in inv)})
    s = seg.get("DeathDecline", [])
    flow = [(e["stateBefore"], e["stateAfter"]) for e in of(s, "death.flow")]
    rows.append({"case": "DeathDecline", "flow": flow, "pass": ("RevivePrompt", "GameOver") in flow})
    sel = [e for e in ev if e["type"] == "death.menuSelect"]
    rows.append({"case": "GameOverMenus", "selected": [(e.get("option"), e.get("returnPerformed")) for e in sel],
                 "pass": [e.get("option") for e in sel] == ["Lobby", "LoadSave", "CheckpointRestart"] and all(e.get("returnPerformed") is False for e in sel)})
    ok = all(r["pass"] for r in rows)
    # R1 (GPT WP10-M01): the overall status is BLOCKED_SPEC; the observed parts are a separate value (never the TC verdict)
    return {"TC-INT-08": {"cases": rows, "observedPartsPass": ok, "status": "BLOCKED_SPEC",
                          "note": "TC-06-19b (real return to checkpoint / save / lobby) is BLOCKED_SPEC (U-08), so TC-INT-08 cannot be a full PASS (master 13.6)",
                          "pass": False}}


def selftest(main, i7, i8):
    res = []

    def mutate(name, key, which, fn):
        logs = [copy.deepcopy(main), copy.deepcopy(i7), copy.deepcopy(i8)]
        try:
            fn(logs[which])
        except (StopIteration, KeyError, IndexError) as x:
            res.append({"case": name, "section": key, "sectionPass": None, "pass": None, "note": f"record to mutate not present ({x!r})"})
            return
        sec = {**check_main(logs[0]), **check_int07(logs[1]), **check_int08(logs[2])}[key]
        r = sec["observedPartsPass"] if key == "TC-INT-08" else sec["pass"]  # INT-08 is BLOCKED_SPEC; its observed parts are what can fail
        res.append({"case": name, "section": key, "sectionPass": r, "pass": r is False})

    def first(m, scn, typ, pred=lambda e: True):
        return next(e for e in segments(m)[scn] if e["type"] == typ and pred(e))

    def hit_dead(m):
        s = segments(m)["TenMixedTargets"]
        d = next(e for e in s if e["type"] == "hit" and e["reason"].startswith("Death"))
        h = copy.deepcopy(d)
        h["eventId"] += 0.5
        m.insert(m.index(d) + 1, h)
    mutate("TC-INT-01 dead target hit again", "TC-INT-01", 0, hit_dead)
    mutate("TC-INT-03 RB cancels the dash", "TC-INT-03", 0, lambda m: first(m, "DashJumpDiveWithInputs", "input.reject", lambda e: e["reason"] == "ActionLock:AirDash").__setitem__("reason", "x"))
    mutate("TC-INT-04 boss-armor magic does not break the armor", "TC-INT-04", 0, lambda m: first(m, "BossArmorMagic", "hit").__setitem__("reason", "SuperArmor:DamageOnly(R-06-11)"))
    def one_at_a_time(m):
        for e in segments(m)["MixedAiBattle"]:
            if e["type"] == "tick": e["enemies"] = re.sub(r"(fl|jp|rg):Attack(Startup|Active)", r"\1:Idle", e.get("enemies", ""))
    mutate("TC-INT-05 never two enemy attacks at once", "TC-INT-05", 0, one_at_a_time)
    mutate("TC-INT-06 sentinel follows the warden's transition", "TC-INT-06", 0, lambda m: first(m, "TwoBossTransitions", "boss.phase", lambda e: e["actorId"] == "sn").__setitem__("condition", "HpBelow:0.6"))
    def quest_gone(m):
        for e in segments(m)["QuestDiscardRefused"]:
            if e["type"] == "ring.discard": e["reason"] = "Confirmed(R-02-08)"
    mutate("TC-INT-07 quest item discarded", "TC-INT-07", 1, quest_gone)
    mutate("TC-INT-08 wakeup hit lands", "TC-INT-08", 2, lambda m: first(m, "WakeupTargetAttack", "hit.skip").update({"type": "hit", "stateBefore": "Wakeup"}))

    # GPT WP-10 review section 3 (WP10-M01): 14 counterexamples
    def seg_of(m, scn):
        return segments(m)[scn]
    def zero(m):
        e = first(m, "TenMixedTargets", "hit"); e.update(hpAfter=e["hpBefore"], damageTaken=0)
    def drop(which, scn, typ, pred=lambda e: True):
        def f(m):
            ids = {e["eventId"] for e in seg_of(m, scn) if e["type"] == typ and pred(e)}
            if not ids: raise StopIteration
            m[:] = [e for e in m if e["eventId"] not in ids]
        return f
    gpt = [
        ("GPT count without damage", "TC-INT-01", 0, zero),
        ("GPT tenth attack is ground1", "TC-INT-01", 0, lambda m: [e for e in seg_of(m, "TenMixedTargets") if e["type"] == "attack.start"][-1].update(attackId="ground1")),
        ("GPT unrelated reservation id", "TC-INT-03", 0, lambda m: first(m, "DashJumpDiveWithInputs", "attack.reservationExecuted").update(inputEventId=90001)),
        ("GPT landing ground attack missing", "TC-INT-03", 0, lambda m: drop(0, "DashJumpDiveWithInputs", "attack.start",
            lambda e: e["simulationTick"] == first(m, "DashJumpDiveWithInputs", "attack.reservationExecuted")["simulationTick"])(m)),
        ("GPT zero magic knockback", "TC-INT-04", 0, lambda m: first(m, "BossArmorMagic", "hit").update(knockbackVel=0)),
        ("GPT wrong dodge target", "TC-INT-04", 0, lambda m: first(m, "BossArmorSimultaneous", "dodge").update(targetId="other")),
        ("GPT all parries absent", "TC-INT-05", 0, drop(0, "MixedAiBattle", "guard.parry")),
        ("GPT no p2 -> p3 transition", "TC-INT-06", 0, drop(0, "TwoBossTransitions", "boss.phase", lambda e: e["stateAfter"] == "p3")),
        ("GPT presentation ends missing", "TC-INT-06", 0, drop(0, "TwoBossTransitions", "presentation.end")),
        ("GPT registered item differs from the fixed one", "TC-INT-07", 1, lambda m: first(m, "RegisterNeutralDpad", "quickslot.assign").update(stateAfter="key", target="key")),
        ("GPT discard count grows", "TC-INT-07", 1, lambda m: first(m, "NormalDiscardDone", "ring.discard").update(countAfter=999)),
        ("GPT revive invulnerability proof missing", "TC-INT-08", 2, drop(2, "DeathRevive", "hit.skip")),
        ("GPT feather was not owned", "TC-INT-08", 2, lambda m: first(m, "DeathRevive", "death.revive").update(itemCountBefore=0)),
        ("GPT wakeup refusal on another target", "TC-INT-08", 2, lambda m: [e.update(targetId="other") for e in seg_of(m, "WakeupTargetAttack") if e["type"] == "hit.skip"]),
    ]
    for name, key, which, fn in gpt:
        mutate(name, key, which, fn)

    # R1 cases
    mutate("R1 INT-04 weak magic does not move the boss", "TC-INT-04", 0,
           lambda m: first(m, "BossArmorWeakMagic", "enemy.knockback").update(endX=first(m, "BossArmorWeakMagic", "enemy.knockback")["startX"], displacement=0.0))
    mutate("R1 INT-04 weak magic interval fed by two sources", "TC-INT-04", 0,
           lambda m: first(m, "BossArmorWeakMagic", "enemy.knockback").update(attribution="Ambiguous", velocitySets=2))
    mutate("R1 INT-06 sentinel never attacks in s2", "TC-INT-06", 0, drop(0, "SentinelPhase2Pattern", "projectile.spawn", lambda e: e.get("actorId") == "sn"))
    mutate("R1 INT-06 sentinel never selects an s2 pattern", "TC-INT-06", 0, drop(0, "SentinelPhase2Pattern", "boss.pattern", lambda e: e.get("actorId") == "sn"))
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    m, i7, i8 = load(args[0]), load(args[1]), load(args[2])
    out = {"logs": args, **check_main(m), **check_int07(i7), **check_int08(i8)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(m, i7, i8)
        out["selftest_all_pass"] = all(r["pass"] is True for r in out["selftest"])
    print(json.dumps(out, indent=1, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()
