#!/usr/bin/env python3
"""WP-02-R3 (GPT WP02-M02): per-file source lists for earlier runs and the records that link them.

Run from the repository root:  python Evidence/WP-02-R3/tools/make_r3_records.py
Outputs under Evidence/WP-02-R3/:
  posthoc/RUN-*.source_files.posthoc.tsv   lists made now from the recorded source commit (not at run time)
  corrections/*.yaml                       version-3 records for the two R1 manifests (correcting their v2 records)
                                           and a version-2 record for the R2 check run manifest
  checks/posthoc_method_check.json         the post-hoc method applied to the R3 clean run's commit, compared
                                           with the list that run wrote at run start
  checks/dirty_reconstruction.json         the R3 dirty run rebuilt from commit + patch + untracked copies,
                                           compared with its run-time list
Originals and earlier correction records are not modified. --generated-at fixes the generation time so a rerun
gives the same bytes.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile

import yaml

SCOPE = ["Assets", "Packages", "ProjectSettings", "Tools"]
COLUMNS = "path\tsize\tsha256\tgit_blob"
OUT = "Evidence/WP-02-R3"
TOOL = "Evidence/WP-02-R3/tools/make_r3_records.py"


def git(*args, cwd=".", binary=False, input=None):
    r = subprocess.run(["git", "-c", "core.quotepath=off", *args], cwd=cwd, capture_output=True, check=True, input=input)
    return r.stdout if binary else r.stdout.decode("utf-8")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def sha_file(path):
    with open(path, "rb") as f:
        return sha(f.read())


def tree_rows(rev):
    rows = []
    for e in git("ls-tree", "-r", "-z", rev, "--", *SCOPE).split("\0"):
        if not e:
            continue
        meta, path = e.split("\t", 1)
        blob = meta.split()[2]
        data = git("cat-file", "blob", blob, binary=True)
        rows.append((path, len(data), sha(data), blob))
    rows.sort(key=lambda r: r[0])
    return rows


def write_list(path, rows, header):
    lines = [f"# {k}: {v}" for k, v in header]
    lines += [f"# entries: {len(rows)}", f"# total_bytes: {sum(r[1] for r in rows)}", COLUMNS]
    lines += [f"{p}\t{s}\t{h}\t{b}" for p, s, h, b in rows]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def read_list(path):
    rows = []
    with open(path, encoding="utf-8", newline="") as f:
        for line in f.read().split("\n"):
            if not line or line.startswith("#") or line == COLUMNS:
                continue
            p, s, h, b = line.split("\t")
            rows.append((p, int(s), h, b))
    return rows


def posthoc_list(run_id, rev, generated_at):
    rows = tree_rows(rev)
    rel = f"posthoc/{run_id}.source_files.posthoc.tsv"
    path = f"{OUT}/{rel}"
    write_list(path, rows, [
        ("run_id", run_id),
        ("source_revision", rev),
        ("scope", ",".join(SCOPE) + " (files of the commit tree)"),
        ("bytes_basis", "git blob content of source_revision (line endings as stored in git), generated after the run"),
        ("git_blob", "blob id in the commit tree"),
        ("generated_at", generated_at),
        ("generated_by", TOOL),
    ])
    return rel, path, rows


def file_list_map(rel_from_record, path, rows, rev, generated_at):
    return {
        "file": rel_from_record,
        "sha256": sha_file(path),
        "format": "tsv: path size sha256 git_blob",
        "entries": len(rows),
        "total_bytes": sum(r[1] for r in rows),
        "scope": SCOPE,
        "bytes_basis": "git blob content of source_revision (line endings as stored in git), generated after the run",
        "git_tree": git("rev-parse", rev + "^{tree}").strip(),
        "generated_after_run": True,
        "generated_at": generated_at,
        "generated_by": TOOL,
        "note": ("not written at run time. The run's working tree was clean at source_revision, so these are the files "
                 "of that commit; working-tree bytes may differ from git blob bytes by line endings (core.autocrlf=true). "
                 "See checks/posthoc_method_check.json for how this method compares with a run-time list."),
    }


def write_record(name, doc):
    path = f"{OUT}/corrections/{name}"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# WP-02-R3 correction record (WP02-M02). The file named in 'corrects' is unchanged.\n")
        yaml.safe_dump(doc, f, sort_keys=False, allow_unicode=True, width=1_000_000)
    return path


def correct_r1(run_id, generated_at):
    v2_path = f"Evidence/WP-02-R2/corrections/{run_id}.manifest.v2.yaml"
    with open(v2_path, encoding="utf-8") as f:
        v2 = yaml.safe_load(f)
    corrected = v2["corrected"]
    rev = corrected["source_revision"]
    rel, path, rows = posthoc_list(run_id, rev, generated_at)
    old = corrected["source_file_hash_manifest"]
    corrected["source_file_hash_manifest"] = file_list_map("../" + rel, path, rows, rev, generated_at)
    corrected["source_file_hash_manifest"]["v2_value"] = old
    doc = {
        "record_kind": "manifest_correction",
        "correction_id": "WP02-M02/" + run_id + ".manifest.yaml",
        "correction_version": 3,
        "corrects": v2_path,
        "corrects_sha256": sha_file(v2_path),
        "original": v2["corrects"],
        "original_sha256": v2["corrects_sha256"],
        "reason": "GPT WP02-M02: 13.3 source_file_hash_manifest must be a per-file path/size/SHA-256 list; v2 held only the git tree id",
        "original_kept": True,
        "changes": [
            "source_file_hash_manifest -> mapping that names a per-file list generated after the run from source_revision (marked generated_after_run)",
            "the v2 git-tree value is kept under source_file_hash_manifest.v2_value",
            "all other fields are the v2 corrected values unchanged",
        ],
        "corrected": corrected,
    }
    return write_record(run_id + ".manifest.v3.yaml", doc)


def correct_r2_run(run_id, generated_at):
    src = f"Evidence/WP-02-R2/runs/{run_id}.manifest.yaml"
    with open(src, encoding="utf-8") as f:
        corrected = yaml.safe_load(f)
    rev = corrected["source_revision"]
    rel, path, rows = posthoc_list(run_id, rev, generated_at)
    old = corrected["source_file_hash_manifest"]
    corrected["source_file_hash_manifest"] = file_list_map("../" + rel, path, rows, rev, generated_at)
    corrected["source_file_hash_manifest"]["original_value"] = old
    doc = {
        "record_kind": "manifest_correction",
        "correction_id": "WP02-M02/" + run_id + ".manifest.yaml",
        "correction_version": 2,
        "corrects": src,
        "corrects_sha256": sha_file(src),
        "reason": "GPT WP02-M02: 13.3 source_file_hash_manifest must be a per-file path/size/SHA-256 list; the manifest held only the git tree id",
        "original_kept": True,
        "changes": [
            "source_file_hash_manifest -> mapping that names a per-file list generated after the run from source_revision (marked generated_after_run)",
            "the original git-tree value is kept under source_file_hash_manifest.original_value",
            "all other fields are the original manifest values unchanged (run_id, started_at, updated_at, manifest_revision included)",
            "the at-start copy RUN-20261110-092516.manifest.at-start.yaml is covered by the same list (same run, same revision)",
        ],
        "corrected": corrected,
    }
    return write_record(run_id + ".manifest.v2.yaml", doc)


def posthoc_method_check(run_dir, run_id):
    """Apply the post-hoc method to a run that also wrote its list at run start and compare."""
    runtime = read_list(f"{run_dir}/{run_id}.source_files.tsv")
    with open(f"{run_dir}/{run_id}.manifest.yaml", encoding="utf-8") as f:
        rev = yaml.safe_load(f)["source_revision"]
    post = tree_rows(rev)
    rt, ph = {r[0]: r for r in runtime}, {r[0]: r for r in post}
    same_bytes = [p for p in rt if p in ph and rt[p][2] == ph[p][2]]
    blob_only = [p for p in rt if p in ph and rt[p][2] != ph[p][2] and rt[p][3] == ph[p][3]]
    differ = [p for p in rt if p in ph and rt[p][3] != ph[p][3]]
    return {
        "run_id": run_id, "source_revision": rev,
        "runtime_entries": len(runtime), "posthoc_entries": len(post),
        "same_path_set": set(rt) == set(ph),
        "same_git_blob": len(same_bytes) + len(blob_only),
        "same_sha256": len(same_bytes),
        "line_ending_only_differences": len(blob_only),
        "line_ending_only_paths": sorted(blob_only),
        "content_differences": sorted(differ),
        "meaning": ("post-hoc rows select the same files and git content as the run-time list; sha256 differs only where "
                    "the working tree held the file with different line endings than git stores"),
    }


def dirty_reconstruction(run_dir, run_id):
    """Rebuild the dirty run's source from commit + patch + untracked copies and compare with its run-time list."""
    with open(f"{run_dir}/{run_id}.manifest.yaml", encoding="utf-8") as f:
        man = yaml.safe_load(f)
    art = man["dirty_diff_artifact"]
    rev = man["source_revision"]
    runtime = {r[0]: r for r in read_list(f"{run_dir}/{man['source_file_hash_manifest']['file']}")}
    tmp = tempfile.mkdtemp(prefix="corum2-r3-")
    wt = os.path.join(tmp, "wt")
    try:
        git("worktree", "add", "--detach", wt, rev)
        git("apply", "--binary", os.path.abspath(f"{run_dir}/{art['patch']}"), cwd=wt)
        for rel in art["untracked_files"]:
            dst = os.path.join(wt, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(os.path.join(run_dir, art["untracked_dir"], rel), dst)
        files = [p for p in git("ls-files", "-z", "-c", "-o", "--exclude-standard", "--", *SCOPE, cwd=wt).split("\0") if p]
        files = sorted(p for p in files if os.path.isfile(os.path.join(wt, p)))
        blobs = git("hash-object", "--stdin-paths", cwd=wt, input=("\n".join(files) + "\n").encode()).split()
        rebuilt = {p: b for p, b in zip(files, blobs)}
        untracked_bytes = all(
            sha_file(os.path.join(wt, rel)) == runtime[rel][2] for rel in art["untracked_files"] if rel in runtime)
    finally:
        git("worktree", "remove", "--force", wt)
        shutil.rmtree(tmp, ignore_errors=True)
    mismatch = sorted(p for p in set(rebuilt) | set(runtime) if rebuilt.get(p) != (runtime.get(p) or (None,) * 4)[3])
    return {
        "run_id": run_id, "source_revision": rev, "patch": art["patch"], "untracked_files": art["untracked_files"],
        "runtime_entries": len(runtime), "rebuilt_entries": len(rebuilt),
        "git_blob_equal_all": not mismatch, "mismatch": mismatch,
        "untracked_bytes_equal": untracked_bytes,
        "meaning": "commit + patch + untracked copies reproduce every run-time row's git content (git_blob)",
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--generated-at", required=True)
    a = ap.parse_args()
    out = [correct_r1("RUN-20261110-075442", a.generated_at),
           correct_r1("RUN-20261110-075540", a.generated_at),
           correct_r2_run("RUN-20261110-092516", a.generated_at)]
    os.makedirs(f"{OUT}/checks", exist_ok=True)
    checks = {
        "posthoc_method_check.json": posthoc_method_check(f"{OUT}/runs/clean", "RUN-20261110-102053"),
        "dirty_reconstruction.json": dirty_reconstruction(f"{OUT}/runs/dirty", "RUN-20261110-102151"),
    }
    for name, data in checks.items():
        with open(f"{OUT}/checks/{name}", "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
            f.write("\n")
        out.append(f"{OUT}/checks/{name}")
    for p in out:
        print(p, sha_file(p))


if __name__ == "__main__":
    main()
