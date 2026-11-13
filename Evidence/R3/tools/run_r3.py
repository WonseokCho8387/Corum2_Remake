"""Runs the working-tree checkers on the R2 / WP-10-R1 runs. With an output dir, writes each JSON there."""
import glob, json, os, subprocess, sys
P = "D:/ProjectR/Corum2Remake/"
out_dir = sys.argv[1] if len(sys.argv) > 1 else None
def run_of(pk, stamp): return glob.glob(P + f"Evidence/{pk}/runs/RUN-20261113-{stamp}.ndjson")[0]
J = {
    "wp06_checks_main.json": ("wp06", [run_of("WP-06-R2", "051441"), "--selftest"]),
    "wp06_checks_TC-04-09.json": ("wp06", ["--cw", run_of("WP-06-R2", "052020"), run_of("WP-06-R2", "052239"), "--selftest"]),
    "wp07_checks_main.json": ("wp07", [run_of("WP-07-R2", "052454"), "--selftest"]),
    "wp07_checks_TC-06-03.json": ("wp07", ["--hs", run_of("WP-07-R2", "052454"), run_of("WP-07-R2", "052923"), "--selftest"]),
    "wp08_checks_main.json": ("wp08", [run_of("WP-08-R2", "053139"), "--selftest"]),
    "wp08_checks_TC-07-04.json": ("wp08", ["--pw", run_of("WP-08-R2", "053139"), run_of("WP-08-R2", "053700"), run_of("WP-08-R2", "053926"), "--selftest"]),
    "wp10_checks.json": ("wp10", [run_of("WP-10-R1", "054149"), run_of("WP-10-R1", "054514"), run_of("WP-10-R1", "054756"), "--selftest"]),
}
for name, (tool, args) in J.items():
    o = subprocess.run(["python", "-B", P + f"Tools/{tool}_checks.py"] + args, capture_output=True, text=True, encoding="utf-8")
    try:
        j = json.loads(o.stdout)
    except Exception:
        print(name, "ERROR", o.stderr[-800:]); continue
    if out_dir:
        open(os.path.join(out_dir, name), "w", encoding="utf-8").write(o.stdout)
    st = [r for r in j.get("selftest", []) if r.get("case") != "original"]
    secs = {k: ((v.get("pass"), v.get("observedPartsPass")) if "observedPartsPass" in v else v.get("pass")) for k, v in j.items() if isinstance(v, dict) and "pass" in v}
    print(name, j.get("knockbackDiagnostics", ""), secs)
    print("   selftest", sum(1 for r in st if r.get("pass") is True), "/", len(st), "all", j.get("selftest_all_pass"),
          "notRejected", [(r["case"], r.get("sectionPass"), r.get("note")) for r in st if r.get("pass") is not True])
