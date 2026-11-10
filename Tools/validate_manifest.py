#!/usr/bin/env python3
"""Parse and type-check run manifests and supplement packets (WP02-M01-R1-A/B).

Usage: python Tools/validate_manifest.py FILE... [--expect-fail FILE...]
Every file is read with PyYAML safe_load; then required fields, types and values are checked.
Files after --expect-fail must fail to parse or validate (used to show the original R1 files fail).
Exit code 0 only if every expectation holds. Prints one JSON line per file.
"""
import json
import re
import sys

import yaml

HEX64 = re.compile(r"^[0-9a-f]{64}$")
HEX40 = re.compile(r"^[0-9a-f]{40}$")
ISO_TZ = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$")
RUN_ID = re.compile(r"^RUN-(\d{8})-(\d{6})$")

# Master spec 13.3 template fields.
SPEC_13_3 = [
    "run_id", "spec_id", "spec_version", "engine_appendix_version", "engine_appendix_sha256", "spec_sha256",
    "build_id", "source_revision", "source_capture_mode", "working_tree_dirty", "dirty_diff_artifact",
    "source_snapshot_artifact", "source_file_hash_manifest", "engine", "engine_version", "platform_os",
    "device_input", "display", "render_fps_observed", "simulation_tick", "time_policy_version", "data_snapshot",
    "experimental_policies", "scene_map", "save_fixture", "random_seed", "started_at", "tester", "commands_or_steps",
]


class Bad(Exception):
    pass


def need(cond, msg):
    if not cond:
        raise Bad(msg)


def is_str(v):
    return isinstance(v, str)


def sha_or_null(doc, key, note_key=None):
    v = doc.get(key)
    if v is None:
        need(note_key is None or is_str(doc.get(note_key)), f"{key} is null without {note_key}")
        return
    need(is_str(v) and HEX64.match(v), f"{key} must be a 64-hex string, got {v!r}")


def check_run_manifest(doc):
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
        need(doc["source_revision"] is None or (is_str(doc["source_revision"]) and HEX40.match(doc["source_revision"])),
             "source_revision must be a 40-hex commit or null")
        dirty = doc["working_tree_dirty"]
        need(dirty is None or isinstance(dirty, bool), f"working_tree_dirty must be bool or null, got {dirty!r}")
        if dirty is True:
            need(isinstance(doc["dirty_diff_artifact"], list) and doc["dirty_diff_artifact"], "dirty without dirty_diff_artifact list")
        if dirty is False:
            need(doc["dirty_diff_artifact"] is None, "clean tree must have dirty_diff_artifact null")
        if dirty is None:
            need(is_str(doc.get("source_note")) or is_str(doc.get("working_tree_dirty_note")), "unknown dirty state needs a note")
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
        # run_id is taken from the same clock read as started_at (GameHost, M01-R1-B).
        date, time = m.group(1), m.group(2)
        need(st[:19] == f"{date[:4]}-{date[4:6]}-{date[6:]}T{time[:2]}:{time[2:4]}:{time[4:]}",
             f"started_at {st} does not match run_id {doc['run_id']}")
    if "updated_at" in doc:
        need(is_str(doc["updated_at"]) and ISO_TZ.match(doc["updated_at"]), "updated_at")
        if st is not None:
            need(doc["updated_at"] >= st, "updated_at before started_at")
    if "manifest_revision" in doc:
        need(isinstance(doc["manifest_revision"], int) and not isinstance(doc["manifest_revision"], bool)
             and doc["manifest_revision"] >= 1, "manifest_revision")
    if "input_devices" in doc:
        need(isinstance(doc["input_devices"], list), "input_devices must be a list")
        for d in doc["input_devices"]:
            need(isinstance(d, dict) and is_str(d.get("name")) and isinstance(d.get("virtual"), bool), f"input device {d!r}")
    if "capture" in doc and doc["capture"] is not None:
        c = doc["capture"]
        need(isinstance(c, dict) and is_str(c.get("path")) and isinstance(c.get("fps"), int), "capture map")
    return "run_manifest"


def check_supplement(doc):
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
    return "supplement_packet"


def check_correction(doc):
    for k in ("record_kind", "corrects", "corrects_sha256", "reason", "corrected"):
        need(k in doc, f"missing {k}")
    sha_or_null(doc, "corrects_sha256")
    need(isinstance(doc["corrected"], dict), "corrected must be a mapping")
    inner = doc["corrected"]
    kind = inner.get("record_kind", "run_manifest")
    return "correction/" + (check_supplement(inner) if kind == "supplement_packet" else check_run_manifest(inner))


def validate(path):
    with open(path, encoding="utf-8") as f:
        doc = yaml.safe_load(f)
    need(isinstance(doc, dict), "top level must be a mapping")
    kind = doc.get("record_kind", "run_manifest")
    if kind == "supplement_packet":
        return check_supplement(doc)
    if kind == "manifest_correction":
        return check_correction(doc)
    return check_run_manifest(doc)


def main(argv):
    expect_fail = False
    ok = True
    for a in argv:
        if a == "--expect-fail":
            expect_fail = True
            continue
        try:
            kind = validate(a)
            res = {"file": a, "result": "valid", "kind": kind}
        except yaml.YAMLError as e:
            res = {"file": a, "result": "parse_error", "error": str(e).replace("\n", " ")}
        except Bad as e:
            res = {"file": a, "result": "invalid", "error": str(e)}
        res["expected"] = "fail" if expect_fail else "valid"
        res["pass"] = (res["result"] != "valid") if expect_fail else (res["result"] == "valid")
        ok &= res["pass"]
        print(json.dumps(res, ensure_ascii=False))
    print(json.dumps({"pyyaml": yaml.__version__, "all_pass": ok}))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
