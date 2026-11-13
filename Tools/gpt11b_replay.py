"""Replays GPT's WP-11B review probes (docs/GPT/evidence/WP-11B_GPT_v1.0/audit.py; mutation lines copied verbatim) against the
working-tree Tools/wp11b_checks.py. Expected: all eleven rejected, and the original runs still passing. Read-only on GPT files."""
from pathlib import Path
import copy, importlib.util, json

repo = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("wp11b_checks", repo / "Tools/wp11b_checks.py")
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
runs = repo / "Evidence/WP-11B/runs"
paths = {k: runs / v for k, v in (l.split() for l in (runs / "RUN_NAMES.txt").read_text().splitlines())}
logs = {k: m.load(str(f)) for k, f in paths.items()}
out = {"original": {}, "probes": []}
for mode, keys in (("ops", list(paths)[:7]), ("time", list(paths)[7:])):
    names = [str(paths[k]) for k in keys]; rows = [logs[k] for k in keys]
    fn = m.check_ops if mode == "ops" else m.check_time
    out["original"][mode] = {k: v["pass"] for k, v in fn(names, rows).items()}
    # ---- verbatim from GPT audit.py
    def probe(key,tc,title,mut):
        index=keys.index(key);changed=list(rows);changed[index]=copy.deepcopy(rows[index])
        es=changed[index];s=m.segments(es);details=mut(es,s);assert es!=rows[index]
        out['probes'].append(dict(tc=tc,case=title,run=key,change=details,expected=False,actual=fn(names,changed)[tc]['pass']))
    def setfield(zone,kind,key,value,pred=lambda e:True):
        def mutate(es,s):
            e=next(e for e in m.of(s[zone],kind) if pred(e));a=e.get(key);e[key]=value
            return [e['eventId'],key,a,value]
        return mutate
    if mode=='ops':
        probe('v1','TC-OPS-01','wrong cause for locked jump',setfield('O1Jump','input.reject','reason','ActionLock:HitStun',lambda e:e.get('action')=='Jump'))
        probe('v5','TC-OPS-01','unrelated zone hit disconnected from its attack',setfield('O6Twins','hit','attackInstanceId',90000))
        probe('v5','TC-OPS-01','invented combo termination reason in twins',setfield('O6Twins','combo.end','reason','EnemyGuardCancel'))
        def missing_twin(es,s):
            e=m.of(s['O6Twins'],'tick')[4];e['enemies']='|'.join(v for v in e['enemies'].split('|') if not v.startswith('tb:'));return e['eventId']
        probe('base2','TC-UNITY-03','second twin missing from one rerun tick',missing_twin)
    else:
        probe('s50','TC-UNITY-06','routed jump reaches game as different action',setfield('C_Normal','input','action','SpecialAttack',lambda e:e.get('action')=='Jump'))
        probe('s60','TC-UNITY-06','delivery request time unrelated to route',setfield('C_Normal','input','requestTime',-1000,lambda e:e.get('action')=='Jump'))
        probe('s50','TC-UNITY-06','game delivery also claims ring ownership on route',setfield('C_Normal','input.route','consumer','ring',lambda e:e.get('action')=='Jump'))
        probe('s60','TC-UNITY-06','delayed attack start linked to unrelated input',setfield('C_Delay','attack.start','inputEventId',90000))
        probe('s60','TC-UNITY-06','delayed attack linked to existing Jump id',setfield('C_Delay','attack.start','inputEventId',1))
        probe('s50','TC-UNITY-05','impossible negative simulation cost',setfield('C_Normal','frame.sample','simCostMs',-99))
        probe('s60','TC-UNITY-05','wrong fixed step only during slow phase',lambda es,s:[e.update(fixedDeltaTime=.02) for e in m.of(s['C_Slow'],'frame.sample')] and 'slow fixed step -> .02')
        probe('s50','TC-UNITY-05','stop has a tick in its middle',setfield('C_Pause','frame.sample','ticksThisFrame',1,lambda e:e['timeScale']==0 and e['ticksThisFrame']==0))
    # ---- end verbatim
out["allRejected"] = all(p["actual"] is False for p in out["probes"])
out["originalAllPass"] = all(v for r in out["original"].values() for v in r.values())
print(json.dumps(out, indent=1, ensure_ascii=False, default=str))
