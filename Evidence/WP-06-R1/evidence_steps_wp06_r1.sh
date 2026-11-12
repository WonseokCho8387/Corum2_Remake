#!/usr/bin/env bash
# WP-06 evidence: P0-04 / P0-05 residual TCs on the WP06_ComboState scene (zones 30 m apart), virtual keyboard, recorded video.
# Keys: J attack, K special (launcher; in the air with a direction = dive), L guard (RB), Space jump, A/D/S/W directions.
# In the air: direction + J = air dash, neutral J = air normal attack.
# Usage: wp06_evidence.sh NAME KIND [novideo]   KIND = main | cw (TC-04-09 combo-window run; the window is set by the caller)
set -u
NAME=${1:-main}
KIND=${2:-main}
VIDEO=${3:-video}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP06/$NAME"
mkdir -p "$OUT"

MAIN='0.1:tc=TC-04-04;0.1:cunlock=0,1;0.1:spawn=Z1Ten;0.1:mark=TenTargetsStandardChain;'\
'0.4:kbtap=J;0.7:kbtap=J;1.0:kbtap=J;1.3:kbtap=J;1.6:kbtap=K;2.3:kbtap=Space;2.5:kbtap=J;2.65:kbtap=J;2.95:kbtap=J;3.25:kbtap=J;3.55:kb=D:down;3.6:kbtap=K;3.8:kb=D:up;'\
'5.0:tc=TC-04-02;5.0:cunlock=0,0;5.0:spawn=Z2Launch;5.1:mark=LaunchThenStayOnGround;5.2:kbtap=K;6.0:kb=A:down;6.15:kb=A:up;6.3:kbtap=J;6.7:kbtap=J;'\
'8.0:tc=TC-04-06;8.0:cunlock=0,0;8.0:spawn=Z3Pierce;8.1:mark=InitialDash;8.2:kbtap=Space;8.35:kb=D:down;8.37:kbtap=J;8.6:kb=D:up;'\
'10.0:cunlock=1,0;10.0:spawn=Z3Pierce;10.1:mark=PierceDash;10.2:kbtap=Space;10.35:kb=D:down;10.37:kbtap=J;10.6:kb=D:up;'\
'12.0:tc=TC-04-08;12.0:cunlock=0,0;12.0:spawn=Z4Dive;12.1:mark=InitialDive;12.2:kbtap=Space;12.4:kb=D:down;12.42:kbtap=K;12.7:kb=D:up;'\
'14.0:cunlock=0,1;14.0:spawn=Z4Dive;14.1:mark=AreaDive;14.2:kbtap=Space;14.4:kb=D:down;14.42:kbtap=K;14.7:kb=D:up;'\
'16.0:tc=TC-04-03;16.0:cunlock=0,0;16.0:spawn=Z5Guard;16.1:await=g1:Idle;16.2:mark=EnemyGuardCancel;16.2:kbtap=J;17.2:kbtap=J;'\
'18.5:tc=TC-04-03;18.5:spawn=Z7Lock;18.6:mark=MissEndsChain;18.6:kb=A:down;18.72:kb=A:up;18.8:kbtap=J;19.5:kb=D:down;19.6:kb=D:up;19.8:mark=TimeoutEndsChain;19.8:kbtap=J;21.0:mark=RbCancelEndsChain;21.0:kbtap=J;21.07:kbtap=L;21.6:kbtap=J;'\
'23.0:tc=TC-05-01;23.0:spawn=Z7Lock;23.1:mark=MoveDuringAttack;23.2:kbtap=J;23.21:kb=D:down;23.5:kb=D:up;24.0:kbtap=J;24.12:kb=D:down;24.4:kb=D:up;'\
'25.0:kbtap=J;25.23:kb=D:down;25.5:kb=D:up;26.0:mark=JumpDuringAttack;26.0:kbtap=J;26.1:kbtap=Space;26.6:kbtap=Space;'\
'28.0:tc=TC-05-06;28.0:spawn=Z7Lock;28.1:mark=BufferedPress;28.2:kbtap=J;28.3:kbtap=J;29.5:mark=PressAfterWindow;29.5:kbtap=J;30.6:kbtap=J;'\
'32.0:tc=TC-04-07;32.0:spawn=Z6Dash;32.1:mark=LaunchAirNormalDashThenAttack;32.0:cunlock=1,0;32.2:kbtap=K;32.6:kbtap=Space;32.75:kbtap=J;33.05:kb=D:down;33.07:kbtap=J;33.12:kb=D:up;33.15:kbtap=J;33.45:kbtap=J;34.5:cunlock=0,0;'\
'35.0:tc=TC-05-03;35.0:cunlock=1,0;35.0:spawn=Z6Dash;35.1:mark=RbAtDashStart;35.2:kbtap=Space;35.35:kb=D:down;35.37:kbtap=J;35.38:kbtap=L;35.6:kb=D:up;'\
'37.0:spawn=Z6Dash;37.1:mark=RbAtDashActive;37.2:kbtap=Space;37.35:kb=D:down;37.37:kbtap=J;37.47:kbtap=L;37.6:kb=D:up;'\
'39.0:spawn=Z6Dash;39.1:mark=RbAtDashEnd;39.2:kbtap=Space;39.35:kb=D:down;39.37:kbtap=J;39.59:kbtap=L;39.7:kb=D:up;'\
'41.0:spawn=Z6Dash;41.1:mark=RbAtDiveStart;41.2:kbtap=Space;41.4:kb=D:down;41.42:kbtap=K;41.43:kbtap=L;41.7:kb=D:up;'\
'43.0:spawn=Z6Dash;43.1:mark=RbAtDiveMid;43.2:kbtap=Space;43.4:kb=D:down;43.42:kbtap=K;43.5:kbtap=L;43.7:kb=D:up;'\
'44.90:cunlock=0,0;46.00:tc=TC-05-01;46.00:spawn=Z7Lock;46.20:mark=MoveGround+2;46.20:kbtap=J;46.22:kb=D:down;46.80:kb=D:up;47.20:mark=MoveGround+12;47.20:kbtap=J;47.32:kb=D:down;47.80:kb=D:up;48.20:mark=MoveGround+22;48.20:kbtap=J;48.42:kb=D:down;48.80:kb=D:up;49.20:mark=MoveRepressGround;49.20:kbtap=J;49.32:kb=D:down;49.38:kb=D:up;49.70:kb=D:down;49.90:kb=D:up;50.70:mark=JumpGround+2;50.70:kbtap=J;50.72:kbtap=Space;51.40:kbtap=Space;52.70:mark=JumpGround+12;52.70:kbtap=J;52.82:kbtap=Space;53.40:kbtap=Space;54.70:mark=JumpGround+22;54.70:kbtap=J;54.92:kbtap=Space;55.40:kbtap=Space;60.00:spawn=Z7Lock;60.20:mark=MoveLauncher+2;60.20:kbtap=K;60.22:kb=D:down;60.80:kb=D:up;61.20:mark=MoveLauncher+12;61.20:kbtap=K;61.32:kb=D:down;61.80:kb=D:up;62.20:mark=MoveLauncher+22;62.20:kbtap=K;62.42:kb=D:down;62.80:kb=D:up;63.20:mark=MoveRepressLauncher;63.20:kbtap=K;63.32:kb=D:down;63.38:kb=D:up;63.70:kb=D:down;63.90:kb=D:up;64.70:mark=JumpLauncher+2;64.70:kbtap=K;64.72:kbtap=Space;65.40:kbtap=Space;66.70:mark=JumpLauncher+12;66.70:kbtap=K;66.82:kbtap=Space;67.40:kbtap=Space;68.70:mark=JumpLauncher+22;68.70:kbtap=K;68.92:kbtap=Space;69.40:kbtap=Space;75.00:tc=TC-04-06;75.00:cunlock=1,0;75.00:spawn=Z8GuardPierce;75.05:await=gp:Idle:6;75.10:mark=GuardThenPierce;75.20:kbtap=Space;75.35:kb=D:down;75.37:kbtap=J;75.80:kb=D:up;78.00:tc=TC-04-06;78.00:cunlock=1,0;78.00:spawn=Z9NoGuardPierce;78.05:await=np:Idle:6;78.10:mark=NoGuardPierce;78.20:kbtap=Space;78.35:kb=D:down;78.37:kbtap=J;78.80:kb=D:up;81.00:cunlock=0,0;81.00:tc=TC-04-04;81.00:spawn=Z10Five;81.10:mark=FiveTargetsOneAttack;81.30:kbtap=J;83.00:mark=ScriptEnd;83.10:flush'

