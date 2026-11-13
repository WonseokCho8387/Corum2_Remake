"""WP-11A-R1 input scripts (scene WP11_R1). Writes wp11r1_main.txt and wp11r1_fault.txt.
  TC-08-02  A2R1Attack / A2R1Wait / A2R1Stagger / A2R1Down: enter range, leave, re-enter while the chaser is restricted
  TC-08-12  GAirg1..g3: the gauntlet boss parried in the air in its normal / super armor / launch immune state
  TC-UNITY-02  U2BodyRun / U2BodyDash / U2BodyDive / U2BodyKnock / U2BodyEnemy (rendered mesh vs judged body)
  TC-UNITY-07  U7Late / U7LateCast: external hit signals during startup, active, after the end, after a cancelled cast
"""
import sys
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
T = {k: float(v) for k, v in (a.split("=") for a in sys.argv[1:])}  # timing overrides for probes
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


def kb(o, key, d):
    return [(o, f"kb={key}:down"), (o + d, f"kb={key}:up")]


def fire(o):  # fireball command: down, down-forward(right), attack
    return [(o, "kb=S:down"), (o + 0.03, "kb=D:down"), (o + 0.06, "kb=S:up"), (o + 0.09, "kbtap=J"), (o + 0.2, "kb=D:up")]


def enter(z, e):  # walk right until the chaser starts its attack (the player has just entered its range), then stop
    return [(0, f"mark={z}"), (0, f"spawn={z}"), (0.2, "kb=D:down"), (0.2, f"await={e}:AttackStartup:4"), (0.2, "kb=D:up")]


def main():
    add(0.2, (0, "hp=100000"))
    add(0.1, (0, "tc=TC-08-02"))
    # attacking: leave during its startup, come back while its attack is still running (startup 0.3 + active 0.1 + recovery 0.3)
    a = 0.2
    add(4.0, *enter("A2R1Attack", "ra"), *kb(a + 0.02, "A", T.get("atkOut", 0.25)), *kb(a + 0.30, "D", T.get("atkIn", 0.25)))
    # re-attack wait: leave, let the attack finish, come back before the interval (1.2 s) is over
    add(4.5, *enter("A2R1Wait", "rw"), *kb(a + 0.02, "A", T.get("waitOut", 0.6)), *kb(a + T.get("waitBack", 0.75), "D", T.get("waitIn", 0.6)))
    # staggered (1.5 s): our hit cancels its startup, leave, come back while it is still staggered
    add(5.0, *enter("A2R1Stagger", "rs"), (a + 0.02, "kbtap=J"), *kb(a + 0.45, "A", T.get("stgOut", 0.3)), *kb(a + 0.8, "D", T.get("stgIn", 0.3)))
    # down (2.5 s): launched, leave, come back while it is down
    add(7.0, *enter("A2R1Down", "rd"), (a + 0.02, "kbtap=K"), (a + 0.05, "await=rd:Down:4"), *kb(a + 0.1, "A", T.get("dnOut", 0.3)),
        *kb(a + 0.5, "D", T.get("dnIn", 0.3)))
    # ---- TC-08-12 boss air parry
    add(0.1, (0, "tc=TC-08-12"))
    for st in ("g1", "g2", "g3"):
        b = "a" + st
        pre = [(0, f"spawn=GAir{st}")]
        if st in ("g2", "g3"):
            pre += [(0.02, f"bossevent={b}:arm")]
        if st == "g3":
            pre += [(0.65, f"bossevent={b}:lift")]
        o = 1.3
        cmds = pre + [(o, f"await={b}:AttackStartup:5"), (o, f"mark=GAir{st}"), (o + T.get("jump", 0.40), "kbtap=Space")] + kb(o + T.get("rb", 0.46), "L", 0.4)
        add(o + 2.4, *cmds)
    # ---- TC-UNITY-02 rendered mesh vs judged body
    body()
    # ---- TC-UNITY-07 late signals
    add(0.1, (0, "tc=TC-UNITY-07"))
    burst = [(0.10 + 0.04 * i, f"hitsignal=current:{tg}") for i in range(12) for tg in ("lb", "lf")]
    add(2.0, (0, "mark=U7Late"), (0, "spawn=U7Late"), (0.08, "kbtap=J"), *burst, (0.9, "hitsignal=last:lb"), (1.0, "hitsignal=last:lf"))
    # the cast's 15-tick startup: a signal for the casting instance while it is still starting, then one after the hit cancelled it
    add(2.5, (0, "mark=U7LateCast"), (0, "spawn=U7LateCast"), (0.02, "await=lc:Idle:6"), (0.02, "await=lc:AttackStartup:6"), *fire(0.12),
        (T.get("castSig", 0.28), "hitsignal=current:lc"), (0.9, "hitsignal=last:lc"), (1.2, "hitsignal=current:lc"))


def body():
    add(0.1, (0, "tc=TC-UNITY-02"))
    add(3.0, (0, "mark=U2BodyRun"), (0, "spawn=U2BodyMove"), (0.1, "kbtap=D"), *kb(0.2, "D", 2.2))
    add(3.0, (0, "mark=U2BodyDash"), (0, "spawn=U2BodyMove"), (0.1, "kb=D:down"), (1.0, "kbtap=Space"), (1.15, "kbtap=J"), (1.9, "kb=D:up"))
    add(3.0, (0, "mark=U2BodyDive"), (0, "spawn=U2BodyMove"), (0.1, "kb=D:down"), (1.0, "kbtap=Space"), (1.15, "kbtap=K"), (1.9, "kb=D:up"))
    add(2.5, (0, "mark=U2BodyKnock"), (0, "spawn=U2BodyKnock"), (0.02, "await=bk:Idle:6"), (0.02, "await=bk:AttackActive:6"))
    add(6.0, (0, "mark=U2BodyEnemy"), (0, "spawn=U2BodyEnemy"), *fire(0.1), (0.9, "kbtap=K"), (1.0, "await=bl:Down:4"), (1.05, "await=bl:Idle:6"),
        (1.1, "ehp=bl:1"), (1.12, "kbtap=J"))


main()
out = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"
open(S + "wp11r1_main.txt", "w", encoding="utf-8", newline="").write(out)
print("main", len(out), round(t, 1), "s")
# negative control: the same body scenes with the drawn bodies shifted 0.3 m (view only)
seg, t = [], 0.0
add(0.2, (0, "hp=100000"), (0.01, "bodyfault=player:0.3"), (0.02, "bodyfault=enemy:0.3"))
body()
out = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"
open(S + "wp11r1_fault.txt", "w", encoding="utf-8", newline="").write(out)
print("fault", len(out), round(t, 1), "s")
