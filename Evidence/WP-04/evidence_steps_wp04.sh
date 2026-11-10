#!/usr/bin/env bash
# WP-04 enemy AI / boss evidence run: WP04_AI scene (zones 30+ m apart), virtual gamepad, recorded video.
# Script times are game seconds. "hp=" sets the player HP (logged test setup) so the player survives each zone.
set -u
NAME=${1:-main}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP04/$NAME"
mkdir -p "$OUT"
S=\
'0.1:tc=TC-08-01;0.1:hp=1000;0.1:spawn=Z1Ten;0.3:pad=1,0;0.6:pad=0,0;3.0:mark=Z1Done;'\
'3.2:tc=TC-08-03;3.2:spawn=Z2Chaser;3.2:hp=1000;5.0:padtap=X;5.2:padtap=X;5.4:padtap=X;5.6:pad=-1,0;6.4:pad=0,0;8.0:mark=Z2Done;'\
'8.2:tc=TC-08-04;8.2:spawn=Z3Fleer;8.2:hp=1000;8.2:await=fl:AttackRecovery:5;8.55:padtap=X;8.7:padtap=X;12.0:mark=Z3Done;'\
'12.2:spawn=Z4Hold;12.2:hp=1000;12.2:await=fh:AttackRecovery:5;12.55:padtap=X;12.7:padtap=X;16.0:mark=Z4Done;'\
'16.2:tc=TC-08-05;16.2:spawn=Z5Jumper;16.2:hp=1000;19.0:mark=Z5Done;'\
'19.2:tc=TC-08-06;19.2:spawn=Z6Ranged;19.2:hp=1000;20.5:mark=RangedBeyondTrack;20.6:spawn=Z6Mid;22.0:mark=RangedOutOfRangeWait;22.1:spawn=Z6In;25.0:mark=RangedShotRetreat;25.1:spawn=Z6Close;25.1:padbtn=RightShoulder:down;28.5:padbtn=RightShoulder:up;29.0:mark=Z6Done;'\
'30.2:tc=TC-08-12;30.2:spawn=Z7Near;30.2:hp=1000;30.2:await=boss:AttackStartup:5;30.4:padbtn=RightShoulder:down;30.8:padbtn=RightShoulder:up;'\
'31.5:tc=TC-08-08;31.5:spawn=Z7Boss;34.0:spawn=Z7Near;34.2:padtap=A;35.0:padtap=A;35.8:padtap=A;36.6:padtap=A;37.4:padtap=A;'\
'38.5:tc=TC-08-10;38.5:spawn=Z7Boss;42.00:padtap=X;42.15:padtap=X;42.30:padtap=X;42.45:padtap=X;42.80:padtap=X;42.95:padtap=X;43.10:padtap=X;43.25:padtap=X;43.60:padtap=X;43.75:padtap=X;43.90:padtap=X;44.05:padtap=X;44.40:padtap=X;44.55:padtap=X;44.70:padtap=X;44.85:padtap=X;45.20:padtap=X;45.35:padtap=X;45.50:padtap=X;45.65:padtap=X;46.00:padtap=X;46.15:padtap=X;46.30:padtap=X;46.45:padtap=X;46.80:padtap=X;46.95:padtap=X;47.10:padtap=X;47.25:padtap=X;47.60:padtap=X;47.75:padtap=X;47.90:padtap=X;48.05:padtap=X;48.40:padtap=X;48.55:padtap=X;48.70:padtap=X;48.85:padtap=X;49.20:padtap=X;49.35:padtap=X;49.50:padtap=X;49.65:padtap=X;'\
'56.0:mark=BeforeEvent;56.0:bossevent=boss:calm;59.0:mark=Z7Done;'\
'59.2:tc=TC-08-07;59.2:spawn=Z2Chaser;59.2:hp=1;63.0:mark=ScriptEnd;63.1:flush'
wait_editor
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP04_AI.unity"); return "opened";' >/dev/null
unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
wait_playing; echo "playing"
run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
echo "$run"
ev "$INPUT_FOCUS_ON" >/dev/null
ev 'UnityEngine.Camera.main.aspect = 1.6f; return "aspect";' >/dev/null
ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP04/$NAME/$NAME-$run\", 1280, 800, 50);"
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
