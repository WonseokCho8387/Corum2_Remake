#!/usr/bin/env python3
"""WP-04 run checks: per-TC facts extracted from the NDJSON log (run from the repository root).

python Evidence/WP-04/tools/run_checks.py Evidence/WP-04/runs/RUN-*.ndjson
"""
import collections
import json
import sys


def main(path):
    ev = [json.loads(l) for l in open(path, encoding="utf-8")]

    def of(t, tc=None, actor=None):
        return [e for e in ev if e["type"] == t and (tc is None or e["testCaseId"] == tc) and (actor is None or e["actorId"] == actor)]

    starts = [e for e in ev if e["type"] == "enemy.state" and e["stateAfter"] == "AttackStartup"]
    by_tick = collections.Counter(e["simulationTick"] for e in starts if e["actorId"].startswith("ten"))
    death = next((e for e in ev if e["type"] == "player.death"), None)
    out = {"log": path}
    out["TC-08-01"] = {"max_ten_attack_starts_in_one_tick": max(by_tick.values()) if by_tick else 0,
                       "tick": max(by_tick, key=by_tick.get) if by_tick else None}
    out["TC-08-02"] = {"blocked": collections.Counter(e["actorId"] + ":" + e["reason"] for e in of("ai.blocked")),
                       "first_decisions_in_range": [(e["simulationTick"], e["actorId"], e["reason"]) for e in of("ai.decide") if "FirstChance" in e["reason"]][:12]}

    def decisions(actor):
        return [(e["simulationTick"], e["stateAfter"], e["reason"], round(e.get("distance", 0), 2)) for e in of("ai.decide", actor=actor)]

    def hits_on(actor):
        return [(e["simulationTick"], e["stateBefore"], e["stateAfter"], e["reason"]) for e in of("hit") if e["targetId"] == actor]

    out["TC-08-03"] = {"decisions": decisions("ch"), "hits_on_chaser": hits_on("ch"),
                       "attack_starts": [e["simulationTick"] for e in starts if e["actorId"] == "ch"]}
    out["TC-08-04"] = {
        "fl": {"flee": [(e["simulationTick"], e.get("reapproach")) for e in of("ai.flee", actor="fl")],
               "end": [(e["simulationTick"], e["stateAfter"]) for e in of("ai.actionEnd", actor="fl")], "decisions": decisions("fl")},
        "fh": {"flee": [(e["simulationTick"], e.get("reapproach")) for e in of("ai.flee", actor="fh")],
               "end": [(e["simulationTick"], e["stateAfter"]) for e in of("ai.actionEnd", actor="fh")], "decisions": decisions("fh")},
    }
    out["TC-08-05"] = {"jumps": [(e["simulationTick"], round(e.get("distance", 0), 2)) for e in of("ai.jump", actor="jp")],
                       "attack_starts": [(e["simulationTick"], e["reason"]) for e in starts if e["actorId"] == "jp"],
                       "lands": [e["simulationTick"] for e in of("ai.land", actor="jp")]}
    out["TC-08-06"] = {"decisions": decisions("rg"),
                       "shots": [(e["simulationTick"], e.get("projectileId")) for e in of("projectile.spawn", actor="rg")],
                       "player_hits_from_rg": [(e["simulationTick"], e.get("via")) for e in of("hit.player", actor="rg")],
                       "guard_blocks_from_rg": [(e["simulationTick"], e.get("via"), e.get("damageTaken"), e.get("parry")) for e in of("guard.block") if e["targetId"] == "rg"],
                       "retreats": [e["simulationTick"] for e in of("ai.retreat", actor="rg")]}
    if death:
        t = death["simulationTick"]
        out["TC-08-07"] = {"death_tick": t, "attack_starts_after_death": [(e["simulationTick"], e["actorId"]) for e in starts if e["simulationTick"] > t],
                           "player_dead_decisions": len([e for e in of("ai.decide") if "PlayerDead" in e["reason"]]),
                           "blocked_reattack_logs": len(of("ai.blocked"))}
    out["TC-08-08"] = [(e["simulationTick"], e["testCaseId"], e["stateAfter"], e.get("candidates"), round(e.get("distance", 0), 2), e.get("playerGrounded"))
                       for e in of("boss.pattern")]
    out["TC-08-09/10/11"] = [(e["simulationTick"], e["stateBefore"], e["stateAfter"], e.get("condition"), e.get("hpRatio"), e.get("superArmor"),
                              e.get("staggerResistance"), e.get("attackSpeed"), e.get("transitionTime"), e.get("animation"), e.get("vfx"), e.get("camera"))
                             for e in of("boss.phase")]
    out["TC-08-12"] = {"parry": [(e["simulationTick"], e["targetId"]) for e in of("guard.parry")],
                       "hits_on_boss": hits_on("boss"),
                       "boss_jumps": [e["simulationTick"] for e in of("ai.jump", actor="boss")]}
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main(sys.argv[1])
