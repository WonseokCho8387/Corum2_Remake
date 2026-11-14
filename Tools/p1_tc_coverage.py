"""Maps EditMode results (NUnit XML) and the Play smoke checks to the 204 P1 test ids.

A test method named T05_T58_T62_Xxx covers P1-T05, P1-T58 and P1-T62. A P1 id is PASS(A) when at least one covering
test passed and none failed. Play smoke items (P1-T201~204, T15/T188/T191 screen parts) come from the play checks JSON.
Sub-conditions that the master leaves open (S), human / playtest measurements (H) and real-pad checks (E) are listed
from the tier table of the relaxed test matrix and are never turned into PASS.
Usage: python Tools/p1_tc_coverage.py <TestResults.xml> <play_checks.json> <out.json>
"""
import json
import re
import sys
import xml.etree.ElementTree as ET

TIERS = {
    21: "A+H", 39: "A+H", 45: "H", 51: "A+S", 63: "A+S", 78: "A+E", 127: "A+E", 129: "A+E", 85: "A+S", 87: "A+S", 120: "A+S",
    123: "A+S", 142: "A+S", 150: "A+S", 155: "A+S", 15: "A+P", 188: "A+P", 191: "A+P", 201: "P", 202: "P", 203: "P", 204: "P",
    24: "A+S", 25: "A+S",
}
PLAY = {201: "T201", 202: "T202", 203: "T203", 204: "T204", 188: "growth_screen", 191: "T201", 15: "T203"}


def main(xml_path, play_path, out_path):
    root = ET.parse(xml_path).getroot()
    cover = {}
    for tc in root.iter("test-case"):
        name = tc.get("name", "")
        res = tc.get("result", "")
        cls = tc.get("classname", "")
        if not cls.startswith("Corum2.Tests.P1"):
            continue
        for n in re.findall(r"T(\d+)(?=_)", name):
            cover.setdefault(int(n), []).append((cls.split(".")[-1] + "." + name, res))
    play = json.load(open(play_path, encoding="utf-8"))["summary"]
    rows = []
    for i in range(1, 205):
        tier = TIERS.get(i, "A")
        tests = cover.get(i, [])
        auto = None
        if tests:
            auto = "PASS" if all(r == "Passed" for _, r in tests) else "FAIL"
        pk = PLAY.get(i)
        playres = None if pk is None else ("PASS" if play.get(pk) else "FAIL")
        if tier == "H":
            status = "HUMAN_REVIEW"
        elif tier == "P":
            status = playres or "NOT_RUN"
            if auto == "FAIL":
                status = "FAIL"
        else:
            status = auto or "NOT_RUN"
            if "P" in tier and playres == "FAIL":
                status = "FAIL"
        open_parts = [t for t in tier.split("+") if t in ("S", "H", "E")]
        rows.append({"id": "P1-T%02d" % i if i < 100 else "P1-T%d" % i, "tier": tier, "status": status,
                     "openSubConditions": open_parts, "tests": [t for t, _ in tests], "play": pk and (pk + ":" + str(playres))})
    summ = {}
    for r in rows:
        summ[r["status"]] = summ.get(r["status"], 0) + 1
    json.dump({"summary": summ, "rows": rows}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(summ)
    print("NOT_RUN:", [r["id"] for r in rows if r["status"] == "NOT_RUN"])


if __name__ == "__main__":
    main(*sys.argv[1:4])
