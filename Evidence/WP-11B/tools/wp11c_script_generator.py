"""WP-11 Part C input script (scene WP11_Ops, zone O5Combo: one bag) for TC-UNITY-05 (observation) and TC-UNITY-06.
python wp11c_gen.py <hz 50|60> -> wp11c_<hz>.txt
Phases (markers):
  C_Normal  jump, attack, guard at normal speed
  C_Slow    attack, ring opened during it (world x0.2), closed again
  C_Pause   attack, then a test stop (timeScale 0) of 25 rendered frames; a jump pressed on the stop frame
  C_Delay   one rendered frame of 0.1 s (several ticks): attack pressed and released inside that frame, Right held across frames
  C_RingTx  ring opened and attack pressed on the same frame; ring closed and jump pressed on the same frame
"""
import sys
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
hz = int(sys.argv[1]) if len(sys.argv) > 1 else 50
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.3f}:{c};" for o, c in cmds))
    t += dur


pre = [(0, "hp=100000"), (0, "tc=TC-UNITY-05")]
if hz == 60:
    pre.append((0, "fixeddt=0.0166667"))
add(0.3, *pre, (0.05, "spawn=O5Combo"), (0.1, "framelog=1"))
add(2.0, (0, "mark=C_Normal"), (0.1, "kbtap=Space"), (1.0, "kbtap=J"), (1.5, "kb=L:down"), (1.8, "kb=L:up"))
add(1.5, (0, "mark=C_Slow"), (0.1, "kbtap=J"), (0.14, "kbtap=Q"), (0.6, "kbtap=Q"))
add(1.5, (0, "mark=C_Pause"), (0.1, "kbtap=J"), (0.14, "pause=25"), (0.14, "kbtap=Space"))
add(1.5, (0, "tc=TC-UNITY-06"), (0, "mark=C_Delay"), (0.1, "kb=D:down"), (0.3, "framedt=0.1"), (0.3, "kb=J:down"), (0.3, "kb=J:up"),
    (0.4, "framedt=restore"), (0.7, "kb=D:up"))
add(1.5, (0, "mark=C_RingTx"), (0.1, "kb=Q:down"), (0.1, "kb=J:down"), (0.12, "kb=Q:up"), (0.12, "kb=J:up"),
    (0.6, "kb=Q:down"), (0.6, "kb=Space:down"), (0.62, "kb=Q:up"), (0.62, "kb=Space:up"))
add(0.3, (0, "framelog=0"), *([(0.05, "fixeddt=0.02")] if hz == 60 else []))
out = "".join(seg) + f"{t:.3f}:mark=ScriptEnd;{t + 0.1:.3f}:flush"
open(S + f"wp11c_{hz}.txt", "w", encoding="utf-8", newline="").write(out)
print(hz, len(out), round(t, 1), "s")
