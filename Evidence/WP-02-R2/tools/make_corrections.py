#!/usr/bin/env python3
"""WP-02-R2: correction records for the four WP-02-R1 YAML files that PyYAML cannot parse (GPT WP02-M01-R1-A).

The originals stay unchanged. Each correction is a new file that names the original and its sha256, says what changed
and why, and holds the corrected record under `corrected`. Values are taken from the original text; nothing is
re-measured, except the git tree id of the recorded source revision (read from git, marked as added).

Run from the repository root: python Evidence/WP-02-R2/tools/make_corrections.py
"""
import hashlib
import os
import re
import subprocess

import yaml

OUT = "Evidence/WP-02-R2/corrections"
R1 = "Evidence/WP-02-R1"


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def raw_fields(path):
    """Line-level read of the unparseable originals: 'key: rest' split at the first ': ', list items kept."""
    fields, lists, comments, cur = {}, {}, [], None
    with open(path, encoding="utf-8") as f:
        for line in f.read().splitlines():
            if line.startswith("#"):
                comments.append(line[1:].strip())
            elif line.startswith("  - "):
                lists.setdefault(cur, []).append(line[4:].strip().strip('"'))
            elif line.strip():
                k, _, v = line.partition(":")
                cur = k
                fields[k] = v[1:] if v.startswith(" ") else v
    return fields, lists, comments


def hash_and_rest(text):
    m = re.match(r"^([0-9a-f]{64})\s*(.*)$", text)
    return (m.group(1), m.group(2).strip()) if m else (None, text)


def paren(text):
    m = re.match(r"^\((.*)\)$", text.strip())
    return m.group(1) if m else text.strip()


def git_tree(rev):
    return subprocess.check_output(["git", "rev-parse", rev + "^{tree}"], text=True).strip()


def capture_map(text, original_key="capture"):
    m = re.match(r"^(\S+) \((MediaEncoder H\.264) (\d+)x(\d+) (\d+)fps, (Time\.captureFramerate=\d+)[:;]? ?(.*)\)(.*)$", text)
    if not m:
        raise SystemExit("capture text not recognised: " + text)
    return {
        "path": m.group(1), "encoder": m.group(2), "width": int(m.group(3)), "height": int(m.group(4)),
        "fps": int(m.group(5)), "time_mode": m.group(6) + " (game time)",
        "note": (m.group(7) + m.group(8)).strip(),
    }


def r1_manifest(name):
    src = f"{R1}/runs/{name}.manifest.yaml"
    f, lists, _ = raw_fields(src)
    spec_hash, spec_rest = hash_and_rest(f["spec_sha256"])
    app_hash, app_rest = hash_and_rest(f["engine_appendix_sha256"])
    data_file, _, data_hash = f["data_snapshot"].partition(" sha256 ")
    cmd_file, _, cmd_hash = f["commands_or_steps"].partition(" sha256 ")
    disp = re.match(r"^(\d+)x(\d+) @([\d.]+)Hz$", f["display"])
    tick = re.match(r"^([\d.]+) s \(([\d.]+) Hz game time\)$", f["simulation_tick"])
    scene = re.match(r"^(\S+) start (\w+)/(\w+)$", f["scene_map"])
    dev_list, _, dev_note = f["input_devices"].partition("; ")
    devices = [{"name": d.replace("(virtual)", ""), "virtual": d.endswith("(virtual)")}
               for d in dev_list.strip("[]").split(", ")]
    assert f["source_dirty"] == "false (clean)", f["source_dirty"]
    run_id = f["run_id"]
    d8, t6 = run_id[4:12], run_id[13:19]
    corrected = {
        "run_id": run_id,
        "spec_id": f["spec_id"],
        "spec_version": f["spec_version"],
        "spec_file": paren(spec_rest),
        "spec_sha256": spec_hash,
        "engine_appendix_version": f["engine_appendix_version"],
        "engine_appendix_file": paren(app_rest),
        "engine_appendix_sha256": app_hash,
        "build_id": f["build_id"],
        "source_revision": f["source_revision"],
        "source_capture_mode": f["source_capture_mode"],
        "working_tree_dirty": False,
        "working_tree_dirty_original": f["source_dirty"],
        "dirty_diff_artifact": None,
        "source_snapshot_artifact": None,
        "source_file_hash_manifest": {
            "mode": "git-tree", "tree": git_tree(f["source_revision"]),
            "note": "added in this correction from git (tree of source_revision); not in the original manifest",
        },
        "engine": f["engine"],
        "engine_version": f["engine_version"],
        "platform_os": f["platform_os"],
        "device_input": f["device_input"],
        "display": {"width": int(disp.group(1)), "height": int(disp.group(2)), "refresh_hz": float(disp.group(3))},
        "render_fps_observed": None,
        "render_fps_note": "original value: " + f["render_fps_observed"],
        "simulation_tick": {"seconds": float(tick.group(1)), "hz": float(tick.group(2)), "clock": "game time"},
        "time_policy_version": f["time_policy_version"],
        "data_snapshot": data_file,
        "data_snapshot_sha256": data_hash,
        "data_version": f["data_version"].strip('"'),
        "experimental_policies": lists["experimental_policies"],
        "scene_map": {"scene": scene.group(1), "start_map": scene.group(2), "start_spawn": scene.group(3)},
        "save_fixture": None,
        "save_fixture_note": "original value: " + f["save_fixture"],
        "random_seed": None,
        "random_seed_note": "original value: " + f["random_seed"],
        "started_at": None,
        "started_at_note": (
            "not known. The original value " + f["started_at"] + " is the time of the last manifest rewrite "
            "(recorder start), not the run start (GPT WP02-M01-R1-B). See run_id_clock."
        ),
        "run_id_clock": {
            "local_time": f"{d8[:4]}-{d8[4:6]}-{d8[6:]}T{t6[:2]}:{t6[2:4]}:{t6[4:]}",
            "note": ("run_id is formatted from DateTime.Now when GameHost creates the run (source 7f79776, GameHost.Start); "
                     "second precision, no time zone in the id. Shown for reference; not recorded as started_at."),
        },
        "tester": f["tester"],
        "commands_or_steps": cmd_file,
        "commands_sha256": cmd_hash,
        "capture": dict(capture_map(f["capture"]),
                        repository_copy=f"{R1}/video/" + os.path.basename(capture_map(f["capture"])["path"])),
        "input_devices": devices,
        "input_devices_note": dev_note,
        "log": f["log"],
    }
    changes = [
        "all string values are YAML strings (the originals had unquoted ': ' in tester, capture and input_devices)",
        "source_dirty 'false (clean)' -> working_tree_dirty: false (13.3 name, boolean); original text kept",
        "hashes split from file names: spec/appendix/data_snapshot/commands",
        "display, simulation_tick, scene_map, capture, input_devices -> typed maps/lists from the same text",
        "render_fps_observed/save_fixture/random_seed -> null with the original text in *_note",
        "started_at -> null: the original value was overwritten by later rewrites (M01-R1-B)",
        "added: dirty_diff_artifact/source_snapshot_artifact null, source_file_hash_manifest (git tree)",
    ]
    return src, corrected, changes


