#!/usr/bin/env python3
"""WP-05 run checks: magic commands (R-02-02 / TC-02-02), command vs scroll (R-02-10 / TC-02-10) and the death flow
(R-06-19 / TC-06-19a) from an NDJSON run log. Facts plus per-item pass flags; not a GPT verdict.

python Tools/wp05_checks.py RUN.ndjson
"""
import json
import sys


def main(path):
    ev = sorted((json.loads(l) for l in open(path, encoding="utf-8") if l.strip()), key=lambda e: e["eventId"])
    out = {"log": path.replace("\\", "/")}

    def of(t):
        return [e for e in ev if e["type"] == t]

    def same_tick(t, tick):
        return [e for e in ev if e["type"] == t and e["simulationTick"] == tick]

    # ---- TC-02-02
    cmds = []
    for m in of("magic.command"):
        tick = m["simulationTick"]
        casts = [c for c in same_tick("magic.cast", tick) if c.get("source") == "command:" + m.get("commandId")]
        normal = [a for a in same_tick("attack.start", tick)] + [g for g in same_tick("motor.state", tick) if g["stateAfter"] in ("Guard", "Airborne")]
        effect = next((x for x in of("magic.effect") if x["eventId"] > m["eventId"] and x.get("source") == "command:" + m.get("commandId")), None)
        cmds.append({"tick": tick, "command": m.get("commandId"), "magic": m.get("magicId"), "history": m.get("history"),
                     "competingNormalAction": m.get("competingNormalAction"), "cast": bool(casts), "normalActionStarted": bool(normal),
                     "effect": (effect["simulationTick"], effect["reason"], effect.get("hpBefore"), effect.get("hpAfter"), effect.get("multiplier")) if effect else None,
                     "pass": bool(casts) and not normal and effect is not None})
    misses = []
    for m in of("magic.commandMiss"):
        tick = m["simulationTick"]
        normal = [a["type"] + ":" + a["stateAfter"] for a in same_tick("attack.start", tick)] + \
                 ["guard" for g in same_tick("motor.state", tick) if g["stateAfter"] == "Guard"]
        misses.append({"tick": tick, "history": m.get("history"), "normalAction": m.get("normalAction"), "normalStarted": normal,
                       "noCast": not same_tick("magic.cast", tick), "pass": bool(normal) and not same_tick("magic.cast", tick)})
    buffed = [(h["simulationTick"], h.get("damageD"), h.get("buffMultiplier")) for h in of("hit") if h.get("buffMultiplier", 1) not in (1, 1.0)]
    magic_hits = [(h["simulationTick"], h.get("magicId"), h.get("magicSource"), h.get("damageD"), h.get("damageType"), h["reason"]) for h in of("hit") if h.get("magicId")]
    out["TC-02-02"] = {"commands": cmds, "misses": misses, "buffed_physical_hits": buffed, "magic_hits": magic_hits,
                       "fallbacks": [(e["simulationTick"], e.get("reject")) for e in of("magic.commandFallback")],
                       "pass": bool(cmds) and all(c["pass"] for c in cmds) and bool(misses) and all(m["pass"] for m in misses)}

    # ---- TC-02-10
    casts = of("magic.cast")
    effects = of("magic.effect")
    rows = []
    for c in casts:
        eff = next((x for x in effects if x["eventId"] > c["eventId"] and x.get("source") == c.get("source")), None)
        hit = next((h for h in of("hit") if eff and h["simulationTick"] == eff["simulationTick"] and h.get("magicSource") == c.get("source")), None)
        rows.append({"tick": c["simulationTick"], "magic": c.get("magicId"), "source": c.get("source"), "power": c.get("power"),
                     "castTime": c.get("castTime"), "effectDelayTicks": eff["simulationTick"] - c["simulationTick"] if eff else None,
                     "hitDamage": hit.get("damageD") if hit else None})
    pairs = []
    for mid in sorted({r["magic"] for r in rows}):
        cmd = [r for r in rows if r["magic"] == mid and str(r["source"]).startswith("command:")]
        scr = [r for r in rows if r["magic"] == mid and str(r["source"]).startswith("scroll:")]
        if cmd and scr:
            a, b = cmd[-1], scr[-1]
            pairs.append({"magic": mid, "command": a, "scroll": b,
                          "pass": a["power"] == b["power"] and a["castTime"] == b["castTime"] and a["effectDelayTicks"] == b["effectDelayTicks"]
                          and a["hitDamage"] == b["hitDamage"]})
    out["TC-02-10"] = {"casts": rows, "command_vs_scroll": pairs, "pass": bool(pairs) and all(p["pass"] for p in pairs)}

    # ---- TC-06-19a
    deaths = []
    for d in of("player.death"):
        t = d["simulationTick"]
        flow = [f for f in of("death.flow") if f["simulationTick"] >= t]
        motion = next((f for f in flow if f["stateAfter"] == "Motion"), None)
        nxt = next((f for f in flow if f["stateBefore"] == "Motion"), None)
        after = []
        if nxt:
            if nxt["stateAfter"] == "RevivePrompt":
                after = [f for f in flow if f["stateBefore"] == "RevivePrompt"][:1]
        revive = next((r for r in of("player.revive") if r["simulationTick"] >= t), None)
        expected = "RevivePrompt" if (motion and motion.get("reviveItems", 0) > 0) else "GameOver"
        row = {"deathTick": t, "motionTick": motion["simulationTick"] if motion else None,
               "motionTicks": (nxt["simulationTick"] - motion["simulationTick"]) if motion and nxt else None,
               "reviveItemsAtDeath": motion.get("reviveItems") if motion else None, "afterMotion": nxt["stateAfter"] if nxt else None,
               "promptResult": (after[0]["stateAfter"], after[0]["reason"]) if after else None,
               "revive": (revive["simulationTick"], revive.get("hpBefore"), revive.get("hpAfter")) if revive and after and after[0]["stateAfter"] == "None" else None,
               "enemyAttackStartsWhileDead": len([e for e in ev if e["type"] == "enemy.state" and e["stateAfter"] == "AttackStartup"
                                                  and t + 30 < e["simulationTick"] <= (revive["simulationTick"] if revive and revive["simulationTick"] > t else 10 ** 9)])}
        row["pass"] = motion is not None and motion["simulationTick"] == t and row["afterMotion"] == expected and row["enemyAttackStartsWhileDead"] == 0
        deaths.append(row)
    selects = [(e["simulationTick"], e.get("option"), e.get("returnPerformed"), e.get("tc19b")) for e in of("death.menuSelect")]
    gameovers = [(e["simulationTick"], e.get("options")) for e in of("death.flow") if e["stateAfter"] == "GameOver"]
    out["TC-06-19a"] = {"deaths": deaths, "gameOver": gameovers, "menuSelects": selects,
                        "cursorMoves": [(e["simulationTick"], e["stateBefore"], e["stateAfter"]) for e in of("death.cursor")],
                        "reviveRefusedFromQuickslot": [(e["simulationTick"], e["reason"]) for e in of("item.request") if "ReviveOnlyOnDeath" in e["reason"]],
                        "deathInputRouted": len([e for e in of("input.route") if e["reason"].startswith("Forward:DeathFlow")]),
                        "pass": bool(deaths) and all(d["pass"] for d in deaths) and all(s[2] is False for s in selects)}
    out["TC-06-19b"] = "BLOCKED_SPEC (U-08): no save/checkpoint contract; selections are logged with returnPerformed=false"
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main(sys.argv[1])
