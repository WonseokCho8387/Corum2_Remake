# WP-02-R4 evidence (GPT WP-02-R3 review: WP02-M03)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-02-R4_Report_Claude_v1.0.md`.

Commits:
- `8c7015e` — capture code, Core rules, tests and validator.
- `a370bda` — validator only: collapsed `?? dir/` status entries. This is the run source.

No game logic, data or scene changed.

- `runs/dirty/RUN-20261110-114243` — dirty check run at `a370bda`. It is not combat evidence. Five temporary changes were made before the run and reverted after it:
  - `Tools/wp02_log_report.py` modified
  - `Tools/wp01_log_report.py` deleted
  - untracked file `Tools/r4_probe.txt`
  - untracked folder `Tools/r4_probe_dir/` (`a.txt`, `sub/b.bin` binary 1 KiB); git status shows it collapsed as `?? Tools/r4_probe_dir/`
  - out-of-scope untracked `r4_root_probe.txt`
  - What the run captured:
    - patch, 2 file diffs
    - 4 untracked copies
    - `required_untracked` — the 3 in-scope files
    - `capture_complete: true`
    - a list of 220 rows
  - The validator with `--repo .` rebuilt commit + patch + copies in a temporary git worktree. That rebuild reproduced all 220 rows (`rebuild_rows_matched: 220`).
- `video/dirty/` — short recording of that run.
- `validation/validate_results.ndjson` — `python Tools/validate_manifest.py --repo . ...`; `all_pass: true`.
  - Valid:
    - the R4 run, start copy and final copy
    - all R3 run manifests and corrections, unchanged files
  - Fail, as expected:
    - R2 files without a per-file list
    - an R1 original that does not parse
- `validation/negative_cases.ndjson` — `tools/negative_cases.py`: 12 damaged copies are rejected; 2 unchanged copies and 1 allowed change pass.
  - GPT's M03-A/B/C on the R3 dirty run, plus one more variant of B.
  - R4 cases:
    - copy missing inside an untracked folder
    - binary copy bytes changed
    - patch missing a file diff
    - patch and status both missing a file
    - patch text changed with sha256 updated (caught only by the rebuild)
    - capture marked incomplete
    - in-scope file in `untracked_skipped`
  - Allowed: an out-of-scope copy removed (not required) passes.
- `EV-WP02R4-A01_*` — EditMode 150/150 (145 earlier + 5 `DirtyCaptureTests`).

Claude self-check artifacts, not GPT verdicts.
