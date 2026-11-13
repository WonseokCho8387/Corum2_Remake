"""TC-UNITY-08: PlayMode TC map (R1, GPT WP-11B review section 5).

For every master / appendix TC id the map now says how a PlayMode result is linked, not only whether a tag was seen:
  CHECKED       a committed checker result (Evidence/<packet>/checks/*.json) has this TC as a key with pass=true; the map names the
                check file, its packet, the runs it read (run id, scene, source revision, clean / dirty, how the revision was resolved)
                and the run videos. The newest such result (by its git commit time) is shown; an older pass with a newer fail is flagged.
  OBSERVED      a committed checker output (early packets) records observations under this TC key but makes no pass verdict;
                the expected result was judged from those observations by the GPT review named in the ledger column.
  CROSS         no checker key of its own, but a named checker key of another TC covers it (CROSS table below, with the reason).
  TAGGED_ONLY   runs carry the tag, but no checker result confirms the expected outcome ("run, expected result not confirmed").
  NO_TAG        no PlayMode run carries the tag ("tag not found"); the TC may be judged from EditMode or documents.
A tag alone never makes a TC CHECKED: a run without a check result and a manifest stays TAGGED_ONLY.
The GPT column is copied from the latest GPT ledger given on the command line; this map does not judge.

Usage: python Tools/p0_play_map.py <master spec .md> <unity appendix .md> <latest GPT P0_status.md> <out .json> <out .md>
Run from the repository root (scans Evidence/**/runs/*.ndjson and Evidence/**/checks/*.json).
"""
import glob
import json
import os
import re
import subprocess
import sys

TC = re.compile(r"^\| (TC-[A-Z0-9]+(?:-[A-Z0-9]+)*[a-d]?) \|", re.M)
TCID = re.compile(r'"testCaseId":\s*"([^"]*)"')
RUNID = re.compile(r"RUN-\d{8}-\d{6}")
# legacy manifests that wrote "<packet>": the revision and scene stated in that packet's README
PACKET_SOURCE = {"WP-01-R1": ("1559584", "Assets/Corum2/Scenes/WP01_MapMovement.unity"),
                 "WP-02": ("4cf88ba", "Assets/Corum2/Scenes/WP02_Combat.unity")}
# TCs covered by another TC's checker key (GPT ledger reasons)
CROSS = {"TC-04-10": (["TC-06-18", "TC-INT-01"], "the dead target's chain ends while each surviving target keeps its own (GPT WP-08 / INT-01)")}


def tc_ids(path):
    return sorted(set(TC.findall(open(path, encoding="utf-8").read())) - {"TC-ID"})


def ledger(path):
    """TC -> (status, the ledger's basis column with its review links)."""
    out = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"^\| (TC-[A-Z0-9-]+[a-d]?) \| ([A-Z_]+) \| ([^|]*) \|", line)
        if m:
            out[m.group(1)] = (m.group(2), re.findall(r"\[([^\]]+)\]", m.group(3)))
    return out


def key_tcs(k):
    """Checker keys -> TC ids: 'TC-08-09/10/11' -> three ids; 'TC-06-03-main' -> TC-06-03."""
    k = re.sub(r"-main$", "", k)
    if "/" in k:
        head, *rest = k.split("/")
        stem = head.rsplit("-", 1)[0]
        return [head] + [stem + "-" + r for r in rest]
    return [k]


def commit_time(path):
    try:
        return int(subprocess.check_output(["git", "log", "-1", "--format=%ct", "--", path], text=True).strip() or 0)
    except (subprocess.CalledProcessError, ValueError):
        return 0


def manifest(run_id, packet_hint=None):
    cands = sorted(c.replace(os.sep, "/") for c in glob.glob(f"Evidence/**/runs/{run_id}.manifest.yaml", recursive=True))
    if not cands:
        return {"run": run_id, "manifest": None, "provenance": "no manifest"}
    p = next((c for c in cands if packet_hint and f"Evidence/{packet_hint}/" in c), cands[0])
    txt = open(p, encoding="utf-8").read()
    g = lambda k: (re.search(r'^\s*' + k + r':\s*"?([^"\n]*)"?', txt, re.M) or [None, None])[1]
    packet = p.split("/")[1]
    rev, scene = g("source_revision") or "", g("scene") or ""
    how = "manifest"
    if rev.startswith("<packet"):
        prev, pscene = PACKET_SOURCE.get(packet, (None, None))
        rev, scene, how = prev or "", scene or pscene or "", "packet README (legacy manifest)"
    if not scene:
        sm = re.search(r"^scene_map:\s*(\S+\.unity)", txt, re.M)
        scene = sm.group(1) if sm else ""
    videos = sorted(v.replace(os.sep, "/") for v in glob.glob(f"Evidence/{packet}/video/*{run_id}*"))
    return {"run": run_id, "manifest": p, "packet": packet, "scene": scene, "revision": rev[:7], "dirty": g("working_tree_dirty") == "true",
            "provenance": how if rev and scene else "incomplete", "videos": videos}


def checks():
    """Every committed checker result: file, packet, commit time, the run ids it read, {tc: pass}."""
    out = []
    for f in sorted(glob.glob("Evidence/**/checks/*.json", recursive=True)):
        f = f.replace(os.sep, "/")
        try:
            d = json.load(open(f, encoding="utf-8"))
        except (ValueError, OSError):
            continue
        if not isinstance(d, dict):
            continue
        runs = sorted(set(RUNID.findall(json.dumps(d.get("log") or d.get("logs") or []))))
        tcs = {}
        for k, v in d.items():
            if k.startswith("TC-"):
                for t in key_tcs(k):
                    tcs[t] = (v["pass"] is True) if isinstance(v, dict) and "pass" in v else "observed"
        if tcs and runs:
            out.append({"file": f, "packet": f.split("/")[1], "time": commit_time(f), "runs": runs, "tcs": tcs})
    return out


