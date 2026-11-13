"""Replays GPT's WP-09-R3 review probes (docs/GPT/evidence/WP-09-R3_GPT_v1.0/probes.py; edit functions copied verbatim) against the
working-tree Tools/wp09_checks.py. GPT's `expected` is the Core's verdict for each case; every case should now match it.
Also runs the CLI sidecar cases (missing / other runId) and a manifest-hash case. Usage: python Tools/gpt09r3_replay.py"""
from pathlib import Path
import copy, hashlib, importlib.util, json, re, shutil, subprocess, sys, tempfile

repo = Path(__file__).resolve().parents[1]
tool = repo / "Tools/wp09_checks.py"
spec = importlib.util.spec_from_file_location("wp09_checks", tool)
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
pkg = repo / "Evidence/WP-09-R1/runs"; out = {"cases": [], "cli": []}

def case(mode, name, expected, edit):
    f = next(pkg.glob("*" + ("235402" if mode == "dev" else "235043") + ".ndjson"))
    es = m.load(str(f)); d = json.loads(f.with_suffix(".data.json").read_text()); m.CFG.clear(); m.CFG.update(d)
    s = m.segments(es, m.DEV_SCENARIOS if mode == "dev" else m.SLIDE_SCENARIOS)
    fn, tc = (m.dev_check, "TC-02-01") if mode == "dev" else (m.slide_check, "TC-03-04")
    info = edit(es, s, m.CFG)
    actual = fn(es)[tc]["pass"]
    out["cases"].append(dict(mode=mode, tc=tc, case=name, expected=expected, actual=actual, ok=actual is expected, details=info))

# ---- verbatim from GPT probes.py (d is the loaded data snapshot)
def deadzone(ring,inputdz,stick):
    def change(es,s,d):
        next(x['data'] for x in d['extra'] if x['asset']=='RingConfig')['cursorDeadzone']=ring
        d['input']['stickDeadzone']=inputdz
        sec=s['DevKb_RegisterUp']
        for e in sec:
            if e['type'] in ('ring.release','ring.cursor') and e['simulationTick']==233:e['stick']=[stick,0]
        return dict(ring=ring,input=inputdz,stick=stick)
    return change
case('dev','ring .2 input .35 fully tilted by ring rule',False,deadzone(.2,.35,.3))
case('dev','ring .5 input .35 neutral by ring rule',True,deadzone(.5,.35,.4))
case('dev','equal radial boundary at exactly representable .5',False,deadzone(.5,.5,.5))
def tilt(es,s,d):
    sec=s['DevKb_RegisterUp'];old=next(e for e in sec if e['type']=='ring.cursor' and e['stateAfter']=='-1')
    e=copy.deepcopy(old);a=next(e for e in sec if e['type']=='quickslot.assign')
    e.update(simulationTick=235,gameTime=next(t['gameTime'] for t in sec if t['type']=='tick' and t['simulationTick']==235),stick=[1,0],stateBefore='-1',stateAfter='2',reason='Sector',selectedSlot=2,selectedItem='herb',lastSimTick=235)
    es.insert(es.index(next(t for t in sec if t['type']=='tick' and t['simulationTick']==235)),e)
    for n,row in enumerate(es,es[0]['eventId']):row['eventId']=n
    a.update(stickNeutral=False,selectedSlot=2,selectedItem='herb')
    return dict(target=a['target'],selected=a['selectedItem'],assigned=a['stateAfter'],releaseThenTilt=True)
case('dev','fixed item assigned after neutral then tilt',True,tilt)
def delayed(es,s,d):
    sec=s['DevPad_UseQuickslots'];u=next(e for e in sec if e['type']=='item.use' and e['requestId']==5);es.remove(u)
    t=next(e for e in sec if e['type']=='tick' and e['simulationTick']==746)
    u.update(simulationTick=746,execTick=746,gameTime=t['gameTime'],execGameTime=t['gameTime'],realTime=t['realTime'],waitedFor='unsupported-wait')
    es.insert(es.index(t),u)
    for n,row in enumerate(es,es[0]['eventId']):row['eventId']=n
    return dict(request=744,execTick=746,waitedFor=u['waitedFor'],waitEvents=len([e for e in sec if e['type']=='item.wait' and e.get('requestId')==5]))
