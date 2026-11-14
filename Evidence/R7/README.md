# R7: WP09-M02 ring deadzone float boundary (GPT R5-R6 review)

- `Tools/wp09_checks.py` `stick_neutral` now follows Core `Dir8Util.FromStick` arithmetic: float x, y and deadzone, `x*x + y*y` in float,
  `Math.Sqrt` in double. GPT's Core case (deadzone 0.35f, stick [0.35, 0]) is neutral, as in Core; the 0.5f/0.5 control stays non-neutral
  (`checks/float_boundary_controls.json`).
- The R4 boundary self-test now uses the smallest float stick that Core does not treat as neutral, since [dz, 0] itself can be neutral.
- Four WP-09-R1 runs: same verdicts as R5. Self-tests all as expected. GPT R1/R2/R3 replays: identical to R5.
