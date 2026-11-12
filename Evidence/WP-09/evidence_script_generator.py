"""Generates wp09_evidence.sh: P0-01..03 residual evidence.
  main  (WP06_ComboState scene): TC-02-01 keyboard / pad mapping, TC-03-01 strong stick, TC-03-06/07/08/09 air rules and landing.
  plane (WP04_AI scene): TC-01-01 rendered depth of the player, moving / jumping AI enemies and the boss.
"""
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


def kb(o, key, d):
    return [(o, f"kb={key}:down"), (o + d, f"kb={key}:up")]


add(0.2, (0, "hp=1000"))
# TC-02-01: each confirmed action from the keyboard and from the pad (virtual devices; real devices are BLOCKED_ENV)
add(1.9, (0, "tc=TC-02-01"), (0, "spawn=Start"), (0.05, "mark=MapJump"), (0.1, "kbtap=Space"), (1.0, "padtap=A"))
add(1.2, (0.0, "mark=MapAttack"), (0.1, "kbtap=J"), (0.6, "padtap=X"))
add(1.6, (0.0, "mark=MapSpecial"), (0.1, "kbtap=K"), (0.8, "padtap=Y"))
add(1.0, (0.0, "mark=MapGuard"), *kb(0.1, "L", 0.2), (0.5, "padbtn=RightShoulder:down"), (0.7, "padbtn=RightShoulder:up"))
add(1.2, (0.0, "mark=MapMove"), *kb(0.1, "D", 0.3), (0.6, "pad=1,0"), (0.9, "pad=0,0"))
add(2.6, (0.0, "mark=MapRing"), (0.1, "kbtap=Q"), (0.3, "kbtap=Z"), (0.5, "kbtap=C"), (0.7, "kbtap=E"), (0.9, "kbtap=Q"),
    (1.2, "padtap=LeftShoulder"), (1.4, "padtap=LeftTrigger"), (1.6, "padtap=RightTrigger"), (1.8, "padtap=B"), (2.0, "padtap=LeftShoulder"))
add(2.0, (0.0, "mark=MapQuickslot"), (0.1, "kbtap=Digit3"), (1.4, "padtap=DpadDown"))  # quickslot down = fire scroll (WP05 ring data)
# TC-03-01: strong stick tilt only = walk; double tap and hold = run; release ends the run (pad and keyboard)
add(1.6, (0, "tc=TC-03-01"), (0, "spawn=Start"), (0.05, "mark=StrongTiltWalk"), (0.1, "pad=1,0"), (1.2, "pad=0,0"))
add(1.6, (0, "spawn=Start"), (0.05, "mark=PadDoubleTapRun"), (0.1, "pad=1,0"), (0.16, "pad=0,0"), (0.22, "pad=1,0"), (1.2, "pad=0,0"))
add(1.6, (0, "spawn=Start"), (0.05, "mark=KeyDoubleTapRun"), (0.1, "kb=D:down"), (0.16, "kb=D:up"), (0.22, "kb=D:down"), (1.2, "kb=D:up"))
# TC-03-06: in the air: direction only / double tap / A / direction+attack / direction+special / RB
J = "kbtap=Space"
add(1.4, (0, "tc=TC-03-06"), (0, "spawn=Start"), (0.05, "mark=AirDirectionOnly"), (0.1, J), *kb(0.25, "D", 0.4))
add(1.4, (0, "spawn=Start"), (0.05, "mark=AirDoubleTap"), (0.1, J), (0.25, "kb=D:down"), (0.3, "kb=D:up"), (0.36, "kb=D:down"), (0.6, "kb=D:up"))
add(1.6, (0, "spawn=Start"), (0.05, "mark=AirA"), (0.1, J), (0.4, J))
add(1.4, (0, "spawn=Start"), (0.05, "mark=AirDirAttack"), (0.1, J), (0.3, "kb=D:down"), (0.32, "kbtap=J"), (0.5, "kb=D:up"))
add(1.4, (0, "spawn=Start"), (0.05, "mark=AirDirSpecial"), (0.1, J), (0.3, "kb=D:down"), (0.32, "kbtap=K"), (0.5, "kb=D:up"))
add(1.4, (0, "spawn=Start"), (0.05, "mark=AirRB"), (0.1, J), *kb(0.3, "L", 0.3))
# TC-03-07: jump -> dash -> extra jump; dash -> extra jump order swapped; each only once; air normal attack does not use them
add(2.0, (0, "tc=TC-03-07"), (0, "spawn=Start"), (0.05, "mark=DashThenJump"), (0.1, J), (0.25, "kb=D:down"), (0.27, "kbtap=J"), (0.4, "kb=D:up"),
    (0.6, J), (0.75, "kb=D:down"), (0.77, "kbtap=J"), (0.85, "kb=D:up"), (0.95, J))
