#!/usr/bin/env bash
# WP-05 magic command (R-02-02/10) and death flow (R-06-19) evidence run: WP04_AI scene (zones 30+ m apart), virtual gamepad, recorded video.
# Script times are game seconds. "hp=" sets the player HP (logged test setup) so the player survives each zone.
set -u
NAME=${1:-main}
KIND=${2:-main}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP05/$NAME"
mkdir -p "$OUT"
if [ "$KIND" = nores ]; then S='0.1:tc=TC-06-19a;0.1:item=feather:0;0.1:spawn=TestHitter;0.1:hp=1;4.0:mark=GameOverNoRevive;4.5:mark=SelectCheckpoint;4.5:padtap=A;5.0:pad=0,-1;5.1:pad=0,0;5.4:mark=SelectLoadSave;5.4:padtap=A;6.5:mark=ScriptEnd;6.6:flush'; else S='0.1:tc=TC-02-02;0.1:hp=1000;0.1:spawn=TestBags;0.5:mark=FireballCommand;0.5:pad=0,-1;0.56:pad=0.71,-0.71;0.62:pad=1,0;0.68:padtap=X;0.75:pad=0,0;2.0:mark=TwoDirectionsNoCommand;2.0:pad=0,-1;2.08:pad=0,0;2.16:pad=1,0;2.24:padtap=X;2.3:pad=0,0;3.5:spawn=TestBags;3.5:hp=60;3.6:mark=CureCommand;3.6:pad=0,-1;3.66:pad=-0.71,-0.71;3.72:pad=-1,0;3.78:padbtn=RightShoulder:down;3.84:padbtn=RightShoulder:up;3.9:pad=0,0;5.0:mark=GuardNoCommand;5.0:pad=-1,0;5.06:pad=0,0;5.3:padbtn=RightShoulder:down;5.7:padbtn=RightShoulder:up;6.5:mark=MightCommand;6.5:pad=0,-1;6.56:pad=-0.71,-0.71;6.62:pad=-1,0;6.68:padtap=A;6.75:pad=0,0;7.3:spawn=TestBags;7.5:mark=BuffedAttack;7.5:padtap=X;8.5:tc=TC-02-10;8.5:spawn=TestBags;8.7:mark=CommandFire;8.7:pad=0,-1;8.76:pad=0.71,-0.71;8.82:pad=1,0;8.88:padtap=X;8.95:pad=0,0;10.5:spawn=TestBags;10.7:mark=ScrollFire;10.7:padtap=DpadDown;11.8:mark=ReviveFromQuickslotRefused;11.8:padtap=DpadLeft;12.5:tc=TC-06-19a;12.5:spawn=TestHitter;12.5:item=feather:1;12.5:hp=1;16.5:mark=ReviveUse;16.5:padtap=A;18.5:item=feather:1;18.5:hp=1;22.5:mark=ReviveDecline;22.5:padtap=B;23.5:pad=0,-1;23.6:pad=0,0;23.8:pad=0,-1;23.9:pad=0,0;24.2:mark=SelectLobby;24.2:padtap=A;25.0:pad=0,1;25.1:pad=0,0;25.4:mark=SelectLoadSave;25.4:padtap=A;26.0:pad=0,1;26.1:pad=0,0;26.4:mark=SelectCheckpoint;26.4:padtap=A;28.0:mark=ScriptEnd;28.1:flush'; fi
wait_editor
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP05_MagicDeath.unity"); return "opened";' >/dev/null
unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
wait_playing; echo "playing"
run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
echo "$run"
ev "$INPUT_FOCUS_ON" >/dev/null
ev 'UnityEngine.Camera.main.aspect = 1.6f; return "aspect";' >/dev/null
ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP05/$NAME/$NAME-$run\", 1280, 800, 50);"
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
