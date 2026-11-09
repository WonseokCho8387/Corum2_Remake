"""Summarise a WP-02 combat run log per test case (observed facts only, no verdicts).

Usage: python Tools/wp02_log_report.py Diagnostics/Runs/RUN-....ndjson
"""
import json
import sys
from collections import defaultdict

KEEP = {
    "attack.start", "attack.end", "attack.buffered", "attack.bufferExecuted", "attack.reserved", "attack.reservationExecuted",
    "hit", "hit.skip", "hit.player", "hitstop", "combo.begin", "combo.end", "combo.window", "combo.linkEnd",
    "guard.block", "guard.parry", "guard.turn", "dodge", "enemy.vanish", "enemy.removed", "player.death",
    "input.reject", "motor.jump", "marker",
}
FIELDS = ("attackId", "attackInstanceId", "attackChainStep", "comboBefore", "comboAfter", "comboHits", "damageD", "damageTaken",
          "hpBefore", "hpAfter", "rbToHit", "front", "superArmor", "knockbackVel", "detail", "seconds", "targets", "elapsed", "hits")


def main(path):
    ev = [json.loads(line) for line in open(path, encoding="utf-8") if line.strip()]
    print(f"run {ev[0]['runId']}  events {len(ev)}")
    print("dataVersion", ev[0]["dataVersion"])
    groups = defaultdict(list)
    for e in ev:
        groups[e.get("testCaseId", "")].append(e)
    for tc, es in groups.items():
        if tc in ("", "manual"):
            continue
        print(f"\n== {tc}  ticks {es[0]['simulationTick']}..{es[-1]['simulationTick']}")
        for e in es:
            if e["type"] not in KEEP:
                continue
            if e["type"] == "input.reject" and e["reason"].startswith("BufferFull"):
                continue
            extra = " ".join(f"{k}={e[k]}" for k in FIELDS if k in e and e[k] not in ("", None))
            who = e["actorId"] + ("->" + e["targetId"] if e["targetId"] else "")
            print(f"  t{e['simulationTick']:>5} {e['type']:<22} {who:<16} {e['stateBefore']}->{e['stateAfter']} [{e['reason'][:60]}] {extra}")


if __name__ == "__main__":
    main(sys.argv[1])
