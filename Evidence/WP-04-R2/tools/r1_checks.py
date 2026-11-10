#!/usr/bin/env python3
"""WP-04-R1 run checks: facts for WP04-F01/F02/F03/M01 and the C01/C02 corrections, from the NDJSON log.

python Evidence/WP-04-R1/tools/r1_checks.py Evidence/WP-04-R1/runs/RUN-*.ndjson
"""
import collections
import json
import sys


def main(path):
    ev = [json.loads(l) for l in open(path, encoding="utf-8")]
    out = {"log": path.replace("\\", "/")}

    def of(t, actor=None):
        return [e for e in ev if e["type"] == t and (actor is None or e["actorId"] == actor)]

    starts = [e for e in ev if e["type"] == "enemy.state" and e["stateAfter"] == "AttackStartup"]

    # C01: tick with the most simultaneous attack starts of the ten chasers
    by_tick = collections.Counter(e["simulationTick"] for e in starts if e["actorId"].startswith("ten"))
    out["C01_ten_simultaneous_start"] = {"tick": max(by_tick, key=by_tick.get), "count": max(by_tick.values())}

    # C02: Dodge events with a boss
    out["C02_boss_dodge"] = [(e["simulationTick"], e["targetId"]) for e in of("dodge") if e["targetId"] in ("boss", "sentinel")]

    # F01: every AI propulsion cut, and the boss/sentinel movement while in a reaction after it
    out["F01_propulsion_cuts"] = [(e["simulationTick"], e["actorId"], e["stateBefore"], e["stateAfter"], e["reason"], e.get("moveVelBefore"), e.get("airborne"))
                                  for e in of("ai.propulsionCut")]

    # F02: quick jumper air attacks and air blocks by the re-attack interval
    out["F02_jumperQuick"] = {
        "jumps": [(e["simulationTick"], round(e.get("distance", 0), 2)) for e in of("ai.jump", "jq")],
        "air_attack_starts": [e["simulationTick"] for e in starts if e["actorId"] == "jq" and e["reason"].startswith("AirAttack")],
        "air_blocked": [(e["simulationTick"], e["reason"], round(e.get("cooldown", 0), 3)) for e in of("ai.blocked", "jq") if ":Air" in e["reason"]],
        "lands": [e["simulationTick"] for e in of("ai.land", "jq")],
    }

    # F03: per transition: start, hits inside, end, first new attack
    trans = []
    for ph in of("boss.phase"):
        if ph["stateBefore"] == "":
            continue
        a = ph["actorId"]
        t0 = ph["simulationTick"]
        end = next((e for e in ev if e["type"] in ("boss.transitionEnd", "boss.transitionCancel") and e["actorId"] == a and e["simulationTick"] >= t0), None)
        t1 = end["simulationTick"] if end else None
        hits = [(e["simulationTick"], e["stateBefore"], e["stateAfter"], e["reason"]) for e in of("hit")
                if e["targetId"] == a and t1 is not None and t0 <= e["simulationTick"] <= t1]
        first = next((e["simulationTick"] for e in starts if e["actorId"] == a and e["simulationTick"] >= t0), None)
        trans.append({"actor": a, "from": ph["stateBefore"], "to": ph["stateAfter"], "condition": ph.get("condition"), "start": t0,
                      "transitionTime": ph.get("transitionTime"), "end": t1, "end_reason": end["reason"] if end else None,
                      "hits_inside": hits, "first_attack_start": first,
                      "no_attack_before_end": first is None or t1 is None or first >= t1})
    out["F03_transitions"] = trans

    # M01: sentinel guard / evade decisions and what happened to the following hit
    defense = []
    for d in of("boss.defense"):
        t = d["simulationTick"]
        nxt = next((e for e in of("hit") if e["targetId"] == d["actorId"] and t <= e["simulationTick"] <= t + 15), None)
        defense.append({"tick": t, "actor": d["actorId"], "phase": d["stateBefore"], "pick": d["stateAfter"], "guardFrequency": d.get("guardFrequency"),
                        "evadeFrequency": d.get("evadeFrequency"), "guardAcc": d.get("guardAcc"), "evadeAcc": d.get("evadeAcc"),
                        "distance": round(d.get("distance", 0), 3),
                        "next_hit": (nxt["simulationTick"], nxt["stateBefore"], nxt["stateAfter"], nxt["reason"], nxt.get("damageD"), nxt.get("damageTaken"))
                        if nxt else None})
    out["M01_defense"] = defense
    out["M01_states"] = [(e["simulationTick"], e["actorId"], e["stateBefore"], e["stateAfter"], e["reason"], e.get("position")) for e in of("enemy.state")
                         if e["actorId"] == "sentinel" and (e["stateAfter"] in ("Guard", "Evade") or e["stateBefore"] in ("Guard", "Evade"))]
    out["M01_boss_jumps"] = [(e["simulationTick"], e["actorId"], e["reason"], round(e.get("distance", 0), 2)) for e in of("ai.jump") if e["actorId"] in ("boss", "sentinel")]
    out["M01_warden_defense_events"] = len([d for d in of("boss.defense") if d["actorId"] == "boss"])
    out["M01_input_rejects_in_sentinel_section"] = [(e["simulationTick"], e["reason"]) for e in of("input.reject") if 3500 <= e["simulationTick"] <= 4200]

    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main(sys.argv[1])
