#!/usr/bin/env python3
"""WP-04 boss phase transition and defense checks (WP04-M03 replacement for the transition part of r1_checks.py).

Usage:
  python Tools/wp04_transition_checks.py RUN.ndjson              # checks for one log (JSON on stdout)
  python Tools/wp04_transition_checks.py RUN.ndjson --selftest   # also run the mutation self-test on that log

Transitions are linked in log order (eventId), never by tick alone:
  - a transition opens at a boss.phase event with a non-empty stateBefore (the start phase entry has stateBefore "");
  - it closes at the FIRST later boss.transitionEnd / boss.transitionCancel of the same actor whose "phase" equals the
    phase entered; an end/cancel with another phase, or with no open transition, is reported as unmatched;
  - a new boss.phase of the same actor while one is open marks the open one "superseded" (no end);
  - an open transition at the end of the log is "incomplete".
For each transition:
  complete             true only when closed by its own end/cancel event
  actions_before_end   attack starts (enemy.state -> AttackStartup) and boss.defense Guard/Evade of that actor
                       between start and close, in log order
  no_action_before_end true / false when complete; null (not decided) when incomplete or superseded
"""
import copy
import json
import sys


def load(path):
    return [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]


def transitions(ev):
    ev = sorted(ev, key=lambda e: e["eventId"])
    open_ = {}
    out = []
    unmatched = []

    def close(actor, status, end=None):
        t = open_.pop(actor)
        t["status"] = status
        if end is not None:
            t["end_tick"] = end["simulationTick"]
            t["end_eventId"] = end["eventId"]
            t["end_type"] = end["type"]
            t["end_reason"] = end["reason"]
        t["complete"] = status in ("ended", "cancelled")
        t["no_action_before_end"] = (len(t["actions_before_end"]) == 0) if t["complete"] else None
        out.append(t)

    for e in ev:
        a = e["actorId"]
        if e["type"] == "boss.phase" and e["stateBefore"] != "":
            if a in open_:
                close(a, "superseded")
            open_[a] = {"actor": a, "from": e["stateBefore"], "to": e["stateAfter"], "condition": e.get("condition"),
                        "start_tick": e["simulationTick"], "start_eventId": e["eventId"], "transitionTime": e.get("transitionTime"),
                        "end_tick": None, "end_eventId": None, "end_type": None, "end_reason": None,
                        "hits_inside": [], "actions_before_end": []}
        elif e["type"] in ("boss.transitionEnd", "boss.transitionCancel"):
            t = open_.get(a)
            if t is not None and e.get("phase") == t["to"]:
                close(a, "ended" if e["type"] == "boss.transitionEnd" else "cancelled", e)
            else:
                unmatched.append({"eventId": e["eventId"], "tick": e["simulationTick"], "actor": a, "type": e["type"], "phase": e.get("phase"),
                                  "why": "no open transition" if t is None else "phase %r != open %r" % (e.get("phase"), t["to"])})
        elif a in open_:
            t = open_[a]
            if e["type"] == "enemy.state" and e["stateAfter"] == "AttackStartup":
                t["actions_before_end"].append({"eventId": e["eventId"], "tick": e["simulationTick"], "kind": "AttackStartup", "reason": e["reason"]})
            elif e["type"] == "boss.defense" and e["stateAfter"] in ("Guard", "Evade"):
                t["actions_before_end"].append({"eventId": e["eventId"], "tick": e["simulationTick"], "kind": e["stateAfter"]})
        if e["type"] == "hit" and e["targetId"] in open_:
            open_[e["targetId"]]["hits_inside"].append((e["simulationTick"], e["stateBefore"], e["stateAfter"], e["reason"]))
    for a in list(open_):
        close(a, "incomplete")
    out.sort(key=lambda t: t["start_eventId"])
    return out, unmatched


def defense(ev):
    d = [{"tick": e["simulationTick"], "actor": e["actorId"], "phase": e["stateBefore"], "pick": e["stateAfter"], "reason": e["reason"],
          "playerAttackInstanceId": e.get("playerAttackInstanceId"), "distance": round(e.get("distance", 0), 3),
          "guardAcc": e.get("guardAcc"), "evadeAcc": e.get("evadeAcc")} for e in ev if e["type"] == "boss.defense"]
    s = [{"tick": e["simulationTick"], "actor": e["actorId"], "state": e["stateBefore"], "reason": e["reason"],
          "playerAttackInstanceId": e.get("playerAttackInstanceId"), "distance": round(e.get("distance", 0), 3), "facing": e.get("facing")}
         for e in ev if e["type"] == "boss.defenseSkip"]
    return {"decisions": d, "skips": s}


