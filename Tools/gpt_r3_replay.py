"""Replays GPT's R3 review probes (docs/GPT/evidence/R3_GPT_v1.0/probes.py; transform functions copied verbatim) against the
working-tree checkers. Expected: the nine counterexamples are rejected, the HP-threshold control passes. Read-only on GPT files."""
from pathlib import Path
import copy, importlib.util, json

repo = Path(__file__).resolve().parents[1]
def readlog(f): return [json.loads(l) for l in f.read_text(encoding="utf-8").splitlines()]
def by(es, kind): return [e for e in es if e["type"] == kind]
def first(es, kind): return by(es, kind)[0]
def module(n):
    spec = importlib.util.spec_from_file_location(f"wp{n}_checks", repo / f"Tools/wp{n}_checks.py")
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m

results = []
def probe(tc, label, transform, expected=False):
    es = copy.deepcopy(main)
    details = transform(es, mod.segments(es))
    assert es != main, label
    ans = check(es)
    results.append(dict(wp=n, tc=tc, case=label, expected=expected, actual=ans[tc]["pass"], ok=ans[tc]["pass"] is expected,
                        mode=ans.get("knockbackDiagnostics"), changes=details))
def preview(e): return {k: e[k] for k in ("eventId", "simulationTick", "type", "actorId", "reason", "attackInstanceId", "inputEventId", "targetId", "stateAfter") if k in e}

# ---- verbatim from GPT probes.py
def wrong_jump(es,seg):
    e=next(e for e in seg["AirAirDash_Jumpmid"] if e["type"]=="air.use" and e["reason"]=="ExtraJump")
    before=preview(e);e["reason"]="AirDash"
    return [before,preview(e)]
def remove_schema(es,seg):
    ks=by(es,"enemy.knockback")
    for k in ks:k.pop("attribution")
    wk={"07":"MagicWeak_mw","08":"ArmorWeakMagic","10":"BossArmorWeakMagic"}[n]
    k=first(seg[wk],"enemy.knockback");k["blocked"]=True
    return dict(stripped=len(ks),wrongBlockedId=k["eventId"],remainingFields=[f for f in mod.ATTR_FIELDS if f in k])
def late_active(es,seg):
    s=seg["DoubleLethalHit"];ig=first(s,"hit.player.ignored")
    en=next(e for e in by(s,"enemy.state") if e["actorId"]==ig["actorId"] and e.get("attackInstanceId")==ig["enemyAttackInstanceId"] and e["stateAfter"]=="AttackActive")
    old=en["eventId"];es.remove(en);es.insert(es.index(ig)+1,en)
    for i,e in enumerate(es,start=es[0]["eventId"]):e["eventId"]=i
    return dict(activeOldId=old,ignoredId=ig["eventId"],activeNewId=en["eventId"],tick=ig["simulationTick"])
def wrong_dodge_hp(es,seg):
    d=first(seg["BossArmorSimultaneous"],"dodge");d.pop("playerHp")
    for t in by(es,"tick"):
        if t["simulationTick"] in (d["simulationTick"]-1,d["simulationTick"]):t.pop("hp")
    return dict(dodge=d["eventId"],missingHpTicks=[d["simulationTick"]-1,d["simulationTick"]])
def other_event(es,seg):
    e=next(e for e in by(es,"boss.event") if e.get("event")=="shift")
    e.update(event="shift_other",reason="Event:shift_other(R-08-10)")
    return preview(e)
def wrong_threshold(es,seg):
    rec=next(e for e in by(seg["TwoBossTransitions"],"boss.phase") if e["stateAfter"]=="p2")
    orig=rec["condition"];rec["condition"]="HpBelow:0.99"
    return dict(tick=rec["simulationTick"],before=orig,after=rec["condition"],ratio=rec["hpRatio"])
def p2_after_p3(es,seg):
    a=next(e for e in by(es,"enemy.state") if e["actorId"]=="wb2" and e["reason"]=="Pattern:shot2" and e["simulationTick"]==2009)
    old=preview(a);es.remove(a)
    q=next(e for e in by(es,"tick") if e["simulationTick"]==2229)
    es.insert(es.index(q),a);a["simulationTick"]=2229
    for i,e in enumerate(es,start=es[0]["eventId"]):e["eventId"]=i
    return [old,preview(a)]
def replace_threshold_equality(es,seg):
    a=next(e for e in by(seg["TwoBossTransitions"],"boss.phase") if e["stateAfter"]=="p2")
    a["hpRatio"]=0.6;a["hp"]=120
    return dict(tick=a["simulationTick"],condition=a["condition"],ratio=a["hpRatio"],hp=a["hp"],scope="condition predicate only")
for n in ("06","07","08","10"):
    pkg=repo/"Evidence"/("WP-"+n+("-R1" if n=="10" else "-R2"))
    main=readlog(sorted((pkg/"runs").glob("RUN*.ndjson"))[0]);mod=module(n);check=mod.check_main if n=="10" else mod.check
    if n=="06":probe("TC-05-01","after-end Jump credited as AirDash",wrong_jump)
    else:probe({"07":"TC-06-06","08":"TC-06-12","10":"TC-INT-04"}[n],"R2 stripped discriminator with blocked hit",remove_schema)
    if n=="08":probe("WP08-F02-R1","second attacker becomes active after ignored event in same tick",late_active)
    if n=="10":
        probe("TC-INT-04","Dodge and both player HP samples missing",wrong_dodge_hp)
        probe("TC-INT-06","event with matching prefix but distinct name",other_event)
        probe("TC-INT-06","threshold disagrees with recorded phase configuration",wrong_threshold)
        probe("TC-INT-06","p2 shot2 preparation moved past p3 transition",p2_after_p3)
        probe("TC-INT-06","at HP threshold (predicate isolation)",replace_threshold_equality,True)
# ---- originals and the earlier legacy log
orig = {}
for n in ("06","07","08","10"):
    pkg=repo/"Evidence"/("WP-"+n+("-R1" if n=="10" else "-R2")); mod=module(n)
    main=readlog(sorted((pkg/"runs").glob("RUN*.ndjson"))[0]); a=(mod.check_main if n=="10" else mod.check)(main)
    orig[n]={k:v["pass"] for k,v in a.items() if isinstance(v,dict) and "pass" in v}
m7=module("07"); leg=m7.check(readlog(repo/"Evidence/WP-07-R1/runs/RUN-20261112-194039.ndjson"))
legacy={"mode":leg["knockbackDiagnostics"],"sections":{k:v["pass"] for k,v in leg.items() if isinstance(v,dict) and "pass" in v}}
print(json.dumps({"probes":results,"all_ok":all(r["ok"] for r in results),"originals":orig,"legacyWP07R1_194039":legacy},indent=1,ensure_ascii=False))
