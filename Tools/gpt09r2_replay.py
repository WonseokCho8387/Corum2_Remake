"""Replay GPT WP-09-R2 review section 2 controls (docs/GPT/evidence/WP-09-R2_GPT_v1.0/audit.py, probe functions copied verbatim)
against the current Tools/wp09_checks.py. Usage: python Tools/gpt09r2_replay.py <WP-09-R1 runs dir>"""
import copy, importlib.util, json, re, sys
from pathlib import Path

here = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("wp09_checks", here / "wp09_checks.py")
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
runs = Path(sys.argv[1])
files = {k: next(runs.glob("*" + n + ".ndjson")) for k, n in dict(main="234229", plane="234529", dev="235402", slide="235043").items()}
logs = {k: [json.loads(s) for s in f.read_text(encoding="utf-8").splitlines() if s.strip()] for k, f in files.items()}
def of(rows, kind): return [r for r in rows if r["type"] == kind]
def first(rows, kind): return of(rows, kind)[0]
out = []

def probe(mode, tc, segment, label, change, expected=False):
    m.CFG.clear(); m.CFG.update(json.loads(Path(re.sub(r"\.ndjson$", ".data.json", str(files[mode]))).read_text(encoding="utf-8")))
    es = copy.deepcopy(logs[mode])
    fn, names = {"dev": (m.dev_check, m.DEV_SCENARIOS), "plane": (m.plane_check, []), "slide": (m.slide_check, m.SLIDE_SCENARIOS), "main": (m.check, m.SCENARIOS)}[mode]
    rows = m.segments(es, names)[segment] if segment else es
    change(rows)
    actual = fn(es)[tc]["pass"]
    out.append(dict(mode=mode, tc=tc, segment=segment, case=label, expected=expected, actual=actual, ok=actual is expected))

# --- verbatim from GPT audit.py
def enemy(s):return of(s,"view.planeCheck")[1]
def boss_teleport(s):
    e=enemy(s);e.update(teleportSamples=1,groundStillSamples=e["groundStillSamples"]-1,bossMovingSamples=e["groundMovingSamples"]+e["airborneSamples"]+1)
def wrong_release(s):first(s,"ring.release").update(stick=[1,0])
def premature_use(s):
    q=first(s,"item.request");u=next(x for x in of(s,"item.use") if x["requestId"]==q["requestId"])
    u.update(simulationTick=q["simulationTick"]-1,execTick=q["simulationTick"]-1)
def keep_discarded(s):
    e=first(s,"ring.discard");e["inventory"]+=","+e["item"]+"x1"
def reused_press(s):
    slides=[e for e in of(s,"motor.state") if e["stateAfter"]=="Slide" and e["dir"]==-1]
    slides[1]["inputEventId"]=slides[0]["inputEventId"]
def teleport_exit(s):
    ticks={e["simulationTick"]:e for e in of(s,"tick")}
    for t in range(360,377):ticks[t]["x"]=ticks[359]["x"]
probe("plane","TC-01-01",None,"teleport counted as moving boss",boss_teleport)
probe("dev","TC-02-01","DevKb_RegisterUp","non-neutral release vector",wrong_release)
probe("dev","TC-02-01","DevPad_UseQuickslots","use before its own accepted request",premature_use)
probe("dev","TC-02-01","DevPad_Discard","confirmed item still in inventory",keep_discarded)
probe("slide","TC-03-04","SlideLeftKb","one consumed Jump press starts two slides",reused_press)
probe("slide","TC-03-04","SlideLeftKb","stationary until one-tick 3.24m exit",teleport_exit)
probe("plane","TC-01-01",None,"all samples propelled including first; compare source invariant only",lambda s:enemy(s).update(propelledSamples=enemy(s)["samples"]),True)
# --- originals
for k in files:
    m.CFG.clear(); m.CFG.update(json.loads(Path(re.sub(r"\.ndjson$", ".data.json", str(files[k]))).read_text(encoding="utf-8")))
    fn = {"main": m.check, "plane": m.plane_check, "dev": m.dev_check, "slide": m.slide_check}[k]
    out.append(dict(mode=k, case="original", expected=True, actual=all(v["pass"] for v in fn(logs[k]).values()), ok=all(v["pass"] for v in fn(logs[k]).values())))
print(json.dumps({"controls": out, "all_ok": all(x["ok"] for x in out)}, indent=1, ensure_ascii=False))
