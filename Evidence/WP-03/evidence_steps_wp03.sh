#!/usr/bin/env bash
# WP-03 ring/quickslot evidence run: WP03_Ring scene, virtual gamepad (and keyboard for one pass), recorded video.
# Script times are game seconds; while the ring is open the world runs at x0.2, so they pass slower in real time.
set -u
NAME=${1:-main}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP03/$NAME"
mkdir -p "$OUT"
LB='padtap=LeftShoulder'
S=\
'0.1:tc=TC-02-03;0.1:spawn=TestBags;0.3:pad=1,0;0.8:'$LB';1.0:pad=1,0;1.1:pad=0.3,0.95;1.2:pad=0,0;1.25:padtap=A;'\
'1.3:tc=TC-02-05;1.3:pad=-0.7,0.7;1.35:padtap=RightTrigger;1.4:padtap=RightTrigger;1.45:padtap=LeftTrigger;1.5:padtap=RightTrigger;'\
'1.55:padtap=X;1.65:padtap=X;1.7:pad=1,0;1.75:'$LB';2.3:pad=0,0;'\
'2.6:tc=TC-02-07;2.6:'$LB';2.65:pad=0,1;2.7:padtap=DpadUp;2.75:padtap=Y;2.8:pad=0,0;2.85:padtap=DpadUp;'\
'3.0:'$LB';3.05:pad=0.7,0.7;3.1:padtap=Y;3.15:pad=0,0;3.2:padtap=DpadRight;'\
'3.4:'$LB';3.45:pad=1,0;3.5:padtap=Y;3.55:pad=0,0;3.6:padtap=DpadDown;'\
'3.8:'$LB';3.85:pad=-0.7,-0.7;3.9:padtap=Y;3.95:pad=0,0;4.0:padtap=DpadLeft;'\
'4.2:tc=TC-02-09;4.2:'$LB';4.25:pad=0,1;4.3:padtap=Y;4.35:padtap=B;4.4:pad=0,0;4.45:'$LB';'\
'4.8:tc=TC-02-06;4.8:padtap=DpadUp;6.0:padtap=DpadRight;7.2:padtap=DpadRight;7.3:padtap=DpadDown;8.0:padtap=DpadLeft;8.2:padtap=DpadLeft;'\
'9.4:tc=TC-02-08;9.4:'$LB';9.45:pad=0,1;9.5:padtap=B;9.55:padtap=B;9.6:pad=0.7,-0.7;9.65:padtap=B;9.7:pad=-0.7,-0.7;9.75:padtap=B;9.8:padtap=A;9.81:pad=0,0;'\
'10.2:tc=TC-02-10;10.2:'$LB';10.25:pad=1,0;10.3:padtap=A;10.31:pad=0,0;11.6:'$LB';11.65:pad=1,0;11.7:padtap=A;11.71:pad=0,0;'\
'12.6:tc=TC-02-11;12.6:padtap=A;12.75:'$LB';12.85:'$LB';13.6:padtap=X;13.62:'$LB';13.64:pad=0,-1;13.66:padtap=A;13.67:pad=0,0;'\
'14.6:padtap=A;14.75:pad=1,0;14.76:padtap=X;14.8:'$LB';14.9:'$LB';14.95:pad=0,0;'\
'16.0:padtap=A;16.15:pad=0.7,-0.7;16.16:padtap=Y;16.2:'$LB';16.3:'$LB';16.35:pad=0,0;'\
'17.5:tc=TC-05-05;17.5:spawn=TestHitter;17.5:await=hitter1:AttackStartup:5;17.9:padtap=X;17.92:'$LB';17.94:pad=0,1;17.96:padtap=A;17.97:pad=0,0;'\
'18.6:tc=TC-CF-08a;18.6:spawn=TestHitter;18.6:await=hitter1:AttackStartup:5;19.0:padtap=DpadUp;'\
'19.2:spawn=TestHitter;19.2:await=hitter1:AttackStartup:5;19.6:'$LB';19.62:pad=0,1;19.64:padtap=A;19.66:'$LB';19.68:pad=0.7,0.7;19.7:padtap=A;19.72:pad=0,0;19.74:'$LB';'\
'20.6:spawn=TestBags;'\
'21.0:tc=TC-02-01;21.0:kbtap=Q;21.05:kb=W:down;21.1:kbtap=C;21.15:kbtap=J;21.2:kbtap=J;21.25:kbtap=E;21.3:kbtap=E;21.35:kbtap=Space;21.36:kb=W:up;'\
'22.5:mark=ScriptEnd;22.6:flush'
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
ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP03/$NAME/$NAME-$run\", 1280, 800, 50);"
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
