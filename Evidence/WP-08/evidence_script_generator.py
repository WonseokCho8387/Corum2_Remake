"""Generates wp08_evidence.sh from the WP-06 runner (same runner, WP08 scene, new scripts).

Synchronised segments: the player waits out of range ("<zone>Far"), then spawns in range and awaits the enemy's
AttackStartup in the same frame, so the attack starts on the next tick and offsets below are from that start.
"""
S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"

seg = []
t = 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


def sync(zone, enemy, mark, tc=None, lead=0.4):
    """Commands for the segment start; returns them plus the anchor offset (attack start)."""
    c = []
    if tc: c.append((0, "tc=" + tc))
    c += [(0, f"spawn={zone}Far"), (0.05, "mark=" + mark), (lead, f"spawn={zone}"), (lead, f"await={enemy}:AttackStartup:5")]
    return c, lead


def at(a, cmds):
    return [(a + o, c) for o, c in cmds]


# Recorded runs (Time.captureFramerate 50) deliver a script input one tick after its time; presses meant for an exact tick are
# scheduled LAG earlier.
LAG = 0.02


def fire(o):
    return [(o, "kb=S:down"), (o + 0.03, "kb=D:down"), (o + 0.06, "kb=S:up"), (o + 0.09, "kbtap=J"), (o + 0.2, "kb=D:up")]


def hold(o, d, key="L"):
    return [(o, f"kb={key}:down"), (o + d, f"kb={key}:up")]


def roll(o):  # DownRight + jump = roll toward +X
    return [(o - 0.03, "kb=S:down"), (o - 0.025, "kb=D:down"), (o, "kbtap=Space"), (o + 0.15, "kb=D:up"), (o + 0.16, "kb=S:up")]


add(0.2, (0, "hp=1000"))
# TC-06-11 physical hit during a super-armor attack (armor startup 0.3 s; the hit lands in its startup)
c, a = sync("Z1Armor", "a1", "ArmorPhysical", "TC-06-11")
add(2.5, *c, *at(a, [(0.05, "kbtap=J")]))
# TC-06-12 magic during a super-armor attack (startup 0.6 s), then the next armor attack is armored again
c, a = sync("Z2ArmorMagic", "a2", "ArmorMagic", "TC-06-12")
add(2.5, *c, *at(a, fire(0.0)))
c, a = sync("Z2ArmorMagic", "a2", "ArmorAgain")
add(2.5, *c, *at(a, [(0.05, "kbtap=J")]))
# TC-06-13 roll (invulnerable 0.05..0.30 s of the roll); the slow hitter's hit lands at attack start + 0.60 s
for mark, sp, start in (("RollBeforeWindow", "Z3Ra", 0.57), ("RollInWindow", "Z3Rb", 0.45), ("RollAfterWindow", "Z3Rc", 0.27)):
    c, a = sync("Z3Roll", "r1", mark, "TC-06-13")
    add(2.4, *c, *at(a, [(0.0, "spawn=" + sp)] + roll(start)))
add(3.0, (0, "spawn=Z3Roll"), (0.05, "mark=NormalHitThenNextAttack"))
# TC-06-14 several enemies hit in a row; observe the lock, then walk out
add(5.0, (0, "tc=TC-06-14"), (0, "spawn=Z4Multi"), (0.05, "mark=MultiEnemyLock"), (3.5, "kb=A:down"), (4.6, "kb=A:up"))
# TC-06-15 / 16 same-tick hits (enemy active at +0.30; ground1 startup 0.06; magic cast 0.2)
c, a = sync("Z5Dodge", "dd1", "DodgeNormal", "TC-06-15")
add(2.5, *c, *at(a, [(0.24, "kbtap=J")]))
c, a = sync("Z6DodgeMagic", "dm1", "DodgeMagic", "TC-06-16")
add(2.5, *c, *at(a, fire(0.02)))
c, a = sync("Z7DodgeArmor", "da1", "DodgeArmor", "TC-06-16")
add(2.5, *c, *at(a, [(0.24, "kbtap=J")]))
# TC-06-17 kill, keep attacking during death and vanish
add(3.0, (0, "tc=TC-06-17"), (0, "spawn=Z8Death"), (0.05, "mark=AttackTheDead"), *[(0.2 + 0.3 * i, "kbtap=J") for i in range(7)])
# TC-06-18 A dies, B and C keep the combo
add(3.0, (0, "tc=TC-06-18"), (0, "spawn=Z9Link"), (0.05, "mark=OneDiesOthersContinue"), *[(0.2 + 0.3 * i, "kbtap=J") for i in range(4)])
# TC-07-01 facing right with RB: front attacker (hit +0.3) guarded, rear slow attacker (hit +0.8) hits; then turn left and guard
c, a = sync("Z10Front", "fr", "FrontGuardFacingRight", "TC-07-01")
add(2.0, *c, *at(a, hold(0.0, 1.0)))  # spawn facing = +X (right)
c, a = sync("Z10Back", "bl", "RearAttackWhileGuardingRight")
add(1.6, *c, *at(a, hold(0.0, 0.6)))
c, a = sync("Z10Back", "bl", "TurnLeftAndGuard")
add(1.6, *c, *at(a, [(0.01, "kb=A:down"), (0.07, "kb=A:up")] + hold(0.12, 0.5)))  # turn to the attacker, then RB (0.18 s before the hit: guard, not parry)
# TC-07-02 / TC-OPS-02 D = 9 / 10 / 50 front guard
for z, e, n in (("Z11D9", "g9", "Guard9"), ("Z12D10", "g10", "Guard10"), ("Z13D50", "g50", "Guard50")):
    c, a = sync(z, e, n, "TC-07-02")
    add(1.6, *c, *at(a, hold(0.0, 0.6)))
