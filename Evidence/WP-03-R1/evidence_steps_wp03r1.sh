#!/usr/bin/env bash
# WP-03-R1 evidence run: F02 same-frame stick/button order, F01 stick release before registration,
# M01 ring during an air attack and ring use after an air hit. WP03_Ring scene, virtual gamepad, recorded video.
# Commands with the same script time run in the same rendered frame (same Update), in the written order.
set -u
NAME=${1:-main}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP03R1/$NAME"
mkdir -p "$OUT"
LB='padtap=LeftShoulder'
S=\
'0.1:tc=WP03-F02;0.1:spawn=TestBags;0.3:'$LB';0.35:pad=0,1;'\
'0.5:mark=F02-neutral-then-A;0.5:pad=0,0;0.5:padtap=A;'\
'0.6:mark=F02-newitem-then-A;0.6:pad=1,0;0.6:padtap=A;0.61:pad=0,0;'\
'1.9:'$LB';1.95:pad=0,1;2.1:mark=F02-A-then-neutral;2.1:padtap=A;2.1:pad=0,0;'\
'3.4:'$LB';3.45:pad=0,1;3.6:mark=F02-A-then-newitem;3.6:padtap=A;3.6:pad=1,0;3.61:pad=0,0;'\
'4.9:mark=F02-stick-open-A-one-frame;4.9:pad=0.7,0.7;4.9:'$LB';4.9:padtap=A;4.95:pad=0,0;'\
'6.2:'$LB';6.25:pad=0,1;6.4:mark=F02-inside-deadzone-then-A;6.4:pad=0.2,0.2;6.4:padtap=A;6.5:'$LB';6.55:pad=0,0;'\
'7.0:tc=WP03-F01;7.0:'$LB';7.05:pad=0,1;7.1:padtap=Y;7.15:mark=F01-dpad-before-release;7.15:padtap=DpadUp;7.2:pad=1,0;7.25:padtap=DpadUp;'\
'7.3:mark=F01-release;7.3:pad=0,0;7.35:pad=-1,0;7.4:mark=F01-dpad-after-release;7.4:padtap=DpadUp;7.45:pad=0,0;'\
'7.8:'$LB';7.85:pad=0.7,0.7;7.9:mark=F01-same-frame-Y-dpad-release;7.9:padtap=Y;7.9:padtap=DpadDown;7.9:pad=0,0;8.0:padtap=DpadDown;'\
'8.4:'$LB';8.45:pad=1,0;8.5:mark=F01-same-frame-Y-release-dpad;8.5:padtap=Y;8.5:pad=0,0;8.5:padtap=DpadRight;'\
'9.2:tc=TC-02-11;9.2:padtap=A;9.35:padtap=X;9.38:mark=M01-air-attack-ring;9.38:'$LB';9.4:pad=0,1;9.42:padtap=A;9.45:pad=0,0;'\
'10.6:tc=TC-INT-02;10.6:spawn=TestHitter;10.6:await=hitter1:AttackStartup:5;10.76:padtap=A;'\
'11.05:mark=M01-air-hit-ring;11.05:'$LB';11.07:pad=0,1;11.09:padtap=A;11.1:pad=0,0;'\
'13.0:mark=ScriptEnd;13.1:flush'
wait_editor
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP03_Ring.unity"); return "opened";' >/dev/null
unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
wait_playing; echo "playing"
run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
echo "$run"
ev "$INPUT_FOCUS_ON" >/dev/null
ev 'UnityEngine.Camera.main.aspect = 1.6f; return "aspect";' >/dev/null
ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP03R1/$NAME/$NAME-$run\", 1280, 800, 50);"
ev "Corum2.Unity.GameHost.Instance.RunInputScript(\"$S\"); return \"started\";" >/dev/null
wait_script_done; echo "script done"
ev 'return Corum2.EditorTools.EvidenceRecorder.Stop();'
unity command editor.playmode stop $C >/dev/null 2>&1
wait_edit
ev "$INPUT_FOCUS_OFF" >/dev/null
cp "$P/Diagnostics/Runs/$run".* "$OUT/" 2>/dev/null
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo "done $run"
