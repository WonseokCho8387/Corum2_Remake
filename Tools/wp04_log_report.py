#!/usr/bin/env python3
"""WP-04 AI / boss log report: one line per AI decision, enemy attack start, boss pattern/phase, projectile,
hit on the player, guard/parry, hit on an enemy, death and script marker.

Usage: python Tools/wp04_log_report.py RUN.ndjson [--enemy ID]
"""
import json
import sys

TYPES = {"ai.decide", "ai.blocked", "ai.flee", "ai.retreat", "ai.jump", "ai.land", "ai.actionEnd",
         "boss.pattern", "boss.phase", "boss.event", "boss.transitionEnd", "projectile.spawn", "projectile.expire",
         "hit.player", "guard.block", "guard.parry", "hit", "player.death", "marker", "dodge", "enemy.spawn"}
KEYS = {
    "ai.decide": ["aiType", "distance", "playerState", "playerGrounded", "phase"],
    "ai.blocked": ["distance", "cooldown"],
    "ai.flee": ["fleeTime", "reapproach"],
    "ai.retreat": ["retreatTime"],
    "ai.jump": ["distance"],
    "boss.pattern": ["candidates", "distance", "playerGrounded", "priority", "uses", "special"],
    "boss.phase": ["condition", "hp", "hpRatio", "patterns", "superArmor", "staggerResistance", "attackSpeed", "transitionTime", "animation", "vfx", "camera"],
    "projectile.spawn": ["projectileId", "position", "pattern"],
    "projectile.expire": ["projectileId", "travelled"],
    "hit.player": ["via", "attackId", "hpBefore", "hpAfter"],
    "guard.block": ["via", "damageTaken", "parry"],
    "guard.parry": ["rbToHit"],
    "hit": ["attackId", "hpBefore", "hpAfter"],
}


def main(path, only):
    tc = None
    for line in open(path, encoding="utf-8"):
        e = json.loads(line)
        t = e["type"]
        if t == "enemy.state" and e.get("stateAfter") in ("AttackStartup", "PhaseTransition", "Dead"):
            pass
        elif t not in TYPES:
            continue
        if only and only not in (e.get("actorId"), e.get("targetId")) and t not in ("marker",):
            continue
        if e.get("testCaseId") != tc:
            tc = e.get("testCaseId")
            print(f"===== {tc}")
        extra = " ".join(f"{k}={e[k]}" for k in KEYS.get(t, []) if k in e)
        print(f"t{e['simulationTick']:>5} {t:<18} {e.get('actorId','')[:8]:<8}->{e.get('targetId','')[:8]:<8} "
              f"{e.get('stateBefore','')}->{e.get('stateAfter','')} [{e.get('reason','')}] {extra}")


if __name__ == "__main__":
    args = sys.argv[1:]
    only = args[args.index("--enemy") + 1] if "--enemy" in args else None
    main(args[0], only)
