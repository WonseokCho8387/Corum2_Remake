#!/usr/bin/env python3
"""WP-02-R4 (GPT WP02-M03): damaged copies of dirty-run captures must fail Tools/validate_manifest.py --repo .

Run from the repository root: python Evidence/WP-02-R4/tools/negative_cases.py
For each case the run folder is copied to a temp folder, one change is made, and the validator runs on the copy.
Cases M03-A/B/C repeat GPT's R3 review cases on the R3 dirty run; the rest use the R4 dirty run (modified, deleted,
untracked file, untracked folder, out-of-scope file). Exit 0 only if every damaged copy is rejected and every
unchanged copy passes.
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile

import yaml

RUNS = {
    "r3": ("Evidence/WP-02-R3/runs/dirty", "RUN-20261110-102151"),
    "r4": ("Evidence/WP-02-R4/runs/dirty", "RUN-20261110-114243"),
}


def load(p):
    with open(p, encoding="utf-8") as f:
        return yaml.safe_load(f)


def dump(p, doc):
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        yaml.safe_dump(doc, f, sort_keys=False, allow_unicode=True, width=1_000_000)


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def art_edit(fn):
    def f(d, man):
        doc = load(man)
        fn(doc["dirty_diff_artifact"], d)
        dump(man, doc)
    return f


def replace_copy(rel, data):
    def f(art, d):
        with open(os.path.join(d, art["untracked_dir"], rel), "wb") as fh:
            fh.write(data)
    return f


def drop_copy_to_skipped(rel):
    def f(art, d):
        os.remove(os.path.join(d, art["untracked_dir"], rel))
        art["untracked_files"].remove(rel)
        art["untracked_skipped"] = (art.get("untracked_skipped") or []) + [rel]
    return f


def drop_copy_silently(rel):
    def f(art, d):
        os.remove(os.path.join(d, art["untracked_dir"], rel))
        art["untracked_files"].remove(rel)
        if rel in (art.get("required_untracked") or []):
            art["required_untracked"].remove(rel)
    return f


def patch_bytes(transform):
    def f(art, d):
        p = os.path.join(d, art["patch"])
        with open(p, "rb") as fh:
            data = fh.read()
        with open(p, "wb") as fh:
            fh.write(transform(data))
        art["patch_sha256"] = sha(p)
    return f


def only_first_file_diff(data):
    text = data.decode("utf-8")
    parts = text.split("diff --git ")
    return ("diff --git " + parts[1]).encode("utf-8")


def drop_first_file_diff_and_status(art, d):
    """Remove the first file's diff and its status line: the patch and status agree, but the list does not."""
    p = os.path.join(d, art["patch"])
    with open(p, "rb") as fh:
        text = fh.read().decode("utf-8")
    parts = text.split("diff --git ")
    first = parts[1].split(" ")[0][2:]
    with open(p, "wb") as fh:
        fh.write(("diff --git " + "diff --git ".join(parts[2:])).encode("utf-8"))
    art["patch_sha256"] = sha(p)
    art["status"] = [s for s in art["status"] if not s.endswith(" " + first)]


def tamper_patch_content(data):
    return data.replace(b"probe line", b"PROBE LINE", 1)


def mark_incomplete(art, d):
    art["capture_complete"] = False
    art["incomplete_reasons"] = ["untracked file in scope not copied: Tools/r4_probe.txt"]


CASES = [
    ("r3", "unchanged (control)", None, "valid"),
    ("r3", "M03-A: untracked copy bytes replaced", art_edit(replace_copy("Tools/r3_dirty_probe.txt", b"different bytes\n")), "invalid"),
    ("r3", "M03-B: copy removed, moved to untracked_skipped", art_edit(drop_copy_to_skipped("Tools/r3_dirty_probe.txt")), "invalid"),
    ("r3", "M03-B variant: copy removed and dropped from untracked_files (no skipped entry)", art_edit(drop_copy_silently("Tools/r3_dirty_probe.txt")), "invalid"),
    ("r3", "M03-C: patch emptied, patch_sha256 updated", art_edit(patch_bytes(lambda b: b"")), "invalid"),
    ("r4", "unchanged (control)", None, "valid"),
    ("r4", "copy inside an untracked folder removed (lists updated)", art_edit(drop_copy_silently("Tools/r4_probe_dir/sub/b.bin")), "invalid"),
    ("r4", "binary copy bytes replaced", art_edit(replace_copy("Tools/r4_probe_dir/sub/b.bin", b"\x00" * 1024)), "invalid"),
    ("r4", "out-of-scope copy removed (not required)", art_edit(drop_copy_silently("r4_root_probe.txt")), "valid"),
    ("r4", "patch keeps only the first file diff", art_edit(patch_bytes(only_first_file_diff)), "invalid"),
    ("r4", "first file diff and its status line removed", art_edit(drop_first_file_diff_and_status), "invalid"),
    ("r4", "patch text changed (sha256 updated)", art_edit(patch_bytes(tamper_patch_content)), "invalid"),
    ("r4", "capture marked incomplete", art_edit(mark_incomplete), "invalid"),
    ("r4", "in-scope file listed in untracked_skipped", art_edit(drop_copy_to_skipped("Tools/r4_probe.txt")), "invalid"),
]


def main():
    ok = True
    for run, name, mut, expect in CASES:
        src, rid = RUNS[run]
        tmp = tempfile.mkdtemp(prefix="corum2-neg4-")
        try:
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
