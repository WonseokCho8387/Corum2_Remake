#!/usr/bin/env python3
"""Parse and check run manifests, supplement packets and correction records (WP02-M01-R1-A/B, WP02-M02).

Usage:
  python Tools/validate_manifest.py [--repo DIR] [--worktree] FILE... [--expect-fail FILE...]

Every file is read with PyYAML safe_load; then required fields, types and values are checked.
Master spec 13.3 source_file_hash_manifest must be a mapping that names a per-file list (TSV: path, size,
sha256, git_blob). The list file is resolved next to the YAML file; its sha256, row format, entry count, total
bytes, ordering and uniqueness are checked.
  --repo DIR   also compare every row with the commit `source_revision` in that git repository: the path set must
               equal the commit's files in the list scope (clean runs), and each row's git_blob must equal the
               commit's blob. Lists whose bytes_basis is git blob content are re-hashed from the blobs.
  --worktree   (with --repo) also re-hash the files in the repository working tree for working-tree-basis lists;
               use only when the working tree is at source_revision with the same line-ending settings.
Files after --expect-fail must fail to parse or validate.
Exit code 0 only if every expectation holds. Prints one JSON line per file.
"""
import hashlib
import json
import os
import re
import subprocess
import sys

import yaml

HEX64 = re.compile(r"^[0-9a-f]{64}$")
HEX40 = re.compile(r"^[0-9a-f]{40}$")
ISO_TZ = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$")
RUN_ID = re.compile(r"^RUN-(\d{8})-(\d{6})$")
COLUMNS = "path\tsize\tsha256\tgit_blob"

# Master spec 13.3 template fields.
SPEC_13_3 = [
    "run_id", "spec_id", "spec_version", "engine_appendix_version", "engine_appendix_sha256", "spec_sha256",
    "build_id", "source_revision", "source_capture_mode", "working_tree_dirty", "dirty_diff_artifact",
    "source_snapshot_artifact", "source_file_hash_manifest", "engine", "engine_version", "platform_os",
    "device_input", "display", "render_fps_observed", "simulation_tick", "time_policy_version", "data_snapshot",
    "experimental_policies", "scene_map", "save_fixture", "random_seed", "started_at", "tester", "commands_or_steps",
]

OPTS = {"repo": None, "worktree": False}


class Bad(Exception):
    pass


def need(cond, msg):
    if not cond:
        raise Bad(msg)


def is_str(v):
    return isinstance(v, str)


