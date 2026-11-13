"""WP-11 Part A input script (scene WP11_Evidence). Writes wp11a_main.txt.
Segments (markers) are what Tools/wp11_checks.py reads.
  TC-05-04  H5<Dev>_<Input>: hitter hit -> press during HitStun -> press again after HitStunEnd (Kb, Pad; None/Move/Jump/Attack/Special/Guard/Ring)
  TC-08-02  A2Ready, A2Stagger, A2Down, A2Dead: player enters a still chaser's range in each enemy state
  TC-08-05  J5NearIn/J5NearOut/J5FarIn/J5FarOut: two jumper configurations, distance inside / outside the window
  TC-08-09  B9Solo/B9Duo/B9Warden: 1 / 2 / 3 phase bosses driven past their HP thresholds
  TC-08-12  G<g1|g2|g3><Phys|Magic|Parry|Simul|Launch>: gauntlet boss in normal / super armor / launch immune state
"""
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


def kb(o, key, d):
    return [(o, f"kb={key}:down"), (o + d, f"kb={key}:up")]


def fire(o):  # fireball command: down, down-forward(right), attack
    return [(o, "kb=S:down"), (o + 0.03, "kb=D:down"), (o + 0.06, "kb=S:up"), (o + 0.09, "kbtap=J"), (o + 0.2, "kb=D:up")]


KB = {"Jump": lambda o: [(o, "kbtap=Space")], "Attack": lambda o: [(o, "kbtap=J")], "Special": lambda o: [(o, "kbtap=K")],
      "Guard": lambda o: [(o, "kbtap=L")], "Ring": lambda o: [(o, "kbtap=Q"), (o + 0.25, "kbtap=Q")],
      "Move": lambda o: kb(o, "A", 0.3)}
PAD = {"Jump": lambda o: [(o, "padtap=A")], "Attack": lambda o: [(o, "padtap=X")], "Special": lambda o: [(o, "padtap=Y")],
       "Guard": lambda o: [(o, "padtap=RightShoulder")], "Ring": lambda o: [(o, "padtap=LeftShoulder"), (o + 0.25, "padtap=LeftShoulder")],
       "Move": lambda o: [(o, "pad=-1,0"), (o + 0.3, "pad=0,0")]}

add(0.2, (0, "hp=100000"))
# ---------------- TC-05-04: HitStun lock (21 ticks); during = 0.1 s after the hit, after = 0.5 s after the hit
for dev, zone, hk, table in (("Kb", "H5Kb", "hk", KB), ("Pad", "H5Pad", "hp", PAD)):
    add(0.1, (0, "tc=TC-05-04"))
    for inp in ("None", "Move", "Jump", "Attack", "Special", "Guard", "Ring"):
        # wait for the hitter to be Idle (not mid-attack), then mark, then its next active attack is the hit of this case
        cmds = [(0, f"spawn={zone}"), (0.02, f"await={hk}:Idle:6"), (0.02, f"mark=H5{dev}_{inp}"), (0.02, f"await={hk}:AttackActive:6")]
        if inp != "None":
            cmds += table[inp](0.12) + table[inp](0.55)
        add(2.2, *cmds)
# ---------------- TC-08-02: still chaser (range 1.6, re-attack interval 1.2), player 4 m away
add(0.1, (0, "tc=TC-08-02"))
# each case: hold Right until the chaser starts its first attack (the player just entered its range), then release
def enter(z, e):
    return [(0, f"mark={z}"), (0, f"spawn={z}"), (0.2, "kb=D:down"), (0.2, f"await={e}:AttackStartup:4"), (0.2, "kb=D:up")]
add(4.5, *enter("A2Ready", "pr")[:-1], (0.6, "kb=D:up"))                                    # ready -> first chance; walks deeper in, stays: cooldown -> re-attack
add(4.0, *enter("A2Stagger", "ps"), (0.22, "kbtap=J"))                                       # our hit cancels its startup -> stagger in range -> re-decide
add(6.0, *enter("A2Down", "pd"), (0.22, "kbtap=K"), (0.3, "await=pd:Down:4"), *kb(0.35, "D", 0.25))  # launched -> down -> wakeup, player walks in
add(4.5, *enter("A2Dead", "px"), (0.22, "kbtap=J"), (0.52, "kbtap=J"), *kb(0.75, "A", 0.3), *kb(1.15, "D", 0.35))  # dead (shown 1.24 s): leave and re-enter range
# ---------------- TC-08-05: jumpers
add(0.1, (0, "tc=TC-08-05"))
for z in ("J5NearIn", "J5NearOut", "J5FarIn", "J5FarOut"):
    add(3.0, (0, f"mark={z}"), (0, f"spawn={z}"))  # mark first: a jumper may jump on the spawn tick
