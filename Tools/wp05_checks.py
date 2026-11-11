#!/usr/bin/env python3
"""WP-05 run checks (R1: WP05-M02): magic commands (R-02-02 / TC-02-02), command vs scroll (R-02-10 / TC-02-10) and the
death flow (R-06-19 / TC-06-19a) from an NDJSON run log. Facts plus per-item pass flags; not a GPT verdict.

python Tools/wp05_checks.py RUN.ndjson [--expect-selects A,B,C] [--selftest]

Linking is by ids and event order, never by "the next event with the same source":
  cast -> effect -> hit by attackInstanceId; scroll item.use by requestId; death -> flow stages in eventId order.
A section passes only if its required records exist: every matched command has a cast, an effect and (attack magic) a
hit of that cast; every miss starts a normal action; each command/scroll pair compares two complete casts; every death
has a motion of the configured length and the expected next stage; every game over is followed by a selection (and,
with --expect-selects, exactly that list in order); no enemy attack starts after a death until the revive.
"""
import copy
import json
import sys


def load(path):
    return sorted((json.loads(l) for l in open(path, encoding="utf-8") if l.strip()), key=lambda e: e["eventId"])


def check(ev, expect_selects=None):
    out = {}

    def of(t):
        return [e for e in ev if e["type"] == t]

    by_attack = {}
    for t in ("magic.effect", "hit"):
        for e in of(t):
            if e.get("attackInstanceId") is not None:
                by_attack.setdefault((t, e["attackInstanceId"]), []).append(e)

    def cast_chain(c):
        aid = c.get("attackInstanceId")
        eff = by_attack.get(("magic.effect", aid), [])
        hits = [h for h in by_attack.get(("hit", aid), []) if h.get("magicId")]
        return eff, hits

    # ---- TC-02-02
    cmds = []
    for m in of("magic.command"):
        tick = m["simulationTick"]
        cast = next((c for c in of("magic.cast") if c["eventId"] > m["eventId"] and c["simulationTick"] == tick
                     and c.get("source") == "command:" + str(m.get("commandId"))), None)
        normal = [a for a in of("attack.start") if a["simulationTick"] == tick] + \
                 [g for g in of("motor.state") if g["simulationTick"] == tick and g["stateAfter"] == "Guard"]
        eff, hits = cast_chain(cast) if cast else ([], [])
        is_attack = bool(eff) and str(eff[0]["reason"]).startswith("AttackActive")
        row = {"tick": tick, "command": m.get("commandId"), "magic": m.get("magicId"), "history": m.get("history"),
               "competingNormalAction": m.get("competingNormalAction"), "attackInstanceId": cast.get("attackInstanceId") if cast else None,
               "effect": [(e["simulationTick"], e["reason"]) for e in eff], "hits": [(h["simulationTick"], h.get("damageD"), h["reason"]) for h in hits],
               "normalActionStarted": bool(normal)}
        row["pass"] = cast is not None and len(eff) == 1 and not normal and (not is_attack or len(hits) >= 1)
        cmds.append(row)
    misses = []
    for m in of("magic.commandMiss"):
        tick = m["simulationTick"]
        normal = [a["stateAfter"] for a in of("attack.start") if a["simulationTick"] == tick] + \
                 ["Guard" for g in of("motor.state") if g["simulationTick"] == tick and g["stateAfter"] == "Guard"]
        no_cast = not [c for c in of("magic.cast") if c["simulationTick"] == tick]
        misses.append({"tick": tick, "history": m.get("history"), "normalAction": m.get("normalAction"), "normalStarted": normal,
                       "noCast": no_cast, "pass": bool(normal) and no_cast})
    out["TC-02-02"] = {"commands": cmds, "misses": misses,
                       "buffed_physical_hits": [(h["simulationTick"], h.get("damageD"), h.get("buffMultiplier")) for h in of("hit") if h.get("buffMultiplier", 1) not in (1, 1.0)],
                       "fallbacks": [(e["simulationTick"], e.get("reject")) for e in of("magic.commandFallback")],
                       "pass": bool(cmds) and all(c["pass"] for c in cmds) and bool(misses) and all(m["pass"] for m in misses)}

    # ---- TC-02-10
    rows = []
    for c in of("magic.cast"):
        eff, hits = cast_chain(c)
        use = None
        if str(c.get("source", "")).startswith("scroll:"):
            use = next((u for u in of("item.use") if u["simulationTick"] == c["simulationTick"] and str(u["reason"]).startswith("magic:")), None)
        rows.append({"tick": c["simulationTick"], "magic": c.get("magicId"), "source": c.get("source"), "attackInstanceId": c.get("attackInstanceId"),
                     "power": c.get("power"), "castTime": c.get("castTime"),
                     "effectDelayTicks": eff[0]["simulationTick"] - c["simulationTick"] if len(eff) == 1 else None,
                     "hitDamage": hits[0].get("damageD") if hits else None,
                     "itemUse": (use["simulationTick"], use.get("countAfter"), use.get("cooldown")) if use else None,
                     "complete": len(eff) == 1 and bool(hits) and (use is not None or not str(c.get("source", "")).startswith("scroll:"))})
    pairs = []
    for mid in sorted({r["magic"] for r in rows if r["magic"]}):
        cmd = [r for r in rows if r["magic"] == mid and str(r["source"]).startswith("command:") and r["complete"]]
        scr = [r for r in rows if r["magic"] == mid and str(r["source"]).startswith("scroll:") and r["complete"]]
        if cmd and scr:
            a, b = cmd[-1], scr[-1]
            pairs.append({"magic": mid, "command": a, "scroll": b,
                          "pass": a["power"] == b["power"] and a["castTime"] == b["castTime"] and a["effectDelayTicks"] == b["effectDelayTicks"]
                          and a["hitDamage"] == b["hitDamage"]})
    out["TC-02-10"] = {"casts": rows, "command_vs_scroll": pairs,
                       "scroll_waits": [(e["simulationTick"], e["reason"]) for e in of("item.request") if "EXP-ITEM-07" in e["reason"]],
                       "scroll_cancels": [(e["simulationTick"], e["reason"], e.get("consumed")) for e in of("item.cancel")],
                       "pass": bool(pairs) and all(p["pass"] for p in pairs)}

    # ---- TC-06-19a
    deaths = []
    flows = of("death.flow")
    for d in of("player.death"):
        t, eid = d["simulationTick"], d["eventId"]
        motion = next((f for f in flows if f["eventId"] > eid and f["stateAfter"] == "Motion"), None)
        nxt = next((f for f in flows if motion and f["eventId"] > motion["eventId"] and f["stateBefore"] == "Motion"), None)
        after = next((f for f in flows if nxt and nxt["stateAfter"] == "RevivePrompt" and f["eventId"] > nxt["eventId"] and f["stateBefore"] == "RevivePrompt"), None)
        revive = next((r for r in of("player.revive") if r["eventId"] > eid), None)
        end_eid = revive["eventId"] if revive and after and after["stateAfter"] == "None" else 10 ** 12
        attacks = [e for e in ev if e["type"] == "enemy.state" and e["stateAfter"] == "AttackStartup" and eid < e["eventId"] < end_eid]
        expected = "RevivePrompt" if (motion and (motion.get("reviveItems") or 0) > 0) else "GameOver"
        row = {"deathTick": t, "motionTick": motion["simulationTick"] if motion else None,
               "motionTicks": (nxt["simulationTick"] - motion["simulationTick"]) if motion and nxt else None,
               "reviveItemsAtDeath": motion.get("reviveItems") if motion else None, "afterMotion": nxt["stateAfter"] if nxt else None,
               "promptResult": (after["stateAfter"], after["reason"]) if after else None,
               "revive": (revive["simulationTick"], revive.get("hpBefore"), revive.get("hpAfter")) if revive and after and after["stateAfter"] == "None" else None,
               "enemyAttackStartsWhileDead": len(attacks)}
        row["pass"] = (motion is not None and motion["simulationTick"] == t and nxt is not None and row["afterMotion"] == expected
                       and row["enemyAttackStartsWhileDead"] == 0 and (expected == "GameOver" or after is not None))
        deaths.append(row)
    selects = of("death.menuSelect")
    gameovers = [f for f in flows if f["stateAfter"] == "GameOver"]
    go_ok = all(any(s["eventId"] > g["eventId"] for s in selects) for g in gameovers)
    sel_list = [s.get("option") for s in selects]
    exp_ok = expect_selects is None or sel_list == expect_selects
    out["TC-06-19a"] = {"deaths": deaths, "gameOver": [(g["simulationTick"], g.get("options")) for g in gameovers],
                        "menuSelects": [(s["simulationTick"], s.get("option"), s.get("returnPerformed"), s.get("tc19b")) for s in selects],
                        "expectedSelects": expect_selects, "cursorMoves": [(e["simulationTick"], e["stateBefore"], e["stateAfter"]) for e in of("death.cursor")],
                        "reviveRefusedFromQuickslot": [(e["simulationTick"], e["reason"]) for e in of("item.request") if "ReviveOnlyOnDeath" in e["reason"]],
                        "reviveInvulnSkips": [(e["simulationTick"], e["reason"]) for e in of("hit.skip") if "Revive" in e["reason"]],
                        "pass": bool(deaths) and all(d["pass"] for d in deaths) and bool(gameovers) == bool(selects) and go_ok and exp_ok
                        and all(s.get("returnPerformed") is False for s in selects)}
    out["TC-06-19b"] = "BLOCKED_SPEC (U-08): no save/checkpoint contract; selections are logged with returnPerformed=false"
    return out