def summary(ev):
    ts, un = transitions(ev)
    return {"transitions": ts, "unmatched_end_events": un,
            "all_complete": all(t["complete"] for t in ts),
            "all_no_action_before_end": all(t["no_action_before_end"] is True for t in ts) if ts else None,
            "defense": defense(ev)}


def selftest(ev):
    """Mutations of a real log; each must change the verdict the way named."""
    res = []
    base = summary(ev)
    res.append({"case": "original", "all_complete": base["all_complete"], "all_no_action_before_end": base["all_no_action_before_end"],
                "pass": base["all_complete"] is True and base["all_no_action_before_end"] is True and not base["unmatched_end_events"]})

    m = [e for e in ev if e["type"] not in ("boss.transitionEnd", "boss.transitionCancel")]
    s = summary(m)
    res.append({"case": "remove every end/cancel", "statuses": sorted({t["status"] for t in s["transitions"]}),
                "no_action_values": sorted({str(t["no_action_before_end"]) for t in s["transitions"]}),
                "pass": all(not t["complete"] and t["no_action_before_end"] is None for t in s["transitions"]) and s["all_no_action_before_end"] is False})

    ts = base["transitions"]
    if ts:
        t0 = ts[0]
        # attack start of that actor moved before its end
        m = copy.deepcopy(ev)
        mx = max(e["eventId"] for e in m)
        fake = {"type": "enemy.state", "eventId": t0["start_eventId"] + 0.5, "simulationTick": t0["start_tick"], "actorId": t0["actor"],
                "targetId": "", "stateBefore": "PhaseTransition", "stateAfter": "AttackStartup", "reason": "MUTATION"}
        m.append(fake)
        s = summary(m)
        t = next(x for x in s["transitions"] if x["start_eventId"] == t0["start_eventId"])
        res.append({"case": "attack start inserted before the end", "no_action_before_end": t["no_action_before_end"], "pass": t["no_action_before_end"] is False})
        # end with a different phase
        m = copy.deepcopy(ev)
        for e in m:
            if e["eventId"] == t0["end_eventId"]:
                e["phase"] = "WRONG"
        s = summary(m)
        t = next(x for x in s["transitions"] if x["start_eventId"] == t0["start_eventId"])
        res.append({"case": "own end carries another phase", "status": t["status"], "unmatched": len(s["unmatched_end_events"]),
                    "pass": t["end_eventId"] != t0["end_eventId"] and len(s["unmatched_end_events"]) >= 1})
        # duplicated end
        m = copy.deepcopy(ev)
        dup = copy.deepcopy(next(e for e in m if e["eventId"] == t0["end_eventId"]))
        dup["eventId"] = t0["end_eventId"] + 0.25
        m.append(dup)
        s = summary(m)
        res.append({"case": "duplicated end", "unmatched": len(s["unmatched_end_events"]), "pass": len(s["unmatched_end_events"]) == len(base["unmatched_end_events"]) + 1})
    # same-tick chain: an end of the previous phase at the same tick as the next start must not close the new one
    chain = [t for t in ts if any(u["actor"] == t["actor"] and u["end_tick"] == t["start_tick"] and u["end_eventId"] < t["start_eventId"] for u in ts)]
    res.append({"case": "same-tick chain links to its own later end", "examples": [(t["from"], t["to"], t["start_tick"], t["end_tick"]) for t in chain],
                "pass": all(t["end_eventId"] is None or t["end_eventId"] > t["start_eventId"] for t in ts) and all(t["end_tick"] != t["start_tick"] or t["transitionTime"] == 0 for t in chain)})
    return res


def main():
    path = sys.argv[1]
    ev = load(path)
    out = {"log": path.replace("\\", "/"), **summary(ev)}
    if "--selftest" in sys.argv:
        out["selftest"] = selftest(ev)
        out["selftest_all_pass"] = all(r["pass"] for r in out["selftest"])
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1, default=list)
    print()


if __name__ == "__main__":
    main()
