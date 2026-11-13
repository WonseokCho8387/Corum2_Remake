# R6: WP-10 held boss events (GPT R4 review section 3, over-rejection)

- Core keeps a boss's raised events in a held set across phases and removes a name only when an event transition uses it.
  `Tools/wp10_checks.py` now checks an event transition against that: the event is raised by the same boss before the transition
  (by event order) and after the last transition of that boss that consumed the same name.
- Same-tick order now comes from eventId, so an event logged after its transition no longer counts (new self-test).
- Original WP-10-R1 runs: same verdicts as R4 (`checks/wp10_checks.json`). Self-tests: 40 of 40 as expected.
- Under the P0 verification bar the user set on 2026-11-14, the other R4 / WP-11A-R1 findings (logs tampered by hand that a checker
  still passes) are non-blocking notes and were not changed.