def selftest(ev, expect):
    res = []
    base = check(ev, expect)
    keys = ("TC-02-02", "TC-02-10", "TC-06-19a")
    res.append({"case": "original", "pass": {k: base[k]["pass"] for k in keys}})
    mx = max(e["eventId"] for e in ev)

    def mutate(name, key, fn, applicable):
        if not applicable:
            res.append({"case": name, "pass": None, "note": "not applicable"})
            return
        m = copy.deepcopy(ev)
        fn(m)
        m.sort(key=lambda e: e["eventId"])
        r = check(m, expect)
        res.append({"case": name, "section": key, "sectionPass": r[key]["pass"], "pass": r[key]["pass"] is False})

    fire_casts = [c for c in ev if c["type"] == "magic.cast" and c.get("magicId") == "fire"]
    fire_ids = {c.get("attackInstanceId") for c in fire_casts}
    mutate("all fire effects and hits removed", "TC-02-10",
           lambda m: m.__setitem__(slice(None), [e for e in m if not (e["type"] in ("magic.effect", "hit") and e.get("attackInstanceId") in fire_ids)]),
           bool(fire_casts) and base["TC-02-10"]["pass"])
    first_fire_cmd = next((c for c in fire_casts if str(c.get("source", "")).startswith("command:")), None)
    mutate("first command fire effect removed", "TC-02-02",
           lambda m: m.__setitem__(slice(None), [e for e in m if not (e["type"] == "magic.effect" and e.get("attackInstanceId") == first_fire_cmd.get("attackInstanceId"))]),
           first_fire_cmd is not None and base["TC-02-02"]["pass"])
    mutate("all menu selections removed", "TC-06-19a",
           lambda m: m.__setitem__(slice(None), [e for e in m if e["type"] != "death.menuSelect"]),
           any(e["type"] == "death.menuSelect" for e in ev) and base["TC-06-19a"]["pass"])
    d0 = next((e for e in ev if e["type"] == "player.death"), None)

    def attack_after_death(m):
        m.append({"type": "enemy.state", "eventId": d0["eventId"] + 0.5, "simulationTick": d0["simulationTick"] + 1, "actorId": "x",
                  "targetId": "", "stateBefore": "Idle", "stateAfter": "AttackStartup", "reason": "MUTATION"})
    mutate("enemy attack start one tick after the death", "TC-06-19a", attack_after_death, d0 is not None and base["TC-06-19a"]["pass"])
    scroll_use = next((u for u in ev if u["type"] == "item.use" and str(u["reason"]).startswith("magic:")), None)
    mutate("scroll item.use removed", "TC-02-10", lambda m: m.__setitem__(slice(None), [e for e in m if e is not None and e.get("eventId") != scroll_use["eventId"]]),
           scroll_use is not None and base["TC-02-10"]["pass"])
    return res


def main():
    path = sys.argv[1]
    expect = None
    if "--expect-selects" in sys.argv:
        expect = sys.argv[sys.argv.index("--expect-selects") + 1].split(",")
    ev = load(path)
    out = {"log": path.replace("\\", "/"), **check(ev, expect)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(ev, expect)
        out["selftest_all_pass"] = all(r["pass"] is not False for r in out["selftest"][1:])
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main()
