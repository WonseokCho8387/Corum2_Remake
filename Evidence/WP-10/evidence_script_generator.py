"""Generates wp10_evidence.sh: integration scenarios (master 13.6).
  main  (WP10_Integration): TC-INT-01, 03, 04, 05, 06
  int07 (WP03_Ring):        TC-INT-07
  int08 (WP05_MagicDeath):  TC-INT-08
"""
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
LAG = 0.02
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


def kb(o, key, d):
    return [(o, f"kb={key}:down"), (o + d, f"kb={key}:up")]


def fire(o):
    return [(o, "kb=S:down"), (o + 0.03, "kb=D:down"), (o + 0.06, "kb=S:up"), (o + 0.09, "kbtap=J"), (o + 0.2, "kb=D:up")]


def flush(name):
    global t
    out = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"
    seg.clear()
    t = 0.0
    return out


J = "kbtap=Space"
# ---------------- main
add(0.2, (0, "hp=100000"))
# TC-INT-01: standard chain on 10 mixed targets (3 die, 2 launch-immune), dive area on
add(5.5, (0, "tc=TC-INT-01"), (0, "cunlock=0,1"), (0, "spawn=I1Mix"), (0.05, "mark=TenMixedTargets"),
    (0.3, "kbtap=J"), (0.6, "kbtap=J"), (0.9, "kbtap=J"), (1.2, "kbtap=J"), (1.5, "kbtap=K"), (2.2, J), (2.4, "kbtap=J"), (2.55, "kbtap=J"), (2.85, "kbtap=J"),
    (3.15, "kbtap=J"), (3.45, "kb=D:down"), (3.5, "kbtap=K"), (3.7, "kb=D:up"), (5.4, "cunlock=0,0"))
# TC-INT-03: dash -> remaining extra jump -> dive; RB and attack during each motion
add(3.0, (0, "tc=TC-INT-03"), (0, "cunlock=1,0"), (0, "spawn=I3Air"), (0.05, "mark=DashJumpDiveWithInputs"), (0.1, J),
    (0.25, "kb=D:down"), (0.27, "kbtap=J"), (0.3, "kb=D:up"), (0.31, "kbtap=L"), (0.35, "kbtap=J"),
    (0.6, J), (0.62, "kbtap=L"), (0.85, "kb=D:down"), (0.87, "kbtap=K"), (0.9, "kb=D:up"), (0.91, "kbtap=L"), (0.93, "kbtap=J"), (2.9, "cunlock=0,0"))
# TC-INT-04: boss in its super-armor phase (p2 below 60 % HP): physical alone, magic alone, simultaneous
add(1.2, (0, "tc=TC-INT-04"), (0, "spawn=I4ArmorFar"), (0.05, "mark=BossPhase2Setup"), (0.1, "ehp=wb:110"), (0.1, "ehp=wb3:110"), (0.1, "ehp=wb4:110"))
add(2.5, (0, "spawn=I4Armor"), (0, "await=wb:AttackStartup:6"), (0.0, "mark=BossArmorPhysical"), (0.05, "kbtap=J"))
add(2.5, (0, "spawn=I4ArmorBFar"), (0.5, "spawn=I4ArmorB"), (0.5, "await=wb3:Idle:6"), (0.5, "await=wb3:AttackStartup:6"), (0.5, "mark=BossArmorMagic"), *fire(0.5))  # wb3 p2 startup 0.7 s (attack speed 0.5)
add(2.5, (0, "spawn=I4ArmorCFar"), (0.5, "spawn=I4ArmorC"), (0.5, "await=wb4:AttackStartup:6"), (0.5, "mark=BossArmorSimultaneous"), (0.5 + 0.18, "kbtap=J"))  # p2 crush is active 12 ticks after its start
# TC-INT-05: four AI types together; guard, attack, roll, jump; observe attacks, constraints and locks
add(9.0, (0, "tc=TC-INT-05"), (0, "spawn=I5Mixed"), (0.05, "mark=MixedAiBattle"), *kb(0.5, "L", 1.5), (2.3, "kbtap=J"), (2.6, "kbtap=J"),
    (3.2, "kb=S:down"), (3.22, "kb=D:down"), (3.25, J), (3.4, "kb=D:up"), (3.42, "kb=S:up"), *kb(4.0, "L", 1.5), (6.0, J), (6.2, "kbtap=J"), *kb(7.0, "L", 1.5))
# TC-INT-06: two bosses: HP transition (warden) and event transition (sentinel) in the same fight
add(8.0, (0, "tc=TC-INT-06"), (0, "spawn=I6Bosses"), (0.05, "mark=TwoBossTransitions"), (1.0, "ehp=wb2:110"), (1.05, "kbtap=J"), (3.0, "bossevent=sn:shift"), *kb(4.0, "L", 2.0))
main = flush("main")

