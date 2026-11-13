"""Replays GPT's WP-11A review probes (docs/GPT/evidence/WP-11A_GPT_v1.0/probes.py; edit functions copied verbatim) against the
working-tree Tools/wp11_checks.py on the WP-11A main run. Expected: all ten rejected, the original passing. Read-only on GPT files."""
from pathlib import Path
import copy, importlib.util, json, os, re

repo = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("wp11_checks", repo / "Tools/wp11_checks.py")
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
main = repo / "Evidence/WP-11A/runs/RUN-20261113-111800.ndjson"
m.DATA_DIRS[:] = [str(main.parent)]
original = m.load(str(main)); results = []
def run(tc,sc,description,edit):
    es=copy.deepcopy(original);s=m.segments(es)[sc]
    details=edit(es,s)
    assert es!=original
    results.append(dict(tc=tc,scenario=sc,case=description,expected=False,actual=m.check(es)[tc]['pass'],changed=details))
def field(kind,key,val,pred=lambda e:True):
    def update(es,s):
        e=next(x for x in m.of(s,kind) if pred(x));old=e.get(key);e[key]=val
        return [e['eventId'],e['simulationTick'],key,old,val]
    return update
run('TC-05-04','H5Kb_Attack','accepted attack after stun attributed to an unrelated input',field('attack.start','inputEventId',90000))
run('TC-08-02','A2Ready','cooldown block reason is an unrelated constraint',field('ai.blocked','reason','OutOfRange'))
def remove_start(es,s):
    e=next(e for e in s if e['type']=='enemy.state' and e['actorId']=='jn1' and e['stateAfter']=='AttackStartup')
    es.remove(e);return [e['eventId'],e['reason']]
run('TC-08-05','J5NearIn','air attack decision without actual startup',remove_start)
def wrongphase(es,s):
    e=m.of(s,'tick')[-1];before=e['enemies'];e['enemies']=re.sub(r'(bb:[^|]+ ph:)b2',r'\g<1>b1',before)
    return [e['eventId'],m.enemy(e,'bb')]
run('TC-08-09','B9Duo','last observed phase contradicts transition',wrongphase)
run('TC-08-12','Gg2Phys','armor hit claims no damage',field('hit','hpAfter',1000,lambda e:e['targetId']=='xg2Phys'))
run('TC-08-12','Gg2Magic','knockback endpoint contradicts displacement',field('enemy.knockback','endX',0,lambda e:e['actorId']=='xg2Magic'))
run('TC-UNITY-02','U2WallEnemy','wall knockback distance is negative despite increasing x',field('enemy.knockback','displacement',-4))
def no_depth(es,s):
    vs=m.of(es,'view.planeCheck')
    for e in vs: es.remove(e)
    return [e['eventId'] for e in vs]
run('TC-UNITY-02','U2WallRun','remove all rendered depth evidence',no_depth)
def startup_hit(es,s):
    hit=m.of(s,'hit')[0];a=m.of(s,'attack.start')[0]
    old=hit['simulationTick'];hit['simulationTick']=a['simulationTick'];hit['motionProgress']=0
    es.remove(hit);es.insert(es.index(a)+1,hit)
    for i,e in enumerate(es,es[0]['eventId']):e['eventId']=i
    return {'oldTick':old,'newTick':hit['simulationTick'],'instance':hit['attackInstanceId'],'startupSeconds':0.06}
run('TC-UNITY-07','U7Multi','one ground1 hit moved to startup before activation',startup_hit)
run('TC-UNITY-07','U7Cancel','cancel event at a different tick from actual hit',field('magic.cancel','simulationTick',10080))
orig = {k: v["pass"] for k, v in m.check(original).items()}
print(json.dumps({"probes": results, "allRejected": all(r["actual"] is False for r in results), "original": orig}, indent=1, ensure_ascii=False, default=str))