add(2.0, (0, "spawn=Start"), (0.05, "mark=JumpThenDash"), (0.1, J), (0.3, J), (0.5, "kb=D:down"), (0.52, "kbtap=J"), (0.6, "kb=D:up"), (0.9, J))
add(2.0, (0, "spawn=Start"), (0.05, "mark=AirAttackKeepsCounts"), (0.1, J), (0.25, "kbtap=J"), (0.6, J), (0.75, "kb=D:down"), (0.77, "kbtap=J"), (0.9, "kb=D:up"))
# TC-03-08: dive first, then A and dash
add(1.6, (0, "tc=TC-03-08"), (0, "spawn=Start"), (0.05, "mark=DiveThenAAndDash"), (0.1, J), (0.35, "kb=D:down"), (0.37, "kbtap=K"), (0.4, J), (0.43, "kbtap=J"), (0.6, "kb=D:up"))
# TC-03-09: after a normal landing: attack, guard and a new jump at once (counts reset)
add(1.6, (0, "tc=TC-03-09"), (0, "spawn=Start"), (0.05, "mark=LandThenAttack"), (0.1, J), (0.3, J), (0.1 + 0.02 * 51 + 0.02, "kbtap=J"))
add(1.6, (0, "spawn=Start"), (0.05, "mark=LandThenGuard"), (0.1, J), *kb(0.1 + 0.02 * 38 + 0.02, "L", 0.2))
add(2.0, (0, "spawn=Start"), (0.05, "mark=LandThenJumpAgain"), (0.1, J), (0.3, J), (0.1 + 0.02 * 51 + 0.02, J), (1.45, J))
main = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"

# plane run (WP04_AI): player walks / jumps through the AI zones; chasers, jumpers, fleers and the boss move
seg.clear(); t = 0.0
add(0.2, (0, "hp=100000"), (0, "tc=TC-01-01"))
for z in ("Z2Chaser", "Z3Fleer", "Z5Jumper", "Z6Ranged", "Z7Boss"):
    add(3.2, (0, "spawn=" + z), (0.05, "mark=Plane_" + z), (0.2, "kbtap=J"), (0.6, J), *kb(1.2, "A", 0.5), (1.9, J), *kb(2.2, "D", 0.5))
plane = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"

s = open(S + "wp06_evidence.sh", encoding="utf-8").read()
head, rest = s.split("MAIN='", 1)
rest = rest.split('case "$KIND" in', 1)[1]
head = head.replace("WP-06 evidence: P0-04 / P0-05 residual TCs on the WP06_ComboState scene", "WP-09 evidence: P0-01..03 residual TCs (main: WP06_ComboState scene; plane: WP04_AI scene)")
head = head.replace("KIND = main | cw (TC-04-09 combo-window run; the window is set by the caller)", "KIND = main | plane")
head = head.replace("Diagnostics/WP06", "Diagnostics/WP09")
rest = rest.replace('cw) S="$CW" ;;', 'plane) S="$PLANE"; SCENE=Assets/Corum2/Scenes/WP04_AI.unity ;;')
rest = rest.replace('*) S="$MAIN" ;;', '*) S="$MAIN"; SCENE=Assets/Corum2/Scenes/WP06_ComboState.unity ;;')
rest = rest.replace('OpenScene("Assets/Corum2/Scenes/WP06_ComboState.unity")', 'OpenScene(\\"$SCENE\\")')
rest = rest.replace("ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\\\"$SCENE\\\"); return \"opened\";'", "ev \"UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\\\"$SCENE\\\"); return \\\"opened\\\";\"")
rest = rest.replace("Diagnostics/WP06", "Diagnostics/WP09")
out = head + "MAIN='" + main + "'\n\nPLANE='" + plane + "'\n\ncase \"$KIND\" in" + rest
open(S + "wp09_evidence.sh", "w", encoding="utf-8").write(out)
print(round(t, 2))
