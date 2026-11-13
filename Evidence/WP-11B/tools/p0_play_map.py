"""TC-UNITY-08: PlayMode TC map. For every master / appendix TC id, the PlayMode runs whose log records carry that test case id
(the input script's tc= context), with each run's scene, source revision and clean / dirty state from its manifest, next to the
latest GPT ledger status. A TC with no PlayMode run stays NOT_RUN in this map (it may still be judged from EditMode or documents).

Usage: python Tools/p0_play_map.py <master spec .md> <unity appendix .md> <GPT P0_status.md> <out .json> <out .md>
Scans Evidence/**/runs/*.ndjson under the repository root (the current directory).
"""
import glob
import json
import os
import re
import sys

TC = re.compile(r"^\| (TC-[A-Z0-9]+(?:-[A-Z0-9]+)*[a-d]?) \|", re.M)
TCID = re.compile(r'"testCaseId":\s*"([^"]*)"')


def tc_ids(path):
    return sorted(set(TC.findall(open(path, encoding="utf-8").read())) - {"TC-ID"})  # "TC-ID" is a table header


def ledger(path):
    out = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"^\| (TC-[A-Z0-9-]+[a-d]?) \| ([A-Z_]+) \|", line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def manifest(run):
    p = run[:-len(".ndjson")] + ".manifest.yaml"
    if not os.path.exists(p):
        return {}
    txt = open(p, encoding="utf-8").read()
    g = lambda k: (re.search(r'^\s*' + k + r':\s*"?([^"\n]*)"?', txt, re.M) or [None, None])[1]
    return {"revision": (g("source_revision") or "")[:7], "dirty": g("working_tree_dirty") == "true", "scene": g("scene")}


def main():
    master, appendix, led, out_json, out_md = sys.argv[1:6]
    ids = tc_ids(master)
    ids += [i for i in tc_ids(appendix) if i not in ids]
    status = ledger(led)
    runs = sorted(glob.glob("Evidence/**/runs/*.ndjson", recursive=True))
    by_tc = {}
    for r in runs:
        seen = set()
        with open(r, encoding="utf-8") as f:
            for line in f:
                m = TCID.search(line)
                if m:
                    seen.update(x.strip() for x in m.group(1).split(",") if x.strip().startswith("TC-"))
        mf = manifest(r)
        rec = {"run": os.path.basename(r)[:-len(".ndjson")], "evidence": r.replace("\\", "/").split("/runs/")[0], **mf}
        for t in seen:
            by_tc.setdefault(t, []).append(rec)
    rows = []
    for t in ids:
        rs = sorted(by_tc.get(t, []), key=lambda x: x["run"])
        rows.append({"tc": t, "gptStatus": status.get(t), "playRuns": len(rs), "latest": rs[-1] if rs else None,
                     "evidence": sorted({x["evidence"] for x in rs}), "playMode": "RUN" if rs else "NOT_RUN"})
    unknown = sorted(t for t in by_tc if t not in ids)
    json.dump({"tcs": rows, "runsScanned": len(runs), "tcIdsInLogsNotInSpec": unknown}, open(out_json, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    with open(out_md, "w", encoding="utf-8") as f:
        f.write("| TC | GPT 대장 | PlayMode 실행 수 | 최신 실행 | 장면 | 리비전 | 증빙 폴더 |\n|---|---|---:|---|---|---|---|\n")
        for r in rows:
            l = r["latest"] or {}
            scene = (l.get("scene") or "").replace("Assets/Corum2/Scenes/", "")
            rev = (l.get("revision") or "") + (" (dirty)" if l.get("dirty") else "")
            f.write(f"| {r['tc']} | {r['gptStatus'] or '-'} | {r['playRuns']} | {l.get('run', 'NOT_RUN')} | {scene} | {rev} | {', '.join(e.split('/')[-1] for e in r['evidence'])} |\n")
    print(json.dumps({"tcs": len(rows), "withPlayRuns": sum(1 for r in rows if r["playRuns"]), "runsScanned": len(runs), "unknownIds": unknown}))


if __name__ == "__main__":
    main()
