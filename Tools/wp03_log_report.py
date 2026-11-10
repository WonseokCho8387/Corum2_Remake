#!/usr/bin/env python3
"""WP-03 ring/quickslot log report: one line per ring, item, quickslot, time-scale and test-case event.

Usage: python Tools/wp03_log_report.py RUN.ndjson [--all-routes]
Input routes are shown only when the consumer is ring or quickslot (or with --all-routes).
"""
import json
import sys

TYPES = {"ring.open", "ring.close", "ring.cursor", "ring.mode", "ring.page", "ring.discard", "ring.use", "ring.ignore",
         "quickslot.assign", "item.request", "item.use", "item.cancel", "item.wait", "magic.request", "time.scale",
         "input.reject", "player.hit", "player.death", "marker", "script.start", "script.end"}


def fmt(v):
    return json.dumps(v, ensure_ascii=False) if isinstance(v, (dict, list)) else str(v)


def main(path, all_routes):
    tc = ""
    with open(path, encoding="utf-8") as f:
        for line in f:
            e = json.loads(line)
            t = e.get("type", "")
            if e.get("testCaseId") != tc:
                tc = e.get("testCaseId")
                print(f"===== {tc}")
            route = t == "input.route" and (all_routes or e.get("consumer") in ("ring", "quickslot"))
            if t not in TYPES and not route:
                continue
            keys = {
                "input.route": ["action", "edge", "consumer", "ringMode", "playerState"],
                "ring.open": ["slowScale", "inventory", "quickslots"],
                "ring.close": ["mode"],
                "ring.cursor": ["selectedItem", "stick"],
                "ring.mode": ["target"],
                "ring.page": ["page", "pageCount", "selectedItem"],
                "ring.discard": ["item", "countBefore", "countAfter"],
                "ring.use": ["item", "requestId"],
                "ring.ignore": ["action"],
                "quickslot.assign": ["slot", "quickslots", "itemConsumed", "stickNeutral"],
                "item.request": ["requestId", "source", "via", "count", "cooldownLeft"],
                "item.use": ["requestId", "source", "requestTick", "execTick", "waitedFor", "hpBefore", "hpAfter", "countAfter", "quickslotsCleared"],
                "item.cancel": ["requestId", "consumed"],
                "item.wait": ["requestId"],
                "magic.request": ["magicId", "power", "castTime", "source"],
                "time.scale": ["timeScale", "gameTime", "ringOpenCount"],
                "player.hit": ["hpBefore", "hpAfter"],
            }.get(t, [])
            extra = " ".join(f"{k}={fmt(e[k])}" for k in keys if k in e)
            print(f"t{e.get('simulationTick', 0):>5} rt{e.get('realTime', 0):9.3f} {t:<16} {e.get('targetId','')[:10]:<10} "
                  f"{e.get('stateBefore','')}->{e.get('stateAfter','')} [{e.get('reason','')}] {extra}")


if __name__ == "__main__":
    main(sys.argv[1], "--all-routes" in sys.argv)
