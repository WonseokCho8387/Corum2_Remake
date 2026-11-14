"""P1 Play smoke checks (relaxed bar): reads one GameHost run log and checks the key outcome of each scripted
scenario by its test-case id. Prints JSON. Usage: python Tools/p1_play_checks.py <run.ndjson>

The checks look only at what the scenario is about (T201~T204, growth screen, missile commit, merchant pack); they
are a reading aid for the Play smoke, not a log tamper detector (2026-11-14 user verification bar).
"""
import json
import sys


def load(path):
    ev = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            e = json.loads(line)
            if e.get("type") in ("tick", "frame.sample", "input.route"):
                continue
            ev.append(e)
    return ev


def f(e, k, d=None):
    return e.get(k, e.get("fields", {}).get(k, d)) if isinstance(e, dict) else d


def by_tc(ev, tc):
    return [e for e in ev if e.get("testCaseId") == tc]


def first(es, t, reason=None):
    for e in es:
        if e["type"] == t and (reason is None or e.get("reason", "").startswith(reason)):
            return e
    return None


def count(es, t, reason=None):
    return sum(1 for e in es if e["type"] == t and (reason is None or e.get("reason", "").startswith(reason)))


def marker_result(es, cmd):
    for e in es:
        if e["type"] == "marker" and e.get("reason") == "P1Script:" + cmd:
            return e
    return None


def main(path):
    ev = load(path)
    out = {"log": path, "checks": {}}
    c = out["checks"]

    a = by_tc(ev, "P1-PLAY-A-GROWTH")
    op = first(a, "p1.growth.open")
    kills = [e for e in a if e["type"] == "p1.exp.grant"]
    c["growth_screen"] = {
        "pass": op is not None and len(kills) == 3 and count(a, "p1.growth.choose") == 1,
        "growthOpenTick": op and op["simulationTick"], "expGrants": len(kills),
        "chosen": [e.get("card") for e in a if e["type"] == "p1.growth.choose"],
    }
    m = by_tc(ev, "P1-PLAY-A-MISSILE")
    cs = [e for e in m if e["type"] == "p1.magic.castStart"]
    cm = [e for e in m if e["type"] == "p1.magic.commit"]
    fire = [e for e in m if e["type"] == "p1.magic.missile"]
    c["missile_commit"] = {
        "pass": len(cm) >= 1 and all(x["simulationTick"] > y["simulationTick"] for x, y in zip(cm, cs)) and any(e.get("count") == 5 for e in fire),
        "castStartTicks": [e["simulationTick"] for e in cs], "commitTicks": [e["simulationTick"] for e in cm],
        "mpCost": [e.get("mpCost") for e in cm], "projectiles": [e.get("count") for e in fire],
    }
    b = by_tc(ev, "P1-PLAY-B-SHOP")
    c["shop_pack_unpaid"] = {
        "pass": first(b, "p1.merchant.open") is not None and first(b, "p1.merchant.pack") is not None and count(b, "p1.merchant.commit") == 0,
        "pack": (first(b, "p1.merchant.pack") or {}).get("cards"),
    }
    t204 = by_tc(ev, "P1-PLAY-T204")
    c["T204"] = {
        "pass": first(t204, "p1.merchant.interrupt") is not None and first(t204, "p1.merchant.cancel") is not None
        and count(t204, "p1.merchant.commit") == 0 and first(t204, "p1.growth.open") is not None,
        "interrupt": (first(t204, "p1.merchant.interrupt") or {}).get("reason"),
        "cancelConsumed": (first(t204, "p1.merchant.cancel") or {}).get("consumed"),
    }
    t201 = by_tc(ev, "P1-PLAY-T201")
    held = first(t201, "player.deathHeld")
    ring_closed = first(t201, "ring.close") or first(t201, "ring.state")
    c["T201"] = {
        "pass": held is not None and count(t201, "player.death") == 0 and first(t201, "p1.load") is not None
        and count(t201, "p1.level.up") >= 1 and first(t201, "p1.growth.open") is not None,
        "deathHeldTick": held and held["simulationTick"], "deaths": count(t201, "player.death"),
        "ringCloseReason": ring_closed and ring_closed.get("reason"),
        "levelUp": [(e.get("level"), e.get("maxHp")) for e in t201 if e["type"] == "p1.level.up"],
        "hpSetAfter": [e.get("hpAfter") for e in t201 if e["type"] == "player.hpSet"][-1:],
    }
    t202 = by_tc(ev, "P1-PLAY-T202")
    grant = [e for e in t202 if e["type"] == "p1.exp.grant"]
    shop_try = marker_result(t202, "shop")
    buys = [e for e in t202 if e["type"] == "p1.merchant.buy"]
    ld = first(t202, "p1.load")
    c["T202"] = {
        "pass": bool(grant) and grant[0].get("newLevels") == 2 and grant[0].get("lv50CurrencyGain") == 30
        and shop_try is not None and "GrowthPending" in str(shop_try.get("result")) and len(buys) == 1 and ld is not None
        and ld.get("lv50Currency") == 30,
        "firstGrant": grant and {k: grant[0].get(k) for k in ("newLevels", "lv50Currency", "lv50CurrencyGain")},
        "shopWhilePending": shop_try and shop_try.get("result"), "buys": [(e.get("reason"), e.get("price")) for e in buys],
        "afterLoad": ld and {k: ld.get(k) for k in ("level", "lv50Currency")},
    }
    t203 = by_tc(ev, "P1-PLAY-T203")
    c["T203"] = {
        "pass": count(t203, "p1.merchant.exitConfirm") >= 2 and count(t203, "p1.merchant.exitCancel") == 1
        and first(t203, "p1.merchant.depart") is not None and first(t203, "p1.merchant.error") is not None
        and count(t203, "p1.merchant.roll") == 0,
        "exitAsks": count(t203, "p1.merchant.exitConfirm"), "cancel": count(t203, "p1.merchant.exitCancel"),
        "error": (first(t203, "p1.merchant.error") or {}).get("reason"),
        "reentries": [e.get("region") for e in t203 if e["type"] == "p1.merchant.reenter"],
    }
    s = by_tc(ev, "P1-PLAY-SPELLS")
    c["spells"] = {
        "pass": {e.get("spellId") for e in s if e["type"] == "p1.magic.commit"} >= {"spell.fire_wall", "spell.archangel", "spell.shield", "spell.light_orb"},
        "commits": [(e.get("spellId"), e.get("mpCost")) for e in s if e["type"] == "p1.magic.commit"],
    }
    out["summary"] = {k: v["pass"] for k, v in c.items()}
    print(json.dumps(out, ensure_ascii=False, indent=1, default=str))


if __name__ == "__main__":
    main(sys.argv[1])
