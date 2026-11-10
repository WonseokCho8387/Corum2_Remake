# WP-02-R2 evidence (GPT WP-02-R1 review: WP02-M01-R1-A/B)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-02-R2_Report_Claude_v1.0.md`. Source of the code change and the check run: `6ad45db` (clean tree, recorded in the manifest). Only the manifest writer changed; no game logic, data or scene changed.

- `runs/` — `RUN-20261110-092516`: a short check run of the new manifest path. It is **not** new combat evidence; F01–F03 evidence stays in `Evidence/WP-02-R1`.
  - `*.manifest.at-start.yaml` — copy taken right after Play started (`manifest_revision: 1`).
  - `*.manifest.yaml` — final file after 4 more rewrites: virtual devices added (2 fields, 2 rewrites), recorder start, input script start (`manifest_revision: 5`). `run_id` and `started_at` are the same in both copies; `updated_at` and `manifest_revision` changed.
  - `*.ndjson`, `*.data.json`, `*.commands.txt`, `r2.report.txt` (made with `Tools/wp02_log_report.py`).
- `video/` — `r2-RUN-20261110-092516.mp4` (228 frames, 1280x800, 50 fps game time) and a PNG still per second. It shows only the check run; it exists because the recorder start is one of the manifest rewrite paths.
- `corrections/` — version-2 correction records for the four R1 files that PyYAML could not parse. Each names the original (`corrects`) and its sha256 (`corrects_sha256`), lists the changes and the reason, and holds the corrected record under `corrected`. The originals in `Evidence/WP-02-R1` are unchanged.
  - `tools/make_corrections.py` generates them from the original text (run from the repository root). The only value not taken from the original text is the git tree id of the recorded source revision, marked as added.
  - R1 manifests: `started_at` is `null` with a note, because the original value was the last rewrite time. `run_id_clock` shows the time in the run id for reference only.
- `validation/validate_results.ndjson` — `python Tools/validate_manifest.py` output (PyYAML 6.0.3 `safe_load` + field type checks): the 2 new manifest copies and 4 corrections are valid; the 4 R1 originals still fail to parse (expected). `all_pass: true`.
- `EV-WP02R2-A01_*` — EditMode 140/140 (132 earlier + 8 `ManifestYamlTests`). The tests ran on the working tree that was then committed unchanged as `6ad45db`.
- `evidence_steps_r2.sh`, `evidence_helpers.sh` — the driving script.

Claude self-check artifacts, not GPT verdicts.