# TC-07-03 consecutive ground guard, air guard, air parry (slow hitter hit at +0.80)
c, a = sync("Z14Cont", "cg", "ConsecutiveGuard", "TC-07-03")
add(3.2, *c, *at(a, hold(0.0, 2.5)))
c, a = sync("Z15AirGuard", "ag", "AirGuard")
add(2.2, *c, *at(a, [(0.1, "kbtap=Space")] + hold(0.4, 0.7)))
c, a = sync("Z16AirParry", "ap", "AirParry")
add(2.2, *c, *at(a, [(0.1, "kbtap=Space")] + hold(0.72, 0.3)))
# TC-07-05 parry (RB 0.08 s before the hit) then attack at once
c, a = sync("Z17Parry", "pp", "ParryThenAttack", "TC-07-05")
add(2.2, *c, *at(a, hold(0.22, 0.18) + [(0.33, "kbtap=J")]))
# TC-07-06 normal / unblockable / guard break against a front guard
for z, e, n in (("Z20Normal", "tn", "GuardNormal"), ("Z18Unbl", "tu", "GuardUnblockable"), ("Z19Break", "tb", "GuardBreak")):
    c, a = sync(z, e, n, "TC-07-06")
    add(1.8, *c, *at(a, hold(0.0, 0.8)))
# TC-07-07 RB: ground normal, launcher, air normal (allowed); dash, dive (refused); during hitstun (refused), after it (allowed)
add(1.0, (0, "tc=TC-07-07"), (0, "spawn=Z21Cancel"), (0.05, "mark=RbGroundNormal"), (0.1, "kbtap=J"), (0.17, "kbtap=L"))
add(1.0, (0, "spawn=Z21Cancel"), (0.05, "mark=RbLauncher"), (0.1, "kbtap=K"), (0.17, "kbtap=L"))
add(1.5, (0, "spawn=Z21Cancel"), (0.05, "mark=RbAirNormal"), (0.1, "kbtap=Space"), (0.3, "kbtap=J"), (0.37, "kbtap=L"))
add(1.5, (0, "cunlock=1,0"), (0, "spawn=Z21Cancel"), (0.05, "mark=RbDash"), (0.1, "kbtap=Space"), (0.25, "kb=D:down"), (0.27, "kbtap=J"), (0.33, "kbtap=L"), (0.5, "kb=D:up"), (1.4, "cunlock=0,0"))  # pierce: the dash runs its full time
add(1.5, (0, "spawn=Z21Cancel"), (0.05, "mark=RbDive"), (0.1, "kbtap=Space"), (0.3, "kb=D:down"), (0.32, "kbtap=K"), (0.35, "kbtap=L"), (0.5, "kb=D:up"))
c, a = sync("Z22Stun", "hs", "RbDuringHitstun")
add(2.0, *c, *at(a, [(0.4, "kbtap=L")] + hold(0.8, 0.1)))
# TC-07-04 parry window boundary: RB 0.22 / 0.18 / 0.12 / 0.08 s before the hit (hit = attack start + 0.30)
pw_start = t
for i, x in enumerate((0.22, 0.18, 0.12, 0.08)):
    c, a = sync(f"Z23Window{i}", f"pw{i}", f"ParryOffset{int(round(x * 100)):02d}", "TC-07-04")
    add(1.6, *c, *at(a, hold(0.30 - x - LAG, 0.3)))
pw = seg[-4:]
main = "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"
# the parry-window-only script (same four segments, re-based to 0.2)
seg.clear()
t = 0.2
for i, x in enumerate((0.22, 0.18, 0.12, 0.08)):
    c, a = sync(f"Z23Window{i}", f"pw{i}", f"ParryOffset{int(round(x * 100)):02d}", "TC-07-04")
    add(1.6, *c, *at(a, hold(0.30 - x - LAG, 0.3)))
pwonly = "0.10:hp=1000;" + "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"

s = open(S + "wp06_evidence.sh", encoding="utf-8").read()
head, rest = s.split("MAIN='", 1)
rest = rest.split('case "$KIND" in', 1)[1]
head = head.replace("WP-06 evidence: P0-04 / P0-05 residual TCs on the WP06_ComboState scene", "WP-08 evidence: P0-06 TC-06-11..18 and P0-07 guard / parry TCs on the WP08_Defense scene")
head = head.replace("KIND = main | cw (TC-04-09 combo-window run; the window is set by the caller)", "KIND = main | pw (TC-07-04 parry-window run; the window is set by the caller)")
head = head.replace("Diagnostics/WP06", "Diagnostics/WP08")
rest = rest.replace('cw) S="$CW" ;;', 'pw) S="$PW" ;;').replace("WP06_ComboState", "WP08_Defense").replace("Diagnostics/WP06", "Diagnostics/WP08")
out = head + "MAIN='" + main + "'\n\nPW='" + pwonly + "'\n\ncase \"$KIND\" in" + rest
open(S + "wp08_evidence.sh", "w", encoding="utf-8").write(out)
print(len(main), round(t, 2))
