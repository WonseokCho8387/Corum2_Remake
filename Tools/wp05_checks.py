#!/usr/bin/env python3
"""WP-05 run checks (R1/R2: WP05-M02): magic commands (R-02-02 / TC-02-02), command vs scroll (R-02-10 / TC-02-10) and the
death flow (R-06-19 / TC-06-19a) from an NDJSON run log. Facts plus per-item pass flags; not a GPT verdict.

python Tools/wp05_checks.py RUN.ndjson [--expect-selects A,B,C] [--selftest]

Linking is by ids and event order, never by "the next event with the same source":
  cast -> effect -> hit by attackInstanceId; scroll item.use by requestId; death -> flow stages in eventId order.
A section passes only if its required records exist: every matched command has a cast, an effect and (attack magic) a
hit of that cast, in that order; every miss starts a known normal action; every scroll cast has its accepted item.request,
magic.request and exactly one item.use with the same requestId, after the cast, with countAfter = count - 1 and a cooldown; each command/scroll pair compares two complete casts; every death
has a motion of the configured length and the expected next stage; every game over is followed by a selection (and,
with --expect-selects, exactly that list in order); no enemy attack starts after a death until the revive.
"""
import copy
import json
import sys


def load(path):
    return sorted((json.loads(l) for l in open(path, encoding="utf-8") if l.strip()), key=lambda e: e["eventId"])


# Every outcome the game returns for a press that starts (or reserves) a normal action. Anything else (rejected:,
# notImplemented, magic:, an unknown string) is not proof that a normal action ran (WP-05-R1 review section 3).
NORMAL_EXACT = {"guard", "buffered", "reservedForLanding", "extraJump", "airDash", "dive", "slide", "roll"}
NORMAL_PREFIX = ("attack:", "jump:")


def is_normal(res):
    return res is not None and (res in NORMAL_EXACT or any(str(res).startswith(p) for p in NORMAL_PREFIX))