def is_int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def sha_file(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def sha_or_null(doc, key, note_key=None):
    v = doc.get(key)
    if v is None:
        need(note_key is None or is_str(doc.get(note_key)), f"{key} is null without {note_key}")
        return
    need(is_str(v) and HEX64.match(v), f"{key} must be a 64-hex string, got {v!r}")


def git(*args, binary=False):
    out = subprocess.run(["git", "-c", "core.quotepath=off", "-C", OPTS["repo"], *args], capture_output=True, check=True)
    return out.stdout if binary else out.stdout.decode("utf-8")


def read_list(path):
    header, rows, columns_seen = {}, [], False
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    need(text.endswith("\n"), "list must end with a newline")
    for line in text[:-1].split("\n"):
        if line.startswith("# "):
            k, _, v = line[2:].partition(": ")
            header[k] = v
            continue
        if not columns_seen:
            need(line == COLUMNS, f"list column line {line!r}")
            columns_seen = True
            continue
        p = line.split("\t")
        need(len(p) == 4, f"list row needs 4 columns: {line!r}")
        need(p[1].isdigit(), f"size {p[1]!r}")
        need(HEX64.match(p[2]), f"sha256 {p[2]!r}")
        need(p[3] == "" or HEX40.match(p[3]), f"git_blob {p[3]!r}")
        rows.append((p[0], int(p[1]), p[2], p[3]))
    need(columns_seen, "list has no column line")
    return header, rows


def check_file_list(sfm, base_dir, doc):
    need(isinstance(sfm, dict), f"source_file_hash_manifest must be a mapping naming a per-file list, got {sfm!r}")
    for k in ("file", "sha256", "entries", "total_bytes", "scope", "bytes_basis"):
        need(k in sfm, f"source_file_hash_manifest.{k} missing")
    need(is_str(sfm["file"]) and HEX64.match(sfm["sha256"] or ""), "source_file_hash_manifest file/sha256")
    need(is_int(sfm["entries"]) and sfm["entries"] > 0 and is_int(sfm["total_bytes"]), "entries/total_bytes")
    need(isinstance(sfm["scope"], list) and sfm["scope"] and all(is_str(s) for s in sfm["scope"]), "scope")
    need(is_str(sfm["bytes_basis"]), "bytes_basis")
    if sfm.get("git_tree") is not None:
        need(HEX40.match(sfm["git_tree"]), "git_tree")
    path = os.path.join(base_dir, sfm["file"])
    need(os.path.isfile(path), f"list file not found: {path}")
    need(sha_file(path) == sfm["sha256"], "list file sha256 differs from the manifest")
    header, rows = read_list(path)
    paths = [r[0] for r in rows]
    need(paths == sorted(paths), "rows not sorted (ordinal)")
    need(len(set(paths)) == len(paths), "duplicate paths")
    need(len(rows) == sfm["entries"] and str(len(rows)) == header.get("entries"), "entry count mismatch")
    total = sum(r[1] for r in rows)
    need(total == sfm["total_bytes"] and str(total) == header.get("total_bytes"), "total_bytes mismatch")
    need(header.get("run_id") == doc["run_id"], "list run_id differs from manifest")
    need(header.get("source_revision") == doc["source_revision"], "list source_revision differs from manifest")
    for r in rows:
        need(any(r[0] == s or r[0].startswith(s + "/") for s in sfm["scope"]), f"path outside scope: {r[0]}")
    stats = {"entries": len(rows), "total_bytes": total}
    if OPTS["repo"]:
        stats.update(check_against_git(rows, sfm, doc))
    return stats


def check_against_git(rows, sfm, doc):
    rev = doc["source_revision"]
    if sfm.get("git_tree"):
        need(git("rev-parse", rev + "^{tree}").strip() == sfm["git_tree"], "git_tree differs from the commit's tree")
    tree = {}
    for entry in git("ls-tree", "-r", "-z", rev, "--", *sfm["scope"]).split("\0"):
        if entry:
            meta, path = entry.split("\t", 1)
            tree[path] = meta.split()[2]
    clean = doc.get("working_tree_dirty") is False
    row_paths = {r[0] for r in rows}
    if clean:
        need(row_paths == set(tree), f"clean run: list paths differ from commit (only list {sorted(row_paths - set(tree))[:5]}, "
             f"only commit {sorted(set(tree) - row_paths)[:5]})")
    blob_mismatch = [r[0] for r in rows if r[0] in tree and r[3] != tree[r[0]]]
    if clean:
        need(not blob_mismatch, f"git_blob differs from commit for {blob_mismatch[:5]}")
    if doc.get("working_tree_dirty") is True:
        # Rows that differ from the commit (changed, untracked) and commit files without a row (deleted) must be
        # exactly the scope paths that git status reported at run start.
        status_paths = set()
        for line in doc["dirty_diff_artifact"]["status"]:
            p = line[3:].split(" -> ")[-1].strip('"')
            if any(p == s or p.startswith(s + "/") for s in sfm["scope"]):
                status_paths.add(p)
        differing = set(blob_mismatch) | (row_paths - set(tree)) | (set(tree) - row_paths)
        need(differing == status_paths, f"dirty rows {sorted(differing)} differ from git status {sorted(status_paths)}")
    res = {"git_rows_matched": sum(1 for r in rows if r[0] in tree and r[3] == tree[r[0]]), "git_blob_mismatch": len(blob_mismatch)}
    basis = sfm["bytes_basis"]
    if basis.startswith("git blob content"):
        for path, size, sha, blob in rows:
            data = git("cat-file", "blob", tree[path], binary=True)
            need(len(data) == size and hashlib.sha256(data).hexdigest() == sha, f"blob bytes differ for {path}")
        res["rehashed_from_git"] = len(rows)
    elif OPTS["worktree"]:
        for path, size, sha, blob in rows:
            full = os.path.join(OPTS["repo"], path)
            need(os.path.isfile(full), f"worktree file missing {path}")
            need(os.path.getsize(full) == size and sha_file(full) == sha, f"worktree bytes differ for {path}")
        res["rehashed_from_worktree"] = len(rows)
    return res


def check_dirty_artifact(doc, base_dir):
    dirty = doc["working_tree_dirty"]
    art = doc["dirty_diff_artifact"]
    if dirty is True:
        need(isinstance(art, dict), "dirty run needs a dirty_diff_artifact mapping")
        need(isinstance(art.get("status"), list) and art["status"], "dirty_diff_artifact.status")
        need(is_str(art.get("patch")) and HEX64.match(art.get("patch_sha256") or ""), "dirty_diff_artifact patch/sha256")
        need(sha_file(os.path.join(base_dir, art["patch"])) == art["patch_sha256"], "patch sha256 differs")
        need(isinstance(art.get("untracked_files"), list), "untracked_files list")
        for rel in art["untracked_files"]:
            need(os.path.isfile(os.path.join(base_dir, art["untracked_dir"], rel)), f"untracked copy missing: {rel}")
    if dirty is False:
        need(art is None, "clean tree must have dirty_diff_artifact null")


def check_run_manifest(doc, base_dir):
    for k in SPEC_13_3:
        need(k in doc, f"missing 13.3 field {k}")
    m = RUN_ID.match(doc["run_id"]) if is_str(doc["run_id"]) else None
    need(m, f"run_id {doc['run_id']!r}")
    need(doc["spec_id"] == "CORUM2-P0-MASTER", "spec_id")
    need(doc["spec_version"] == "1.2.0" and doc["engine_appendix_version"] == "1.0.0", "spec/appendix version must be strings")
    sha_or_null(doc, "spec_sha256", "spec_hash_note")
    sha_or_null(doc, "engine_appendix_sha256", "spec_hash_note")
    need(doc["source_capture_mode"] in ("git", "snapshot"), "source_capture_mode")
    if doc["source_capture_mode"] == "git":
        need(is_str(doc["source_revision"]) and HEX40.match(doc["source_revision"]), "source_revision must be a 40-hex commit")
        dirty = doc["working_tree_dirty"]
        need(dirty is None or isinstance(dirty, bool), f"working_tree_dirty must be bool or null, got {dirty!r}")
        if dirty is None:
            need(is_str(doc.get("source_note")) or is_str(doc.get("working_tree_dirty_note")), "unknown dirty state needs a note")
        check_dirty_artifact(doc, base_dir)
    stats = check_file_list(doc["source_file_hash_manifest"], base_dir, doc)
    need(isinstance(doc["experimental_policies"], list) and all(is_str(x) for x in doc["experimental_policies"]),
         "experimental_policies must be a list of strings")
    need(is_str(doc["data_snapshot"]), "data_snapshot")
    sha_or_null(doc, "data_snapshot_sha256")
    need(is_str(doc["tester"]) and doc["tester"], "tester")
    need(is_str(doc["commands_or_steps"]), "commands_or_steps")
    if "commands_sha256" in doc:
        sha_or_null(doc, "commands_sha256")
    st = doc["started_at"]
    if st is None:
        need(is_str(doc.get("started_at_note")), "started_at null without started_at_note")
    else:
        need(is_str(st) and ISO_TZ.match(st), f"started_at {st!r}")
        date, time = m.group(1), m.group(2)
        need(st[:19] == f"{date[:4]}-{date[4:6]}-{date[6:]}T{time[:2]}:{time[2:4]}:{time[4:]}",
             f"started_at {st} does not match run_id {doc['run_id']}")
    if "updated_at" in doc:
        need(is_str(doc["updated_at"]) and ISO_TZ.match(doc["updated_at"]), "updated_at")
        if st is not None:
            need(doc["updated_at"] >= st, "updated_at before started_at")
    if "manifest_revision" in doc:
        need(is_int(doc["manifest_revision"]) and doc["manifest_revision"] >= 1, "manifest_revision")
    if "input_devices" in doc:
        need(isinstance(doc["input_devices"], list), "input_devices must be a list")
        for d in doc["input_devices"]:
            need(isinstance(d, dict) and is_str(d.get("name")) and isinstance(d.get("virtual"), bool), f"input device {d!r}")
    if "capture" in doc and doc["capture"] is not None:
        c = doc["capture"]
        need(isinstance(c, dict) and is_str(c.get("path")) and is_int(c.get("fps")), "capture map")
    return "run_manifest", stats


def check_supplement(doc, base_dir):
    for k in ("record_kind", "run_id", "supplements_manifest", "supplements_manifest_sha256", "spec_sha256",
              "engine_appendix_sha256", "source_revision", "working_tree_dirty", "tester", "commands_or_steps"):
        need(k in doc, f"missing {k}")
    need(RUN_ID.match(doc["run_id"]), "run_id")
    for k in ("supplements_manifest_sha256", "spec_sha256", "engine_appendix_sha256"):
        sha_or_null(doc, k)
    need(HEX40.match(doc["source_revision"]), "source_revision")
    d = doc["working_tree_dirty"]
    need(d is None or isinstance(d, bool), "working_tree_dirty type")
    if d is None:
        need(is_str(doc.get("working_tree_dirty_note")), "null dirty state needs working_tree_dirty_note")
    stats = {}
    if "source_file_hash_manifest" in doc:
        stats = check_file_list(doc["source_file_hash_manifest"], base_dir, doc)
    return "supplement_packet", stats


def check_correction(doc, base_dir):
    for k in ("record_kind", "corrects", "corrects_sha256", "reason", "corrected"):
        need(k in doc, f"missing {k}")
    sha_or_null(doc, "corrects_sha256")
    need(isinstance(doc["corrected"], dict), "corrected must be a mapping")
    inner = doc["corrected"]
    if inner.get("record_kind") == "supplement_packet":
        kind, stats = check_supplement(inner, base_dir)
    else:
        kind, stats = check_run_manifest(inner, base_dir)
    return "correction/" + kind, stats


def validate(path):
    with open(path, encoding="utf-8") as f:
        doc = yaml.safe_load(f)
    need(isinstance(doc, dict), "top level must be a mapping")
    base = os.path.dirname(os.path.abspath(path))
    kind = doc.get("record_kind", "run_manifest")
    if kind == "supplement_packet":
        return check_supplement(doc, base)
    if kind == "manifest_correction":
        return check_correction(doc, base)
    return check_run_manifest(doc, base)


def main(argv):
    expect_fail = False
    ok = True
    files = []
    it = iter(argv)
    for a in it:
        if a == "--repo":
            OPTS["repo"] = next(it)
        elif a == "--worktree":
            OPTS["worktree"] = True
        elif a == "--expect-fail":
            expect_fail = True
        else:
            files.append((a, expect_fail))
    for a, ef in files:
        try:
            kind, stats = validate(a)
            res = {"file": a, "result": "valid", "kind": kind, **stats}
        except yaml.YAMLError as e:
            res = {"file": a, "result": "parse_error", "error": str(e).replace("\n", " ")}
        except Bad as e:
            res = {"file": a, "result": "invalid", "error": str(e)}
        res["expected"] = "fail" if ef else "valid"
        res["pass"] = (res["result"] != "valid") if ef else (res["result"] == "valid")
        ok &= res["pass"]
        print(json.dumps(res, ensure_ascii=False))
    print(json.dumps({"pyyaml": yaml.__version__, "repo": OPTS["repo"], "worktree": OPTS["worktree"], "all_pass": ok}))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