case('dev','NextTick delayed via invented wait reason',False,delayed)
def field(section,kind,key,value,pred=lambda e:True):
    def change(es,s,d):
        e=next(e for e in s[section] if e['type']==kind and pred(e));before=e.get(key);e[key]=value
        return dict(id=e['eventId'],key=key,before=before,after=value)
    return change
case('dev','absent post-discard snapshot treated as zero',False,field('DevPad_Discard','ring.discard','inventory',None))
case('slide','deliveredTick contradicts consumed Jump start',False,field('SlideLeftKb','input','deliveredTick',90000,lambda x:x.get('inputEventId')==17))
def skip_restore(es,s,d):
    rr=[e for e in es if e['reason']=='TestSetItemCount' and e['simulationTick']==633]
    pre=next(e for e in s['DevKb_Discard'] if e['type']=='ring.discard')['inventory']
    inv={k:int(v) for k,v in [x.rsplit('x',1) for x in pre.split(',')]}
    order=[next(e for e in rr if e['targetId']=='elixir')]+[e for e in rr if e['targetId']!='elixir']
    for old,new in zip(rr,copy.deepcopy(order)):
        inv[new['targetId']]=new['countAfter'];old.update(targetId=new['targetId'],countBefore=new['countBefore'],countAfter=new['countAfter'],inventory=','.join(k+'x'+str(v) for k,v in inv.items()))
    return {'discarded':'elixir','firstLaterSnapshot':rr[0]['reason'],'afterRestore':rr[0]['countAfter'],'scope':'discard-to-next-snapshot predicate'}
case('dev','legitimate item replenishment as next snapshot (predicate)',True,skip_restore)
# ---- end verbatim

def cli(mode, sfx, state, setup):
    with tempfile.TemporaryDirectory() as td:
        p = Path(td); orig = next(pkg.glob("*" + sfx + ".ndjson")); f = p / orig.name; f.write_bytes(orig.read_bytes())
        setup(orig, f)
        r = json.loads(subprocess.check_output([sys.executable, "-B", str(tool), "--" + mode, str(f)]))
        actual = next(v["pass"] for k, v in r.items() if k.startswith("TC-"))
        exp = state == "own snapshot + manifest"
        out["cli"].append(dict(mode=mode, sidecar=state, snapshot=r["dataSnapshot"]["status"], expected=exp, actual=actual, ok=actual is exp))

def other_run(orig, f):
    d = json.loads(orig.with_suffix(".data.json").read_text()); d["runId"] = "OTHER-RUN"; f.with_suffix(".data.json").write_text(json.dumps(d))
def own(orig, f):
    for ext in (".data.json", ".manifest.yaml"): shutil.copy(orig.with_suffix(ext), f.with_suffix(ext))
def edited(orig, f):  # same runId, one value changed: the manifest hash no longer matches
    own(orig, f); d = json.loads(orig.with_suffix(".data.json").read_text()); d["movement"]["slideSpeed"] = 90; f.with_suffix(".data.json").write_text(json.dumps(d))
for mode, sfx in (("dev", "235402"), ("slide", "235043")):
    cli(mode, sfx, "missing", lambda o, f: None)
    cli(mode, sfx, "different runId", other_run)
    cli(mode, sfx, "snapshot edited after the manifest", edited)
    cli(mode, sfx, "own snapshot + manifest", own)
out["allMatch"] = all(x["ok"] for x in out["cases"] + out["cli"])
print(json.dumps(out, indent=1, ensure_ascii=False, default=str))
