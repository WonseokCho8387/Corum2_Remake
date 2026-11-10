# WP-02-R3 evidence (GPT WP-02-R2 review: WP02-M02)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-02-R3_Report_Claude_v1.0.md`.
- Run source: `8a9a1a0` (manifest writer). The validator got one more check in `5715403` (Tools only, after the runs).
- No game logic, data or scene changed. Combat evidence stays in `Evidence/WP-02-R1`.

## Runs (check runs of the new source capture, not combat evidence)

- `runs/clean/RUN-20261110-102053` — clean tree at `8a9a1a0`.
  - `*.source_files.tsv` is the per-file list written at run start: 214 files of Assets/Packages/ProjectSettings/Tools, with path, size, sha256 (working-tree bytes) and git_blob.
  - The manifest's `source_file_hash_manifest` names this list and its sha256.
  - `*.manifest.at-start.yaml` is the copy taken at revision 1; the plain `*.manifest.yaml` is the final copy.
- `runs/dirty/RUN-20261110-102151` — the same commit with two deliberate temporary changes, both reverted after the run:
  - one appended comment line in `Tools/wp01_log_report.py`
  - an untracked file `Tools/r3_dirty_probe.txt`
  - What the run captured:
    - `working_tree_dirty: true`
    - `*.dirty.patch` (`git diff HEAD --binary`)
    - `*.untracked/` (a copy of the untracked file)
    - a list of 215 rows
- `video/` — short recordings of both runs (one of the manifest rewrite paths).

## Records for earlier runs

- `posthoc/` — per-file lists made **after** the runs, from the recorded source commit's git blobs (`bytes_basis` says so).
  - R1 runs `RUN-20261110-075442` / `-075540`: commit `7f79776`, 205 files.
  - R2 check run `RUN-20261110-092516`: commit `6ad45db`, 210 files.
- `corrections/` — new records that link the post-hoc lists. Each changes only `source_file_hash_manifest`, keeps the previous value inside it, and is marked `generated_after_run: true`.
  - `RUN-20261110-0754xx/0755xx.manifest.v3.yaml` — correct the R2 v2 records (`corrects` + sha256) and name the R1 originals.
  - `RUN-20261110-092516.manifest.v2.yaml` — corrects `Evidence/WP-02-R2/runs/RUN-20261110-092516.manifest.yaml`.
  - All earlier files are unchanged.
- WP-02 runs `RUN-20261110-020410` / `-020604` (source `4cf88ba`) were not given lists. The review asked only for the R1 and R2 clean runs.
- `tools/make_r3_records.py --generated-at 2026-11-10T10:40:00+09:00` regenerates `posthoc/`, `corrections/` and `checks/` byte-identically. Run it from the repository root.

## Checks

- `checks/posthoc_method_check.json` — the post-hoc method applied to the clean R3 run's commit gives the same 214 paths and git blobs as the list that run wrote at run start.
  - The sha256 values also match for all 214 files: in this working tree no file had line endings different from git's stored bytes.
- `checks/dirty_reconstruction.json` — commit + patch + untracked copy, rebuilt in a temporary git worktree, reproduce all 215 run-time rows (git_blob). The untracked file's bytes are equal too.
- `validation/validate_results.ndjson` — output of `python Tools/validate_manifest.py --repo . ...` (PyYAML 6.0.3).
  - Valid, as expected:
    - R3 run manifests: 4 files (2 runs, start and final copies)
    - the 3 new corrections
  - Fail, as expected:
    - the R2 run manifests and the R2 v2 corrections, which have no per-file list
    - the R1 originals, which fail to parse
  - The clean run was also re-hashed against the working tree (`--worktree`) right after the run: 214/214 equal.
- `validation/negative_cases.ndjson` — `tools/negative_cases.py`: 10 mutated manifest/list copies are all rejected and the 2 unmutated copies pass. The mutations include:
  - `source_file_hash_manifest: null` (the review's example)
  - a git-tree-only value
  - edited list rows
  - dirty-state mismatches
- `EV-WP02R3-A01_*` — EditMode 145/145 (140 earlier + 5 `SourceFileListTests`).

Claude self-check artifacts, not GPT verdicts.
