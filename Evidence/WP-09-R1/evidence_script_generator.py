"""Generates wp09r1_evidence.sh (WP-09-R1).
  main  (WP06_ComboState): unchanged WP-09 main script (TC-02-01 part 1, TC-03-01, TC-03-06..09), re-recorded on the R1 commit.
  plane (WP04_AI): TC-01-01, WP09-M01: per zone the WP-09 pattern plus all 8 stick directions, jumps while holding up / up-left / up-right,
        keyboard diagonals; AI chasers / fleers walk on the ground (MoveVelX), jumpers jump, the boss moves.
  dev   (WP03_Ring, 2 ring pages): TC-02-01 part 2, per virtual device: register quickslots up / right / down / left through the ring,
        use each quickslot, turn the page next / prev for real, B on a selected item -> discard confirm -> A.
  slide (WP01_MapMovement): TC-03-04 left: slide right through the low passage, turn left, crouch + A slides left under the ceiling
        and out; once with the keyboard, once with the pad.
"""
import re

S = "C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/"
seg, t = [], 0.0


def add(dur, *cmds):
    global t
    seg.append("".join(f"{t + o:.2f}:{c};" for o, c in cmds))
    t += dur


def kb(o, key, d):
    return [(o, f"kb={key}:down"), (o + d, f"kb={key}:up")]


def done():
    return "".join(seg) + f"{t:.2f}:mark=ScriptEnd;{t + 0.1:.2f}:flush"


old = open(S + "wp09_evidence.sh", encoding="utf-8").read()
MAIN = re.search(r"MAIN='([^']*)'", old).group(1)

# ---------------- plane
J = "kbtap=Space"
seg.clear(); t = 0.0
add(0.2, (0, "hp=100000"), (0, "tc=TC-01-01"))
for z in ("Z2Chaser", "Z3Fleer", "Z5Jumper", "Z6Ranged", "Z7Boss"):
    add(6.6, (0, "spawn=" + z), (0.05, "mark=Plane_" + z), (0.2, "kbtap=J"), (0.6, J), *kb(1.2, "A", 0.5), (1.9, J), *kb(2.2, "D", 0.5),
        (3.2, "pad=0,1"), (3.3, "padtap=A"), (3.5, "pad=0.7,0.7"), (3.8, "pad=1,0"), (4.1, "pad=0.7,-0.7"), (4.4, "pad=0,-1"),
        (4.7, "pad=-0.7,-0.7"), (5.0, "pad=-1,0"), (5.3, "pad=-0.7,0.7"), (5.4, "padtap=A"), (5.6, "pad=0,0"),
        (5.7, "kb=W:down"), (5.7, "kb=D:down"), (5.8, J), (6.0, "kb=D:up"), (6.0, "kb=A:down"), (6.3, "kb=A:up"), (6.3, "kb=W:up"))
PLANE = done()

# ---------------- dev (ring scene; script times are game time, the ring slows the game to 0.2)
seg.clear(); t = 0.0
add(0.3, (0, "spawn=TestBags"), (0, "tc=TC-02-01"))
DEV = {
    "Kb": dict(open="kbtap=Q", y="kbtap=K", b="kbtap=E", a="kbtap=Space", lt="kbtap=Z", rt="kbtap=C",
               q={"Up": "kbtap=Digit1", "Right": "kbtap=Digit2", "Down": "kbtap=Digit3", "Left": "kbtap=Digit4"},
               stick={"Up": (["W"],), "Right": (["D"],), "DownLeft": (["S", "A"],), "Left": (["A"],), "UpLeft": (["W", "A"],)}),
    "Pad": dict(open="padtap=LeftShoulder", y="padtap=Y", b="padtap=B", a="padtap=A", lt="padtap=LeftTrigger", rt="padtap=RightTrigger",
                q={"Up": "padtap=DpadUp", "Right": "padtap=DpadRight", "Down": "padtap=DpadDown", "Left": "padtap=DpadLeft"},
                stick={"Up": "0,1", "Right": "1,0", "DownLeft": "-0.7,-0.7", "Left": "-1,0", "UpLeft": "-0.7,0.7"}),
}


def hold(dev, d, o):
    if dev == "Kb":
        return [(o, f"kb={k}:down") for k in DEV[dev]["stick"][d][0]]
    return [(o, "pad=" + DEV[dev]["stick"][d])]


