# WP-05 evidence: magic command (R-02-02), command vs scroll (R-02-10), player death flow (R-06-19 / TC-06-19a)

Report: `D:\ProjectR\Corum2\docs\GPT\CORUM2_WP-05_Report_Claude_v1.0.md`.

**Source**
- Run source: `31105d9`, clean tree, for both runs.
- Manifests are valid, with 321 rows each matching git.
- Content: `wp05-1` — `Data/WP05/RingConfig_WP05.asset`, scene `WP05_MagicDeath`, using the WP-02 arena, enemies and CombatConfig.

**Temporary commands (TEST-MAGIC-01, U-02; CF-01 undecided)**

| Command | Directions + button | Magic |
|---|---|---|
| fireball | ↓ ↘ → + X | `fire`, Attack, power 30, cast 0.2 s |
| cure | ↓ ↙ ← + RB | Heal +25, cast 0.25 s |
| might | ↓ ↙ ← + A | Buff ×1.5 for 4 s, cast 0.15 s |

- The Fire Scroll (D-pad down) uses the same `fire` data.
- The Phoenix Feather is the revive item.

## Runs

| Run | Script | Video |
|---|---|---|
| `RUN-20261112-074813` (main) | Commands, failed commands, buff, command vs scroll, revive used, revive declined, game-over menu (3 selections) | `video/main-RUN-20261112-074813.mp4`: 1502 frames, recording from tick 201, PNG stills every 50 frames |
| `RUN-20261112-074951` (nores) | No revive item: death goes straight to game over, then 2 selections | `video/nores-RUN-20261112-074951.mp4`: 414 frames, from tick 205 |

Script: `evidence_steps_wp05.sh <name> main|nores` (input through the Input System virtual gamepad).

- `item=<id>:<n>` and `hp=` are logged test setup (`TestSetItemCount`, `TestSetPlayerHp`).
- They are not game rules.

## Checks (`checks/wp05_checks_*.json`, `Tools/wp05_checks.py`)

**Main run, all pass**

TC-02-02 (magic commands)

| Tick | Command | Result |
|---|---|---|
| 264 | fireball | Magic hit at 273: D 30, `MagicStagger+Knockback(R-06-06)`. No normal attack. |
| 419 | cure | Heal at 431: HP 60 → 85. No guard. |
| 564 | might | Buff at 571. No jump. Buffed X hit at 607: D 10 × 1.5 = 15. |
| 674 | fireball | Cast as above. |

- Each match logs `competingNormalAction` (for example `Attack+Right@Walk`) for CF-01.
- Misses go to the normal action:
  - Tick 342: ↓, neutral, → + X gives a normal attack.
  - Tick 495: ← + RB gives a guard.

TC-02-10 (command vs scroll)
- Command at 674 and scroll at 765 both:
  - power 30, cast time 0.2;
  - effect 9 ticks after the cast start;
  - hit D 30.
- The scroll is consumed.
- The revive item from the D-pad left quickslot is refused at 819 (`ReviveOnlyOnDeath`, EXP-DEATH-05).

TC-06-19a (death flow)
- **Death 1 (tick 869):**
  - Motion 60 ticks (1.2 s), then the revive prompt (1 item).
  - Tick 1055: A, revived with HP 0 → 50 and 1.5 s invulnerability.
- **Death 2 (tick 1206):**
  - Prompt, then B to decline, which leads to game over.
  - Cursor moves (no wrap), then selections:
    - Lobby at 1440
    - LoadSave at 1500
    - CheckpointRestart at 1550
  - Every selection logs `returnPerformed=false` and `BLOCKED_SPEC(U-08)`.
- No enemy attack starts while the player is dead.

**Nores run**
- Death at 254 goes straight to game over after the motion.
- Selections: CheckpointRestart at 460, LoadSave at 505.
- `TC-06-19a` passes.
- Its `TC-02-02` and `TC-02-10` entries are false only because this run has no command or scroll section.

**Other files**
- `validation/validate_results.ndjson`
- `EV-WP05-A01_EditMode_TestResults.xml` / `_summary.txt`: EditMode 259/259 (243 + 16 new).

## Not covered

- **TC-06-19b** (actual return to a checkpoint, save or lobby): BLOCKED_SPEC (U-08). Selections are only logged and shown.
- **CF-01 conflict cases (TC-CF-01a–d):** BLOCKED_SPEC.
  - The temporary policy EXP-CMD-02 (a matched command takes the button) is a test choice, not a decision.
  - Not exercised in Play:
    - map ↑↓ points;
    - double-tap run;
    - slide or roll inputs combined with commands;
    - commands in the air.
- **Refused cast that falls back to the normal action:** EditMode only. So are a guard cancelling a cast, the longest-match rule and `CommandTakesButton=false`.
- **Magic visuals:** a block-out hitbox and HUD lines only. The death pose is a temporary lying block.

Claude self-check artifacts, not GPT verdicts.