def main():
    master, appendix, led, out_json, out_md = sys.argv[1:6]
    ids = tc_ids(master)
    ids += [i for i in tc_ids(appendix) if i not in ids]
    status = ledger(led)
    runs = sorted(r.replace(os.sep, "/") for r in glob.glob("Evidence/**/runs/*.ndjson", recursive=True))
    tagged = {}
    for r in runs:
        seen = set()
        with open(r, encoding="utf-8") as f:
            for line in f:
                m = TCID.search(line)
                if m:
                    seen.update(x.strip() for x in m.group(1).split(",") if x.strip().startswith("TC-"))
        for t in seen:
            tagged.setdefault(t, set()).add(os.path.basename(r)[:-len(".ndjson")])
    cks = checks()
    rows = []
    for t in ids:
        mine = sorted((c for c in cks if t in c["tcs"]), key=lambda c: (c["time"], c["file"]))
        passing = [c for c in mine if c["tcs"][t] is True]
        observed = [c for c in mine if c["tcs"][t] == "observed"]
        st, basis = status.get(t, (None, []))
        row = {"tc": t, "gptStatus": st, "gptBasis": basis, "taggedRuns": len(tagged.get(t, ())), "checks": len(mine)}
        if passing:
            c = passing[-1]
            row.update(category="CHECKED", check=c["file"], checkKey=t, runs=[manifest(r, c["packet"]) for r in c["runs"]],
                       newerFailingCheck=[x["file"] for x in mine if x["time"] > c["time"] and x["tcs"][t] is False])
        elif observed:
            c = observed[-1]
            row.update(category="OBSERVED", check=c["file"], checkKey=t, runs=[manifest(r, c["packet"]) for r in c["runs"]])
        elif t in CROSS:
            via, why = CROSS[t]
            src = []
            for v in via:
                vc = sorted((c for c in cks if c["tcs"].get(v) is True), key=lambda c: (c["time"], c["file"]))
                if vc:
                    src.append({"key": v, "check": vc[-1]["file"], "runs": [manifest(r, vc[-1]["packet"]) for r in vc[-1]["runs"]]})
            row.update(category="CROSS" if src else ("TAGGED_ONLY" if t in tagged else "NO_TAG"), via=src, reason=why)
        elif mine:
            row.update(category="TAGGED_ONLY", note="only failing checker results", failingChecks=[c["file"] for c in mine])
        else:
            row.update(category="TAGGED_ONLY" if t in tagged else "NO_TAG")
        if row["category"] == "TAGGED_ONLY":
            row["runs"] = [manifest(r) for r in sorted(tagged.get(t, ()))]
        if row["category"] in ("CHECKED", "OBSERVED", "CROSS"):
            rs = row.get("runs") or [r for v in row.get("via", []) for r in v["runs"]]
            row["provenanceComplete"] = bool(rs) and all(r["provenance"] in ("manifest", "packet README (legacy manifest)") for r in rs)
        rows.append(row)
    unknown = sorted(t for t in tagged if t not in ids)
    count = {k: sum(1 for r in rows if r["category"] == k) for k in ("CHECKED", "OBSERVED", "CROSS", "TAGGED_ONLY", "NO_TAG")}
    json.dump({"tcs": rows, "counts": count, "runsScanned": len(runs), "checkResults": len(cks), "tcIdsInLogsNotInSpec": unknown},
              open(out_json, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    label = {"CHECKED": "검사 결과로 확인", "OBSERVED": "관찰 기록 연결(자동 판정 없음)", "CROSS": "교차 근거로 확인", "TAGGED_ONLY": "실행했으나 기대 결과 미확인", "NO_TAG": "태그 미발견"}
    with open(out_md, "w", encoding="utf-8") as f:
        f.write("| TC | GPT 대장 (근거) | 연결 | 검사 결과 (키) | 실행 · 장면 · 리비전 | 영상 |\n|---|---|---|---|---|---|\n")
        for r in rows:
            rs = r.get("runs") or [x for v in r.get("via", []) for x in v["runs"]]
            chk = r.get("check") or "; ".join(f"{v['check']} ({v['key']})" for v in r.get("via", [])) or "-"
            run_s = "<br>".join(f"{x['run']} · {(x.get('scene') or '?').replace('Assets/Corum2/Scenes/', '')} · {x.get('revision') or '?'}"
                                + (" dirty" if x.get("dirty") else "") + (" (packet README)" if x.get("provenance", "").startswith("packet") else "")
                                for x in rs) or "-"
            vids = sum(len(x.get("videos") or []) for x in rs)
            extra = " ⚠ 이후 실패 결과" if r.get("newerFailingCheck") else ""
            gb = (r['gptStatus'] or '-') + (" (" + ", ".join(r['gptBasis']) + ")" if r['gptBasis'] else "")
            f.write(f"| {r['tc']} | {gb} | {label[r['category']]}{extra} | {chk} | {run_s} | {vids or '-'} |\n")
    print(json.dumps({"tcs": len(rows), **count, "runsScanned": len(runs), "checkResults": len(cks), "unknownIds": unknown}))


if __name__ == "__main__":
    main()
