"""WP-08 R2: WP08-F02-R1 double lethal hit (two hitters on one spot attack on the same tick; HP 1), guarded and unguarded.
Builds wp08r2_evidence.sh from wp08_evidence.sh (WP-08 + R1 tail). Same pattern as the R1 lethal segments: feather, far spawn,
HP 1, in-range spawn, await the attack startup, (guard), two attack presses after death, A answers the revive prompt."""
S = 'C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/'
cmds = []


def c(t, x):
    cmds.append('%.2f:%s' % (t, x))


t = 97.0
for zone, a, mark, guard in (('Z28DoubleLethalGuard', 'dg1', 'DoubleLethalGuard', True), ('Z29DoubleLethalHit', 'dn1', 'DoubleLethalHit', False)):
    c(t, 'item=feather:1'); c(t, 'tc=WP08-F02-R1'); c(t, 'spawn=%sFar' % zone); c(t + 0.05, 'mark=' + mark); c(t + 0.3, 'hp=1')
    c(t + 0.4, 'spawn=' + zone); c(t + 0.4, 'await=%s:AttackStartup:5' % a)
    if guard:
        c(t + 0.4, 'kb=L:down'); c(t + 1.0, 'kb=L:up')  # same order as the R1 lethal guard: RB 0.26 s before contact in a video run (plain guard)
    c(t + 1.0, 'kbtap=J'); c(t + 1.2, 'kbtap=J'); c(t + 3.0, 'padtap=A')
    t += 5.0
c(t, 'hp=1000'); c(t + 0.5, 'mark=ScriptEnd'); c(t + 0.6, 'flush')
s = open(S + 'wp08_evidence.sh', encoding='utf-8').read()
old = "97.00:mark=ScriptEnd;97.10:flush'"
assert s.count(old) == 1
s = s.replace(old, ';'.join(cmds) + "'").replace('Diagnostics/WP08', 'Diagnostics/WP08R2')
open(S + 'wp08r2_evidence.sh', 'w', encoding='utf-8', newline='\n').write(s)
print('ok', round(t, 2))