# ---------------- TC-INT-07 (WP03 ring scene, pad)
LB = "padtap=LeftShoulder"
add(0.2, (0, "tc=TC-INT-07"), (0, "spawn=TestBags"))
add(1.2, (0, "mark=RegisterThenCancel"), (0.1, LB), (0.15, "pad=0,1"), (0.25, "padtap=Y"), (0.4, "padtap=B"), (0.55, "pad=0,0"))
add(1.0, (0, "mark=QuestDiscardRefused"), (0.05, "pad=0,-1"), (0.2, "padtap=B"), (0.4, "padtap=B"), (0.6, "pad=0,0"))
add(1.2, (0, "mark=NormalDiscardDone"), (0.05, "pad=-0.7,-0.7"), (0.2, "padtap=B"), (0.4, "padtap=A"), (0.45, "pad=0,0"))
add(1.6, (0, "mark=RegisterNeutralDpad"), (0.1, LB), (0.15, "pad=0,1"), (0.25, "padtap=Y"), (0.35, "padtap=DpadUp"), (0.45, "pad=0,0"), (0.6, "padtap=DpadUp"))
add(1.0, (0, "mark=AfterClose"), (0.3, "padtap=X"))
int07 = flush("int07")

# ---------------- TC-INT-08 (WP05 death scene, keyboard + pad): wakeup / dead targets, revive, decline, game over menus
add(0.2, (0, "tc=TC-INT-08"), (0, "hp=1000"))
add(3.5, (0, "spawn=TestBags"), (0.05, "mark=WakeupTargetAttack"), (0.1, "kbtap=K"), (0.2, "await=bag1:Wakeup:6"), (0.22, "kbtap=J"), (0.5, "kbtap=J"))
add(3.0, (0, "spawn=TestCluster"), (0.05, "mark=DeadTargetAttack"), *[(0.2 + 0.3 * i, "kbtap=J") for i in range(6)])
add(6.0, (0, "spawn=TestHitter"), (0, "item=feather:1"), (0, "hp=1"), (0.05, "mark=DeathRevive"), (4.0, "padtap=A"))
add(6.0, (0, "item=feather:1"), (0, "hp=1"), (0.05, "mark=DeathDecline"), (4.0, "padtap=B"), (5.0, "pad=0,-1"), (5.1, "pad=0,0"), (5.3, "pad=0,-1"), (5.4, "pad=0,0"))
add(3.0, (0, "mark=SelectLobby"), (0.05, "padtap=A"), (0.8, "pad=0,1"), (0.9, "pad=0,0"), (1.2, "mark=SelectLoadSave"), (1.2, "padtap=A"),
    (1.8, "pad=0,1"), (1.9, "pad=0,0"), (2.2, "mark=SelectCheckpoint"), (2.2, "padtap=A"))
int08 = flush("int08")

s = open(S + "wp06_evidence.sh", encoding="utf-8").read()
head, rest = s.split("MAIN='", 1)
rest = rest.split('case "$KIND" in', 1)[1]
head = head.replace("WP-06 evidence: P0-04 / P0-05 residual TCs on the WP06_ComboState scene", "WP-10 evidence: integration TC-INT-01/03..08 (main: WP10_Integration, int07: WP03_Ring, int08: WP05_MagicDeath)")
head = head.replace("KIND = main | cw (TC-04-09 combo-window run; the window is set by the caller)", "KIND = main | int07 | int08")
head = head.replace("Diagnostics/WP06", "Diagnostics/WP10")
rest = rest.replace('  cw) S="$CW" ;;', '  int07) S="$INT07"; SCENE=Assets/Corum2/Scenes/WP03_Ring.unity ;;\n  int08) S="$INT08"; SCENE=Assets/Corum2/Scenes/WP05_MagicDeath.unity ;;')
rest = rest.replace('*) S="$MAIN" ;;', '*) S="$MAIN"; SCENE=Assets/Corum2/Scenes/WP10_Integration.unity ;;')
rest = rest.replace("ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\"Assets/Corum2/Scenes/WP06_ComboState.unity\"); return \"opened\";'",
                    "ev \"UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\\\"$SCENE\\\"); return \\\"opened\\\";\"")
rest = rest.replace("Diagnostics/WP06", "Diagnostics/WP10")
out = head + "MAIN='" + main + "'\n\nINT07='" + int07 + "'\n\nINT08='" + int08 + "'\n\ncase \"$KIND\" in" + rest
open(S + "wp10_evidence.sh", "w", encoding="utf-8").write(out)
print("ok")
