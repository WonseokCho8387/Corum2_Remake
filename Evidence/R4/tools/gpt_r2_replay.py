"""Replays GPT's R2 review controls against the working-tree checkers (read-only on GPT files).
Prior controls: WP-06~08 R1 review.py and WP-10 review.py portions, with the same tick substitutions GPT's audit.py made.
New controls: the nine in audit.py §4 (the control() calls) and dodge_control.py (6, 7)."""
from pathlib import Path
import copy, json, re, textwrap, types
G = Path("D:/ProjectR/Corum2/docs/GPT/evidence")
repo = Path("D:/ProjectR/Corum2Remake")
def by(es, kind): return [e for e in es if e["type"] == kind]
def first(es, kind): return by(es, kind)[0]
def short(e): return {k: v for k, v in e.items() if k not in ("dataVersion", "runId", "realTime", "latency", "testCaseId", "enemies")}
def module(n):
    m = types.ModuleType("checker"); exec(compile((repo / f"Tools/wp{n}_checks.py").read_text(encoding="utf-8"), "checker", "exec"), m.__dict__); return m
def portion(folder, start, end):
    s = (G / folder / "review.py").read_text(encoding="utf-8"); return textwrap.dedent(s[s.index(start):s.index(end)])
result = {}
for n in ("06", "07", "08", "10"):
    pkg = repo / "Evidence" / ("WP-" + n + ("-R1" if n == "10" else "-R2"))
    files = sorted((pkg / "runs").glob("RUN*.ndjson"))
    logs = [[json.loads(l) for l in f.read_text(encoding="utf-8").splitlines()] for f in files]
    main = logs[0]; mod = module(n); seg = mod.segments(main)
    rec = result[n] = {"new_controls": []}
    if n != "10":
        old = portion("WP-06to08-R1_GPT_v1.0", '    rec["new_controls"]=[]', '    rec["trace"]')
        if n == "07":
            h = next(e for e in by(seg["WakeupEndBoundary"], "hit") if e.get("attackId") == "ground1")
            old = old.replace("3696", str(h["simulationTick"] - 1))
        env = dict(rec={}, n=n, copy=copy, re=re, mod=mod, main=main, by=by, first=first)
        exec(old, env); rec["prior"] = env["rec"]["new_controls"]
    else:
        old = portion("WP-10_GPT_v1.0", "def mutate(i,key,name,case,edit):", "reg={}")
        land = by(seg["DashJumpDiveWithInputs"], "motor.land")[-1]["simulationTick"]
        gt = next(e["simulationTick"] for e in by(seg["DashJumpDiveWithInputs"], "attack.start") if e["attackId"] == "ground1" and e["simulationTick"] >= land)
        old = old.replace("==561", "==" + str(gt)).replace('actualPass=rec["pass"]', 'actualPass=rec.get("observedPartsPass",rec["pass"])')
        env = dict(logs=logs, chk=mod, r={}, by=by, copy=copy)
        exec(old, env); rec["prior"] = env["r"]["mutations"]
    def control(tc, section, kind, pred, edit, caption):
        rows = copy.deepcopy(main); es = mod.segments(rows)[section]
        row = next(e for e in by(es, kind) if pred(e, es))
        before = short(row); edit(row); assert before != short(row)
        got = (mod.check_main if n == "10" else mod.check)(rows)[tc]["pass"]
        rec["new_controls"].append(dict(tc=tc, section=section, case=caption, expectedPass=False, actualPass=got))
    def anyrow(e, s): return True
    if n == "06":
        control("TC-05-01", "AirDive_Jumpmid", "tick", lambda e, s: e["simulationTick"] == next(a["simulationTick"] for a in by(s, "attack.start") if a["attackId"] == "dive") + 5, lambda e: e.update(y=e["y"] + 3), "vertical excursion during locked dive")
    elif n == "07":
        control("TC-06-06", "MagicWeak_mw", "enemy.knockback", anyrow, lambda e: (e.pop("attribution"), e.update(firstAttackInstanceId=-999)), "missing attribution plus unrelated attack")
    elif n == "08":
        control("TC-06-12", "ArmorWeakMagic", "enemy.knockback", anyrow, lambda e: e.update(blocked=True), "blocked interval labelled SingleSource")
        control("WP08-F02-R1", "DoubleLethalHit", "hit.player.ignored", anyrow, lambda e: e.update(actorId="never_spawned", stateAfter="HitStun"), "unknown second attacker and forbidden reaction")
    else:
        control("TC-INT-04", "BossArmorWeakMagic", "enemy.knockback", anyrow, lambda e: e.update(velocitySets=2, firstSource="Parry"), "multiple velocity sets and conflicting source")
        control("TC-INT-06", "SentinelPhase2Pattern", "boss.pattern", lambda e, s: e["actorId"] == "sn" and e["stateBefore"] == "s2", lambda e: e.update(stateAfter="not_in_phase_table"), "attack paid to an undeclared pattern")
        control("TC-INT-06", "TwoBossTransitions", "boss.phase", lambda e, s: e["stateAfter"] == "p3", lambda e: e.update(condition="Event:unrelated"), "incorrect warden p3 trigger")
        # dodge_control.py
        rows = copy.deepcopy(main); d = next(e for e in rows if e["type"] == "dodge" and e["targetId"] == "wb4"); d["enemyAttackInstanceId"] = 90000
        rec["new_controls"].append(dict(tc="TC-INT-04", case="dodge names another attack instance", expectedPass=False, actualPass=mod.check_main(rows)["TC-INT-04"]["pass"]))
        rows = copy.deepcopy(main); d = next(e for e in rows if e["type"] == "dodge" and e["targetId"] == "wb4")
        t = next(e for e in rows if e["type"] == "tick" and e["simulationTick"] == d["simulationTick"]); t["hp"] -= 20
        rec["new_controls"].append(dict(tc="TC-INT-04", case="player HP drops on the dodge tick", expectedPass=False, actualPass=mod.check_main(rows)["TC-INT-04"]["pass"]))
    rec["priorAllRejected"] = all(x["actualPass"] is False for x in rec["prior"])
    rec["newAllRejected"] = all(x["actualPass"] is False for x in rec["new_controls"])
print(json.dumps(result, ensure_ascii=False, indent=1, default=str))
