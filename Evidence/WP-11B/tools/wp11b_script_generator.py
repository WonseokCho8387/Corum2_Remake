"""WP-11 Part B input script (scene WP11_Ops): the same scenario in every run; only one Inspector value differs per variant.
  O1Jump   jump, second jump in the air            (extraJump)
  O2Dash   jump, air dash right                     (airDash)
  O3Pierce jump, air dash through three bags        (airDash, dashPierce)
  O4Dive   jump, dive right onto three bags         (diveArea)
  O5Combo  attack, second attack 0.4 s after the first ends (comboWindow 0.5 -> same chain; 0.3 -> new chain)
  O6Twins  attack the first of two instances of one definition (TC-UNITY-03 isolation)
Writes wp11b_main.txt."""
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


J = "kbtap=Space"
add(0.2, (0, "hp=100000"), (0, "tc=TC-OPS-01"))
add(2.5, (0, "mark=O1Jump"), (0, "spawn=O1Jump"), (0.1, J), (0.45, J))
add(2.5, (0, "mark=O2Dash"), (0, "spawn=O2Dash"), (0.1, J), (0.18, "kb=D:down"), (0.2, "kbtap=J"), (0.5, "kb=D:up"))
add(2.5, (0, "mark=O3Pierce"), (0, "spawn=O3Pierce"), (0.1, J), (0.18, "kb=D:down"), (0.2, "kbtap=J"), (0.5, "kb=D:up"))
add(2.5, (0, "mark=O4Dive"), (0, "spawn=O4Dive"), (0.1, J), (0.28, "kb=D:down"), (0.3, "kbtap=K"), (0.6, "kb=D:up"))
add(2.5, (0, "mark=O5Combo"), (0, "spawn=O5Combo"), (0.1, "kbtap=J"), (0.78, "kbtap=J"))
add(2.0, (0, "mark=O6Twins"), (0, "spawn=O6Twins"), (0.1, "kbtap=J"))
out = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"
open(S + "wp11b_main.txt", "w", encoding="utf-8", newline="").write(out)
print(len(out), round(t, 1), "s")
