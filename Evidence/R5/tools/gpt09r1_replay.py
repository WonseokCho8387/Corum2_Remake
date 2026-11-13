# Replays GPT's WP-09-R1 counterexamples (prior 12 + new 9) against the working-tree checker. Reads GPT files, writes nothing there.
# R3: sets the checker's run config (CFG) from the dev and slide runs' data.json, which agree on stickDeadzone and slideSpeed (asserted).
from pathlib import Path
import copy, json, types, sys
g = Path(r"D:/ProjectR/Corum2/docs/GPT/evidence")
repo = Path("D:/ProjectR/Corum2Remake")
ep = repo / "Evidence/WP-09-R1"
chk = types.ModuleType("checker"); exec(compile((repo / "Tools/wp09_checks.py").read_text(encoding="utf-8"), "wp09_checks.py", "exec"), chk.__dict__)
def read(f): return [json.loads(l) for l in f.read_text(encoding="utf-8").splitlines()]
def of(es, kind): return [e for e in es if e["type"] == kind]
def first(es, kind): return of(es, kind)[0]
files = {k: next((ep / "runs").glob("*" + v + ".ndjson")) for k, v in dict(main="234229", plane="234529", dev="235402", slide="235043").items()}
logs = {k: read(f) for k, f in files.items()}; main, plane, dev, slide = logs.values()
cfgs = {k: json.loads(Path(str(f)[:-len(".ndjson")] + ".data.json").read_text(encoding="utf-8")) for k, f in files.items()}
assert len({(c["input"]["stickDeadzone"], c["movement"]["slideSpeed"]) for c in cfgs.values()}) == 1
chk.CFG.update(cfgs["dev"])
previous = (g / "WP-09_GPT_v1.0/review.py").read_text(encoding="utf-8")
snippet = previous[previous.index("def mutation("):previous.index('(base/"results.json").write_text')]
snippet = snippet.replace('e["reason"]=="RenderedEnemyDepth"', 'e["reason"].startswith("RenderedEnemyDepth")')
env = dict(chk=chk, main=main, plane=plane, copy=copy, of=of, r={}); exec(snippet, env)
prior = env["r"]["mutations"]
src = (g / "WP-09-R1_GPT_v1.0/review.py").read_text(encoding="utf-8")
block = src[src.index("def mutate("):src.index('(base/"review.json").write_text')]
env2 = dict(chk=chk, logs=logs, copy=copy, of=of, first=first, r={}); exec(block, env2)
new = env2["r"]["new_mutations"]
orig = {"main": chk.check(main), "plane": chk.plane_check(plane), "dev": chk.dev_check(dev), "slide": chk.slide_check(slide)}
out = {"checker": "working tree", "originalPass": {k: {tc: v["pass"] for tc, v in r.items()} for k, r in orig.items()}, "prior": prior, "new": new}
print(json.dumps(out, ensure_ascii=False, indent=1, default=str))