def tick_dt(ev):
    for e in ev:
        if e.get("simulationTick") and e.get("gameTime"):
            return e["gameTime"] / e["simulationTick"]
    return None


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
        """Effects and hits of this cast, plus whether they come in the order cast -> effect -> hit (eventId and tick)."""
        aid = c.get("attackInstanceId")
        eff = by_attack.get(("magic.effect", aid), [])
        hits = [h for h in by_attack.get(("hit", aid), []) if h.get("magicId")]
        ordered = all(e["eventId"] > c["eventId"] and e["simulationTick"] >= c["simulationTick"] for e in eff)
        if eff:
            ordered = ordered and all(h["eventId"] > eff[0]["eventId"] and h["simulationTick"] >= eff[0]["simulationTick"] for h in hits)
        else:
            ordered = ordered and not hits
        return eff, hits, ordered

    # ---- TC-02-02
    outcome = {e["inputEventId"]: e["reason"] for e in of("input") if e.get("edge") == "Press" and e.get("inputEventId") is not None}
    cmds = []
    for m in of("magic.command"):
        tick = m["simulationTick"]
        cast = next((c for c in of("magic.cast") if c["eventId"] > m["eventId"] and c["simulationTick"] == tick
                     and c.get("source") == "command:" + str(m.get("commandId"))), None)
        # the game's own outcome for this button press (the "input" event with the same inputEventId)
        res = outcome.get(m.get("inputEventId"))
        eff, hits, ordered = cast_chain(cast) if cast else ([], [], False)
        is_attack = bool(eff) and str(eff[0]["reason"]).startswith("AttackActive")
        row = {"tick": tick, "command": m.get("commandId"), "magic": m.get("magicId"), "history": m.get("history"),
               "competingNormalAction": m.get("competingNormalAction"), "attackInstanceId": cast.get("attackInstanceId") if cast else None,
               "effect": [(e["simulationTick"], e["reason"]) for e in eff], "hits": [(h["simulationTick"], h.get("damageD"), h["reason"]) for h in hits],
               "pressOutcome": res, "ordered": ordered}
        row["pass"] = (cast is not None and len(eff) == 1 and ordered and res == "magic:" + str(m.get("commandId"))
                       and (not is_attack or len(hits) >= 1))
        cmds.append(row)
    misses = []
    for m in of("magic.commandMiss"):
        tick = m["simulationTick"]
        res = outcome.get(m.get("inputEventId"))
        normal = is_normal(res)
        no_cast = not [c for c in of("magic.cast") if c["simulationTick"] == tick]
        misses.append({"tick": tick, "history": m.get("history"), "normalAction": m.get("normalAction"), "pressOutcome": res,
                       "noCast": no_cast, "pass": normal and no_cast})
    out["TC-02-02"] = {"commands": cmds, "misses": misses,
                       "buffed_physical_hits": [(h["simulationTick"], h.get("damageD"), h.get("buffMultiplier")) for h in of("hit") if h.get("buffMultiplier", 1) not in (1, 1.0)],
                       "fallbacks": [(e["simulationTick"], e.get("reject")) for e in of("magic.commandFallback")],
                       "pass": bool(cmds) and all(c["pass"] for c in cmds) and bool(misses) and all(m["pass"] for m in misses)}

    # ---- TC-02-10
    rows = []
    num = (int, float)

    def scroll_link(c):
        """The scroll's own request and consumption, linked by requestId (the cast's inputEventId for a scroll) and order:
        item.request(accepted/reserved) -> magic.request -> magic.cast -> item.use, with the count and cooldown recorded."""
        rid = c.get("inputEventId")
        item_id = str(c.get("source", ""))[len("scroll:"):]
        req = next((r for r in of("item.request") if r.get("requestId") == rid and r["eventId"] < c["eventId"]
                    and r.get("targetId") == item_id and str(r["reason"]).startswith(("Accepted", "Reserved"))), None)
        mreq = next((r for r in of("magic.request") if r.get("requestId") == rid and r["eventId"] < c["eventId"]
                     and r["simulationTick"] == c["simulationTick"] and r.get("source") == c.get("source")), None)
        uses = [u for u in of("item.use") if u.get("requestId") == rid]
        use = uses[0] if len(uses) == 1 else None
        ok = (req is not None and mreq is not None and use is not None and use["eventId"] > c["eventId"]
              and use["simulationTick"] == c["simulationTick"] and use.get("targetId") == item_id
              and str(use["reason"]).startswith("magic:" + str(c.get("magicId")) + ":")
              and isinstance(use.get("countAfter"), num) and isinstance(use.get("cooldown"), num) and use["cooldown"] > 0
              and isinstance(req.get("count"), num) and use["countAfter"] == req["count"] - 1)
        return use, ok, {"requestId": rid, "itemRequest": req["eventId"] if req else None, "magicRequest": mreq["eventId"] if mreq else None,
                         "itemUses": [u["eventId"] for u in uses], "countBefore": req.get("count") if req else None, "pass": ok}

    for c in of("magic.cast"):
        eff, hits, ordered = cast_chain(c)
        use, link_ok, link = None, True, None
        if str(c.get("source", "")).startswith("scroll:"):
            use, link_ok, link = scroll_link(c)
        rows.append({"tick": c["simulationTick"], "magic": c.get("magicId"), "source": c.get("source"), "attackInstanceId": c.get("attackInstanceId"),
                     "power": c.get("power"), "castTime": c.get("castTime"),
                     "effectDelayTicks": eff[0]["simulationTick"] - c["simulationTick"] if len(eff) == 1 else None,
                     "hitDamage": hits[0].get("damageD") if hits else None, "ordered": ordered,
                     "itemUse": (use["simulationTick"], use.get("countAfter"), use.get("cooldown")) if use else None, "scrollLink": link,
                     "complete": len(eff) == 1 and bool(hits) and ordered and link_ok})
    pairs = []
    for mid in sorted({r["magic"] for r in rows if r["magic"]}):
        cmd = [r for r in rows if r["magic"] == mid and str(r["source"]).startswith("command:") and r["complete"]]
        scr = [r for r in rows if r["magic"] == mid and str(r["source"]).startswith("scroll:") and r["complete"]]
        if cmd and scr:
            a, b = cmd[-1], scr[-1]
            pairs.append({"magic": mid, "command": a, "scroll": b,
                          "pass": a["power"] == b["power"] and a["castTime"] == b["castTime"] and a["effectDelayTicks"] == b["effectDelayTicks"]
                          and a["hitDamage"] == b["hitDamage"]})
    # R3: every scroll cast that started must have a valid consumption link, hit or not (not only the compared pair).
    scroll_links = [{"tick": r["tick"], "source": r["source"], "pass": r["scrollLink"]["pass"]} for r in rows if r["scrollLink"] is not None]
    links_ok = all(s["pass"] for s in scroll_links)
    out["TC-02-10"] = {"casts": rows, "command_vs_scroll": pairs, "scroll_links": scroll_links, "scroll_links_all_pass": links_ok,
                       "scroll_waits": [(e["simulationTick"], e["reason"]) for e in of("item.request") if "EXP-ITEM-07" in e["reason"]],
                       "scroll_cancels": [(e["simulationTick"], e["reason"], e.get("consumed")) for e in of("item.cancel")],
                       "pass": bool(pairs) and all(p["pass"] for p in pairs) and links_ok}

    # ---- TC-06-19a
    deaths = []
    flows = of("death.flow")
    dt = tick_dt(ev)
    for d in of("player.death"):
        t, eid = d["simulationTick"], d["eventId"]
        motion = next((f for f in flows if f["eventId"] > eid and f["stateAfter"] == "Motion"), None)
        nxt = next((f for f in flows if motion and f["eventId"] > motion["eventId"] and f["stateBefore"] == "Motion"), None)
        after = next((f for f in flows if nxt and nxt["stateAfter"] == "RevivePrompt" and f["eventId"] > nxt["eventId"] and f["stateBefore"] == "RevivePrompt"), None)
        revive = next((r for r in of("player.revive") if r["eventId"] > eid), None)
        end_eid = revive["eventId"] if revive and after and after["stateAfter"] == "None" else 10 ** 12
        attacks = [e for e in ev if e["type"] == "enemy.state" and e["stateAfter"] == "AttackStartup" and eid < e["eventId"] < end_eid]
        motion_ticks = (nxt["simulationTick"] - motion["simulationTick"]) if motion and nxt else None
        cfg = motion.get("motionTime") if motion else None
        expected_ticks = int(round(cfg / dt)) if isinstance(cfg, (int, float)) and cfg > 0 and dt else None
        expected = "RevivePrompt" if (motion and (motion.get("reviveItems") or 0) > 0) else "GameOver"
        row = {"deathTick": t, "motionTick": motion["simulationTick"] if motion else None,
               "motionTicks": motion_ticks, "motionTime": cfg, "expectedMotionTicks": expected_ticks,
               "reviveItemsAtDeath": motion.get("reviveItems") if motion else None, "afterMotion": nxt["stateAfter"] if nxt else None,
               "promptResult": (after["stateAfter"], after["reason"]) if after else None,
               "revive": (revive["simulationTick"], revive.get("hpBefore"), revive.get("hpAfter")) if revive and after and after["stateAfter"] == "None" else None,
               "enemyAttackStartsWhileDead": len(attacks)}
        row["pass"] = (motion is not None and motion["simulationTick"] == t and nxt is not None and row["afterMotion"] == expected
                       and expected_ticks is not None and motion_ticks == expected_ticks
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
    # ---- WP-05-R1 review counterexamples (R2): each must fail its section
    scroll_uses = [u for u in ev if u["type"] == "item.use" and str(u["reason"]).startswith("magic:")]

    def shift_request_ids(m):
        for e in m:
            if e["type"] == "item.use" and str(e["reason"]).startswith("magic:"):
                e["requestId"] = e["requestId"] + 90000
    mutate("scroll item.use requestId moved to another request", "TC-02-10", shift_request_ids, bool(scroll_uses) and base["TC-02-10"]["pass"])

    def drop_count_cooldown(m):
        for e in m:
            if e["type"] == "item.use" and str(e["reason"]).startswith("magic:"):
                e.pop("countAfter", None)
                e.pop("cooldown", None)
    mutate("scroll item.use countAfter and cooldown removed", "TC-02-10", drop_count_cooldown, bool(scroll_uses) and base["TC-02-10"]["pass"])

    fire_cmd_ids = {c.get("attackInstanceId"): c for c in fire_casts if str(c.get("source", "")).startswith("command:")}

    def hit_before_cast(m):
        for e in m:
            c = fire_cmd_ids.get(e.get("attackInstanceId"))
            if e["type"] == "hit" and e.get("magicId") and c is not None:
                e["eventId"] = c["eventId"] - 0.5
                e["simulationTick"] = c["simulationTick"]
    mutate("fire hits moved before their cast", "TC-02-02", hit_before_cast,
           any(e["type"] == "hit" and e.get("attackInstanceId") in fire_cmd_ids for e in ev) and base["TC-02-02"]["pass"])

    def motion_zero(m):
        first = next(e for e in m if e["type"] == "player.death")
        mo = next(f for f in m if f["type"] == "death.flow" and f["eventId"] > first["eventId"] and f["stateAfter"] == "Motion")
        nx = next(f for f in m if f["type"] == "death.flow" and f["eventId"] > mo["eventId"] and f["stateBefore"] == "Motion")
        nx["simulationTick"] = first["simulationTick"]
    mutate("first death motion end at the death tick", "TC-06-19a", motion_zero, d0 is not None and base["TC-06-19a"]["pass"])

    miss_ids = {e.get("inputEventId") for e in ev if e["type"] == "magic.commandMiss"}

    def unknown_outcome(m):
        for e in m:
            if e["type"] == "input" and e.get("edge") == "Press" and e.get("inputEventId") in miss_ids:
                e["reason"] = "unknown_outcome"
                break
    mutate("first miss press outcome unknown_outcome", "TC-02-02", unknown_outcome, bool(miss_ids) and base["TC-02-02"]["pass"])
    # ---- WP-05-R2 review counterexamples (R3): one record of the LAST scroll cast only (in main, the unhit cast at 1020),
    # leaving the compared pair intact; each must fail TC-02-10.
    last_use = scroll_uses[-1] if len(scroll_uses) >= 2 else None
    mutate("last scroll item.use removed (other scroll intact)", "TC-02-10",
           lambda m: m.__setitem__(slice(None), [e for e in m if e.get("eventId") != last_use["eventId"]]),
           last_use is not None and base["TC-02-10"]["pass"])

    def shift_last_request_id(m):
        for e in m:
            if e.get("eventId") == last_use["eventId"]:
                e["requestId"] = e["requestId"] + 90000
    mutate("last scroll item.use requestId +90000 (other scroll intact)", "TC-02-10", shift_last_request_id,
           last_use is not None and base["TC-02-10"]["pass"])
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