# ---------------- TC-08-09: bosses A (1 phase), B (2 phases), C (warden, 3 phases)
add(0.1, (0, "tc=TC-08-09"))
add(3.0, (0, "mark=B9Solo"), (0, "spawn=B9Solo"), (0.05, "ehp=ba:20"), (0.1, "await=ba:AttackStartup:4"), (0.15, "kbtap=J"))
add(3.0, (0, "mark=B9Duo"), (0, "spawn=B9Duo"), (0.05, "ehp=bb:55"), (0.1, "await=bb:AttackStartup:4"), (0.15, "kbtap=J"))
add(9.0, (0, "mark=B9Warden"), (0, "spawn=B9Warden"), (0.05, "ehp=bc:125"), (0.1, "await=bc:AttackStartup:4"), (0.15, "kbtap=J"))
# ---------------- TC-08-12: gauntlet, state x case. g2 = event "arm", g3 = "arm" then "lift" (0.5 s presentations)
add(0.1, (0, "tc=TC-08-12"))
CASE = {
    "Phys": [(0.05, "kbtap=J")],
    "Magic": fire(0.05),
    "Parry": kb(0.46, "L", 0.4),
    "Simul": [(0.54, "kbtap=J")],  # boss active = startup + 30 ticks; press 27 ticks after it -> our attack starts at +28, active at +30
    "Launch": [(0.05, "kbtap=K")],
}
for st in ("g1", "g2", "g3"):
    for c in ("Phys", "Magic", "Parry", "Simul", "Launch"):
        b = f"x{st}{c}"
        pre = [(0, f"spawn=G{st}{c}")]
        if st in ("g2", "g3"):
            pre += [(0.02, f"bossevent={b}:arm")]
        if st == "g3":
            pre += [(0.65, f"bossevent={b}:lift")]
        a = 1.3  # every state: the attack that started on the spawn tick passes, so the await catches a fresh startup
        cmds = pre + [(a, f"await={b}:AttackStartup:5"), (a, f"mark=G{st}{c}")] + [(a + o, x) for o, x in CASE[c]]
        add(a + 2.4, *cmds)
# ---------------- TC-UNITY-02: fast moves / knockback into walls (wall edge 4.7 m right of the player's right edge)
add(0.1, (0, "tc=TC-UNITY-02"))
add(3.0, (0, "mark=U2WallRun"), (0, "spawn=U2WallMove"), (0.1, "kbtap=D"), *kb(0.2, "D", 2.2))                      # run into the wall, keep pushing
add(3.0, (0, "mark=U2WallDash"), (0, "spawn=U2WallMove"), (0.1, "kb=D:down"), (1.0, "kbtap=Space"), (1.15, "kbtap=J"), (1.9, "kb=D:up"))   # air dash into it
add(3.0, (0, "mark=U2WallDive"), (0, "spawn=U2WallMove"), (0.1, "kb=D:down"), (1.0, "kbtap=Space"), (1.15, "kbtap=K"), (1.9, "kb=D:up"))   # dive into it
add(2.5, (0, "mark=U2WallKnock"), (0, "spawn=U2WallKnock"), (0.02, "await=uk:Idle:6"), (0.02, "await=uk:AttackActive:6"))  # knocked into a wall behind
add(2.5, (0, "mark=U2WallEnemy"), (0, "spawn=U2WallEnemy"), *fire(0.1))                                             # magic knocks the bag into a wall
# ---------------- TC-UNITY-07: attack signal lifecycle
add(0.1, (0, "tc=TC-UNITY-07"))
add(1.5, (0, "mark=U7Multi"), (0, "spawn=U7Multi"), (0.1, "kbtap=J"))                                               # one motion, four targets
add(2.5, (0, "mark=U7Cancel"), (0, "spawn=U7Cancel"), (0.02, "await=uc:Idle:6"), (0.02, "await=uc:AttackStartup:6"), *fire(0.12))  # cast starts ~10 ticks into its 15-tick startup: its hit lands mid-cast
add(5.0, (0, "mark=U7Wakeup"), (0, "spawn=U7Wakeup"), (0.1, "kbtap=K"), (0.12, "await=uw:Down:4"), (0.3, "kbtap=J"),
    (0.32, "await=uw:Wakeup:4"), (0.36, "kbtap=J"), (0.38, "await=uw:Idle:4"), (0.42, "kbtap=J"))                   # down, waking up, up again
out = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"
open(S + "wp11a_main.txt", "w", encoding="utf-8", newline="").write(out)
print(len(out), round(t, 1), "s")
