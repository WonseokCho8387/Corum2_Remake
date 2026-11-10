#!/usr/bin/env python3
"""WP-02-R3: mutated copies of the R3 run manifests must fail Tools/validate_manifest.py (GPT WP02-M02 blind spot).

Run from the repository root: python Evidence/WP-02-R3/tools/negative_cases.py
Copies run files to a temp folder, applies one mutation per case, runs the validator with --repo . and prints
one JSON line per case. Exit 0 only if every mutated copy is rejected and the unmutated copies pass.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

import yaml

RUNS = {"clean": "RUN-20261110-102053", "dirty": "RUN-20261110-102151"}


def load(p):
    with open(p, encoding="utf-8") as f:
        return yaml.safe_load(f)


def dump(p, doc):
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        yaml.safe_dump(doc, f, sort_keys=False, allow_unicode=True, width=1_000_000)


def set_field(key, value):
    def f(d, man):
        doc = load(man)
        doc[key] = value
        dump(man, doc)
    return f


def edit_list(fn):
    def f(d, man):
        lst = os.path.join(d, load(man)["source_file_hash_manifest"]["file"])
        with open(lst, encoding="utf-8", newline="") as fh:
            lines = fh.read().split("\n")
        with open(lst, "w", encoding="utf-8", newline="") as fh:
            fh.write("\n".join(fn(lines)))
    return f


def fix_list_hash_after(mut):
    """Apply a list edit, then update the manifest's list sha256 so only the content check can catch it."""
    def f(d, man):
        mut(d, man)
        import hashlib
        doc = load(man)
        lst = os.path.join(d, doc["source_file_hash_manifest"]["file"])
        with open(lst, "rb") as fh:
            doc["source_file_hash_manifest"]["sha256"] = hashlib.sha256(fh.read()).hexdigest()
        dump(man, doc)
    return f


def first_row(lines):
    return next(i for i, l in enumerate(lines) if l and not l.startswith("#") and not l.startswith("path\t"))


def drop_row(lines):
    i = first_row(lines)
    return lines[:i] + lines[i + 1:]


def change_size(lines):
    i = first_row(lines)
    p = lines[i].split("\t")
    p[1] = str(int(p[1]) + 1)
    lines[i] = "\t".join(p)
    return lines


def change_blob(lines):
    i = first_row(lines)
    p = lines[i].split("\t")
    p[3] = "0" * 40
    lines[i] = "\t".join(p)
    return lines


def drop_status(d, man):
    doc = load(man)
    doc["dirty_diff_artifact"]["status"] = [s for s in doc["dirty_diff_artifact"]["status"] if "r3_dirty_probe" not in s]
    dump(man, doc)


CASES = [
    ("clean", "unmutated (control)", None, "valid"),
    ("clean", "source_file_hash_manifest null (GPT example)", set_field("source_file_hash_manifest", None), "invalid"),
    ("clean", "git-tree-only mapping (R2 form)", set_field("source_file_hash_manifest", {"mode": "git-tree", "tree": "5bca6573efb03df639574bd13a982f760b326494"}), "invalid"),
    ("clean", "list edited, manifest sha256 not updated", edit_list(change_size), "invalid"),
    ("clean", "row removed (list sha256 fixed in manifest)", fix_list_hash_after(edit_list(drop_row)), "invalid"),
    ("clean", "row size changed (list sha256 fixed in manifest)", fix_list_hash_after(edit_list(change_size)), "invalid"),
    ("clean", "row git_blob changed (list sha256 fixed in manifest)", fix_list_hash_after(edit_list(change_blob)), "invalid"),
    ("clean", "claims dirty without artifact", set_field("working_tree_dirty", True), "invalid"),
    ("dirty", "unmutated (control)", None, "valid"),
    ("dirty", "dirty_diff_artifact null", set_field("dirty_diff_artifact", None), "invalid"),
    ("dirty", "status misses the untracked file", drop_status, "invalid"),
    ("dirty", "claims clean", set_field("working_tree_dirty", False), "invalid"),
]


def main():
    ok = True
    for run, name, mut, expect in CASES:
        rid = RUNS[run]
        tmp = tempfile.mkdtemp(prefix="corum2-neg-")
        try:
            src = f"Evidence/WP-02-R3/runs/{run}"
            for n in os.listdir(src):
                s = os.path.join(src, n)
                (shutil.copytree if os.path.isdir(s) else shutil.copyfile)(s, os.path.join(tmp, n))
            man = os.path.join(tmp, rid + ".manifest.yaml")
            if mut:
                mut(tmp, man)
            r = subprocess.run([sys.executable, "Tools/validate_manifest.py", "--repo", ".", man], capture_output=True, text=True)
            line = json.loads(r.stdout.splitlines()[0])
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        got = "valid" if line["result"] == "valid" else "invalid"
        passed = got == expect
        ok &= passed
        print(json.dumps({"run": run, "case": name, "expected": expect, "result": line["result"],
                          "error": line.get("error"), "pass": passed}, ensure_ascii=False))
    print(json.dumps({"all_pass": ok}))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
