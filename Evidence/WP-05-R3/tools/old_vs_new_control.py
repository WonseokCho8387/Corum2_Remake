import copy, importlib.util, json, sys
def mod(path, name):
    s = importlib.util.spec_from_file_location(name, path); m = importlib.util.module_from_spec(s); s.loader.exec_module(m); return m
old = mod(sys.argv[1], "old"); new = mod("D:/ProjectR/Corum2Remake/Tools/wp05_checks.py", "new")
out = {}
for log in sys.argv[2:]:
    ev = new.load(log)
    uses = [u for u in ev if u["type"] == "item.use" and str(u["reason"]).startswith("magic:")]
    last = uses[-1]
    drop = [e for e in copy.deepcopy(ev) if e.get("eventId") != last["eventId"]]
    shift = copy.deepcopy(ev)
    for e in shift:
        if e.get("eventId") == last["eventId"]: e["requestId"] += 90000
    sel = ["Lobby", "LoadSave", "CheckpointRestart"]
    row = {"lastScrollUseTick": last["simulationTick"]}
    for name, m in (("original", ev), ("lastUseRemoved", drop), ("lastRequestIdShifted", shift)):
        row[name] = {"oldChecker_TC-02-10": old.check(copy.deepcopy(m), sel)["TC-02-10"]["pass"],
                     "newChecker_TC-02-10": new.check(copy.deepcopy(m), sel)["TC-02-10"]["pass"]}
    out[log.split("/")[-1]] = row
print(json.dumps(out, indent=1))
