"""WP-07 R2: TC-06-10 magic at the wakeup boundaries (GPT WP-06~08 R1 review: magic V+L at the exact boundaries).
Builds wp07r2_evidence.sh from wp07_evidence.sh (WP-07 + R1 tail). Reuses the W3 / W4 bags: launcher -> Down, await Down, then the
fireball command (down, right, attack) so that its 5-tick active window spans Down->Wakeup (start) or Wakeup->Idle (end).
Usage: python wp07r2_gen.py [jstart] [jend]   (seconds after Down at which the attack button of the command is pressed)
"""
import sys

S = 'C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/'
JS = float(sys.argv[1]) if len(sys.argv) > 1 else 0.76
JE = float(sys.argv[2]) if len(sys.argv) > 2 else 1.36
cmds = []


def c(t, x):
    cmds.append('%.2f:%s' % (t, x))


def magic(t, j):
    # fireball command: down, down-right, right + attack (same keys as the WP-07 magic segments), attack pressed at t + j
    c(t + j - 0.18, 'kb=S:down'); c(t + j - 0.12, 'kb=D:down'); c(t + j - 0.06, 'kb=S:up'); c(t + j, 'kbtap=J'); c(t + j + 0.08, 'kb=D:up')


c(64.0, 'tc=TC-06-10'); c(64.0, 'spawn=W3WakeStart'); c(64.1, 'mark=MagicWakeupStartBoundary'); c(64.2, 'kbtap=K'); c(64.3, 'await=w3:Down:5')
magic(64.3, JS)
c(68.0, 'spawn=W4WakeEnd'); c(68.1, 'mark=MagicWakeupEndBoundary'); c(68.2, 'kbtap=K'); c(68.3, 'await=w4:Down:5')
magic(68.3, JE)
c(72.0, 'mark=ScriptEnd'); c(72.1, 'flush')
s = open(S + 'wp07_evidence.sh', encoding='utf-8').read()
old = "63.50:mark=ScriptEnd;63.60:flush'"
assert old in s
s = s.replace(old, ';'.join(cmds) + "'").replace('Diagnostics/WP07', 'Diagnostics/WP07R2')
open(S + 'wp07r2_evidence.sh', 'w', encoding='utf-8', newline='\n').write(s)
print('ok', JS, JE)