def free(dev, d, o):
    if dev == "Kb":
        return [(o, f"kb={k}:up") for k in DEV[dev]["stick"][d][0]]
    return [(o, "pad=0,0")]


START = (("potion", 3), ("hipotion", 1), ("herb", 2), ("scroll", 2), ("key", 1), ("bread", 2), ("water", 1), ("apple", 3), ("elixir", 1), ("meat", 1))
for dev in ("Kb", "Pad"):
    D = DEV[dev]
    if dev == "Pad":
        # test setup (logged TestSetItemCount): the keyboard pass used and discarded items; restore the start counts so the pad
        # pass again has 10 stacks = 2 ring pages and an item in every slot
        add(0.1, *[(0.0, f"item={i}:{n}") for i, n in START])
    # register: open ring, point at the item, Y fixes it, release the stick, d-pad registers (ring closes)
    for cursor, slot in (("Up", "Up"), ("Right", "Right"), ("DownLeft", "Down"), ("Left", "Left")):
        add(0.5, (0, f"mark=Dev{dev}_Register{slot}"), (0.02, D["open"]), *hold(dev, cursor, 0.08), (0.14, D["y"]), *free(dev, cursor, 0.2), (0.26, D["q"][slot]))
    # use each quickslot outside the ring (HP lowered so heals apply)
    add(0.1, (0, f"mark=Dev{dev}_UseQuickslots"), (0.02, "hp=10"))
    add(4.9, *[(1.2 * i, D["q"][s]) for i, s in enumerate(("Up", "Right", "Down", "Left"))])
    # page next / prev with two pages; the selection on page 2 is shown, then back to page 1, close
    add(0.6, (0, f"mark=Dev{dev}_Pages"), (0.02, D["open"]), (0.08, D["rt"]), *hold(dev, "Up", 0.14), *free(dev, "Up", 0.2), (0.26, D["lt"]), (0.34, D["open"]))
    # B on a selected item asks to discard; A confirms (ring closes)
    add(0.6, (0, f"mark=Dev{dev}_Discard"), (0.02, D["open"]), *hold(dev, "UpLeft", 0.08), (0.14, D["b"]), *free(dev, "UpLeft", 0.2), (0.26, D["a"]))
DEVS = done()

# ---------------- slide (low passage ceiling x 40..42, y >= 1.2 on M01; spawn TestPassageSlide x = 37.55 facing right)
seg.clear(); t = 0.0
add(4.2, (0, "tc=TC-03-04"), (0, "spawn=TestPassageSlide"), (0.05, "mark=SlideLeftKb"), *kb(0.1, "S", 1.4), (0.3, J), (1.0, J),
    *kb(1.7, "A", 0.04), (1.9, "kb=S:down"), (2.1, J), (2.8, J), (3.6, "kb=S:up"))
add(4.2, (0, "spawn=TestPassageSlide"), (0.05, "mark=SlideLeftPad"), (0.1, "pad=0,-1"), (0.3, "padtap=A"), (1.0, "padtap=A"), (1.5, "pad=0,0"),
    (1.7, "pad=-1,0"), (1.74, "pad=0,0"), (1.9, "pad=0,-1"), (2.1, "padtap=A"), (2.8, "padtap=A"), (3.6, "pad=0,0"))
SLIDE = done()

head, rest = old.split("MAIN='", 1)
rest = rest.split('case "$KIND" in', 1)[1]
head = head.replace("KIND = main | plane", "KIND = main | plane | dev | slide").replace("WP-09 evidence:", "WP-09-R1 evidence:")
rest = rest.replace('plane) S="$PLANE"; SCENE=Assets/Corum2/Scenes/WP04_AI.unity ;;',
                    'plane) S="$PLANE"; SCENE=Assets/Corum2/Scenes/WP04_AI.unity ;;\n  dev) S="$DEV"; SCENE=Assets/Corum2/Scenes/WP03_Ring.unity ;;\n'
                    '  slide) S="$SLIDE"; SCENE=Assets/Corum2/Scenes/WP01_MapMovement.unity ;;')
out = head + "MAIN='" + MAIN + "'\n\nPLANE='" + PLANE + "'\n\nDEV='" + DEVS + "'\n\nSLIDE='" + SLIDE + "'\n\ncase \"$KIND\" in" + rest
assert 'dev) S="$DEV"' in out
open(S + "wp09r1_evidence.sh", "w", encoding="utf-8").write(out)
print("ok", len(PLANE), len(DEVS), len(SLIDE))
