"""WP-06 R2: TC-05-01 air attacks (air normal / air dash / dive). Builds wp06r2_evidence.sh from wp06_evidence.sh (WP-06 + R1 tail).
For each air attack: a control (no extra input) and move (Left, held) / jump presses at start / middle / just before the end of the
motion, then a press after the end. Steering is told apart from the attack's own motion and gravity by comparing each case's x
trajectory with the control's, tick by tick from the attack start.
Usage: python wp06r2_gen.py [trial]   (trial = controls only, to measure the motion lengths)
"""
import sys

S = 'C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/'
TRIAL = len(sys.argv) > 1 and sys.argv[1] == 'trial'
# attack: (keys to start it after the jump, motion length in ticks measured in the trial)
ATTACKS = {
    'AirNormal': ([(0.25, 'kbtap=J')], None),
    'AirDash': ([(0.25, 'kb=D:down'), (0.27, 'kbtap=J'), (0.30, 'kb=D:up')], None),
    'Dive': ([(0.25, 'kb=D:down'), (0.27, 'kbtap=K'), (0.30, 'kb=D:up')], None),
}
LEN = {'AirNormal': 12, 'AirDash': 12, 'Dive': 9}  # motion ticks (attack.start -> attack.end), from the trial run
START = {'AirNormal': 0.25, 'AirDash': 0.27, 'Dive': 0.27}  # script time of the attack start (press + 1 tick)
cmds = []


def c(t, x):
    cmds.append('%.2f:%s' % (t, x))


t = 84.0
c(t, 'tc=TC-05-01')
for name, (keys, _) in ATTACKS.items():
    n = LEN[name]
    cases = [('Control', None)]
    if not TRIAL:
        offs = {'start': 1, 'mid': n // 2, 'end': n - 2}
        cases += [('Move' + k, v) for k, v in offs.items()] + [('Jump' + k, v) for k, v in offs.items()]
    for case, off in cases:
        c(t, 'spawn=Start')
        c(t + 0.05, 'mark=Air%s_%s' % (name, case))
        c(t + 0.1, 'kbtap=Space')
        for o, k in keys:
            c(t + 0.1 + o, k)
        a0 = t + 0.1 + START[name]
        if case.startswith('Move'):
            c(a0 + off * 0.02, 'kb=A:down')
            c(a0 + (n + 6) * 0.02, 'kb=A:up')  # still held for 6 ticks after the end
        elif case.startswith('Jump'):
            c(a0 + off * 0.02, 'kbtap=Space')
            c(a0 + (n + 4) * 0.02, 'kbtap=Space')  # a separate press after the end
        t += 2.0
c(t, 'mark=ScriptEnd'); c(t + 0.1, 'flush')
tail = ';'.join(cmds)
s = open(S + 'wp06_evidence.sh', encoding='utf-8').read()
old = "83.00:mark=ScriptEnd;83.10:flush'"
if old not in s:
    old = "83.0:mark=ScriptEnd;83.1:flush'"
assert old in s, 'tail marker'
s = s.replace(old, tail + "'")
s = s.replace('Diagnostics/WP06', 'Diagnostics/WP06R2')
open(S + ('wp06r2_trial.sh' if TRIAL else 'wp06r2_evidence.sh'), 'w', encoding='utf-8', newline='\n').write(s)
print(len(cmds), 'commands, end', round(t, 2))