CW='0.1:tc=TC-04-09;0.1:spawn=Z7Lock;0.2:mark=ComboWindowBoundary;0.3:kbtap=J;0.9:kbtap=J;2.0:kbtap=J;2.9:kbtap=J;4.0:mark=ScriptEnd;4.1:flush'

case "$KIND" in
  cw) S="$CW" ;;
  *) S="$MAIN" ;;
esac
wait_editor
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP06_ComboState.unity"); return "opened";' >/dev/null
unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
wait_playing; echo "playing"
run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
echo "$run"
ev "$INPUT_FOCUS_ON" >/dev/null
ev 'UnityEngine.Camera.main.aspect = 1.6f; return "aspect";' >/dev/null
[ "$VIDEO" = video ] && ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP06/$NAME/$NAME-$run\", 1280, 800, 50);"
ev "Corum2.Unity.GameHost.Instance.RunInputScript(\"$S\"); return \"started\";" >/dev/null
wait_script_done; echo "script done"
[ "$VIDEO" = video ] && ev 'return Corum2.EditorTools.EvidenceRecorder.Stop();'
unity command editor.playmode stop $C >/dev/null 2>&1
wait_edit
ev "$INPUT_FOCUS_OFF" >/dev/null
cp "$P/Diagnostics/Runs/$run".* "$OUT/" 2>/dev/null
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo "done $run"