def supplement(name, original_started_at):
    src = f"{R1}/supplement_WP-02/{name}.packet.yaml"
    f, _, comments = raw_fields(src)
    man_path, _, man_hash = f["supplements_manifest"].partition(" sha256 ")
    spec_hash, spec_rest = hash_and_rest(f["spec_sha256"])
    app_hash, app_rest = hash_and_rest(f["engine_appendix_sha256"])
    spec_file, _, spec_note = paren(spec_rest).partition(", ")
    app_file, _, app_note = paren(app_rest).partition(", ")
    m = re.match(r"^(\S+) sha256 ([0-9a-f]{64}) ?(.*)$", f["commands_or_steps"])
    cap = f["capture"]
    cap_main, _, cap_more = cap.partition(". ")
    corrected = {
        "record_kind": "supplement_packet",
        "run_id": f["run_id"],
        "packet_note": comments[0] if comments else "",
        "supplements_manifest": man_path,
        "supplements_manifest_sha256": man_hash,
        "spec_file": spec_file,
        "spec_sha256": spec_hash,
        "spec_file_note": spec_note,
        "engine_appendix_file": app_file,
        "engine_appendix_sha256": app_hash,
        "engine_appendix_file_note": app_note,
        "source_revision": f["source_revision"],
        "working_tree_dirty": None,
        "working_tree_dirty_note": f["source_dirty"],
        "started_at_note": (
            "the original WP-02 manifest's started_at " + original_started_at + " was written once at run start "
            "(source 4cf88ba writes the manifest only in GameHost.Start), so M01-R1-B does not apply to it"
        ),
        "tester": f["tester"],
        "commands_or_steps": m.group(1),
        "commands_sha256": m.group(2),
        "commands_note": m.group(3),
        "input_devices": [{"name": "Corum2VirtualKeyboard", "virtual": True}],
        "input_devices_note": f["input_devices"],
        "capture": capture_map(cap_main if cap_main.endswith(")") else cap),
    }
    if cap_more:
        corrected["capture"]["frame_note"] = cap_more
    changes = [
        "all string values are YAML strings (the original had unquoted ': ' in tester and capture)",
        "source_dirty text -> working_tree_dirty: null (not recorded at run time) with the original text in working_tree_dirty_note",
        "hashes split from paths and notes: supplements_manifest, spec, appendix, commands",
        "capture and input_devices -> typed map/list from the same text",
        "added started_at_note (why the original WP-02 started_at is not affected)",
    ]
    return src, corrected, changes


def wp02_started_at(name):
    with open(f"Evidence/WP-02/runs/{name}.manifest.yaml", encoding="utf-8") as f:
        for line in f:
            if line.startswith("started_at: "):
                return line.split(": ", 1)[1].strip()
    raise SystemExit("no started_at in WP-02 manifest " + name)


def write(src, corrected, changes, out_name, reason):
    doc = {
        "record_kind": "manifest_correction",
        "correction_id": "WP02-M01-R1-A/" + os.path.basename(src),
        "correction_version": 2,
        "corrects": src,
        "corrects_sha256": sha(src),
        "reason": reason,
        "original_kept": True,
        "changes": changes,
        "corrected": corrected,
    }
    os.makedirs(OUT, exist_ok=True)
    path = f"{OUT}/{out_name}"
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# WP-02-R2 correction record (version 2 of the file named in 'corrects'). The original is unchanged.\n")
        yaml.safe_dump(doc, f, sort_keys=False, allow_unicode=True, width=1_000_000)
    return path


def main():
    reason_a = "GPT WP02-M01-R1-A: PyYAML safe_load fails ('mapping values are not allowed here'); values not typed"
    out = []
    for name in ("RUN-20261110-075442", "RUN-20261110-075540"):
        src, c, ch = r1_manifest(name)
        out.append(write(src, c, ch, name + ".manifest.v2.yaml", reason_a + "; started_at overwritten (M01-R1-B)"))
    for name in ("RUN-20261110-020410", "RUN-20261110-020604"):
        src, c, ch = supplement(name, wp02_started_at(name))
        out.append(write(src, c, ch, name + ".packet.v2.yaml", reason_a))
    for p in out:
        print(p, sha(p))


if __name__ == "__main__":
    main()
