#!/usr/bin/env bash
# WP-01-R1 evidence: device-level (virtual keyboard/gamepad) input, video recording, unlock-off run,
# multi-tick-per-frame run, Inspector round-trip run. Usage: r1_evidence.sh <outdir-name>
set -u
export UNITY_PROJECT_PATH="D:\\ProjectR\\Corum2Remake"
C="--caller plugin --skill unity-cli --no-banner"
P=/d/ProjectR/Corum2Remake
OUTNAME=$1
OUT="$P/Diagnostics/R1/$1"
mkdir -p "$OUT"
DATA="Assets/Corum2/Data/WP01"

source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh

play_run() { # name unlockAsset record(0/1) script
  local name=$1 unlock=$2 rec=$3 script=$4
  wait_editor
  ev "var h = UnityEngine.Object.FindFirstObjectByType<Corum2.Unity.GameHost>(); h.unlocks = UnityEditor.AssetDatabase.LoadAssetAtPath<Corum2.Unity.AirUnlockFixture>(\"$DATA/$unlock.asset\"); return h.unlocks.name;" >/dev/null
  unity command editor set_auto_tick $C >/dev/null 2>&1
  unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
  unity command editor.playmode start $C >/dev/null 2>&1
  wait_playing
  ev "$INPUT_FOCUS_ON" >/dev/null
  local run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
  echo "[$name] $run playing"
  if [ "$rec" = 1 ]; then
    ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/R1/$OUTNAME/$name-$run\", 1280, 800, 50);"
  fi
  ev "Corum2.Unity.GameHost.Instance.RunInputScript(\"$script\"); return \"started\";" >/dev/null
  wait_script_done
  if [ "$rec" = 1 ]; then ev 'return Corum2.EditorTools.EvidenceRecorder.Stop();'; fi
  ev 'var h = Corum2.Unity.GameHost.Instance; return h.RunId + " tick=" + h.Sim.Tick + " map=" + h.Sim.CurrentMap.Id + " transitions=" + h.Sim.TransitionCount;'
  unity command editor.playmode stop $C >/dev/null 2>&1
  wait_edit
  ev "$INPUT_FOCUS_OFF" >/dev/null
  cp "$P/Diagnostics/Runs/$run".* "$OUT/" 2>/dev/null
  echo "$name $run" >> "$OUT/runs.txt"
}

MAIN='0.5:tc=TC-03-01;0.5:kb=D:down;1.5:kb=D:up;1.8:kb=D:down;1.85:kb=D:up;1.9:kb=D:down;2.7:kb=D:up;'\
'3.0:tc=TC-03-02;3.0:kbtap=Space;4.2:kb=D:down;4.4:kbtap=Space;4.45:kb=D:up;5.6:kb=A:down;5.65:kb=A:up;5.7:kb=A:down;5.9:kbtap=Space;5.95:kb=A:up;'\
'7.2:tc=TC-03-05;7.2:pad=0.7,-0.7;7.3:padtap=South;7.9:pad=0,0;8.1:pad=-0.7,-0.7;8.2:padtap=South;8.8:pad=0,0;9.0:pad=1,0;9.2:pad=0,0;9.4:pad=0.7,-0.7;9.5:padtap=South;10.1:pad=-0.7,-0.7;10.2:padtap=South;10.8:pad=0,0;'\
'11.2:tc=TC-03-06;11.2:padtap=South;11.3:padtap=RightShoulder;11.4:pad=1,0;11.45:pad=0,0;11.5:pad=1,0;11.6:padtap=South;11.75:padtap=West;12.15:pad=0.7,-0.7;12.2:padtap=North;12.7:pad=0,0;'\
'13.2:tc=TC-03-08;13.2:padtap=South;13.5:pad=1,0;13.55:padtap=North;13.6:padtap=South;13.62:padtap=West;14.0:pad=0,0;'\
'14.3:tc=TC-03-09;14.3:spawn=TestOpen;14.4:padtap=South;14.6:padtap=South;15.6:pad=1,0;15.7:padtap=South;15.9:padtap=South;16.6:pad=0,0;'\
'17.0:tc=TC-03-04;17.0:spawn=TestPassageSlide;17.3:kb=S:down;17.5:kbtap=Space;18.1:kb=S:up;18.4:kb=S:down;18.45:kb=D:down;18.6:kbtap=Space;18.8:kb=D:up;18.85:kb=A:down;19.0:kbtap=Space;19.2:kb=A:up;19.4:kbtap=Space;20.0:kb=S:up;'\
'20.4:tc=TC-01-02;20.4:kb=D:down;24.0:kb=D:up;'\
'24.3:tc=TC-01-04;24.3:kb=W:down;24.5:kb=W:up;25.0:kb=S:down;25.2:kb=S:up;25.5:kb=A:down;26.1:kb=A:up;26.3:kb=W:down;26.5:kb=W:up;'\
'26.8:tc=TC-01-03;26.8:kb=D:down;26.85:kb=D:up;26.9:kb=D:down;31.2:kb=D:up;31.6:kb=A:down;33.2:kb=A:up;'\
'33.6:mark=ScriptEnd;33.7:flush'

LOCKED='0.5:tc=TC-03-03;0.5:kbtap=Space;0.75:kbtap=Space;0.8:kb=D:down;0.85:kbtap=J;0.9:kbtap=K;1.0:kb=D:up;1.4:padtap=South;1.6:padtap=South;1.65:pad=1,0;1.7:padtap=West;1.75:padtap=North;1.9:pad=0,0;3.0:mark=ScriptEnd;3.1:flush'

MULTI='0.5:tc=TC-UNITY-06;0.5:framedt=0.1;1.0:kbtap=Space;2.0:kbtap=D;2.5:kb=D:down;2.5:kb=D:up;2.5:kb=D:down;3.5:kb=D:up;4.0:padtap=South;5.0:framedt=restore;5.5:mark=ScriptEnd;5.6:flush'

INSPECT='0.5:tc=TC-UNITY-03;0.5:kb=D:down;1.5:kb=D:up;2.0:mark=ScriptEnd;2.1:flush'

play_run main AirUnlocks_All 1 "$MAIN"
play_run locked AirUnlocks_None 1 "$LOCKED"
play_run multitick AirUnlocks_All 0 "$MULTI"

# Inspector path: edit walkSpeed through SerializedObject (what the Inspector does), save, run, then restore.
ev "var a = UnityEditor.AssetDatabase.LoadAssetAtPath<Corum2.Unity.MovementConfig>(\"$DATA/MovementConfig.asset\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"walkSpeed\").floatValue = 4.2f; so.ApplyModifiedProperties(); UnityEditor.AssetDatabase.SaveAssets(); return a.walkSpeed;" | grep -o '"result":[^,]*'
play_run inspector42 AirUnlocks_All 0 "$INSPECT"
ev "var a = UnityEditor.AssetDatabase.LoadAssetAtPath<Corum2.Unity.MovementConfig>(\"$DATA/MovementConfig.asset\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"walkSpeed\").floatValue = 3.5f; so.ApplyModifiedProperties(); UnityEditor.AssetDatabase.SaveAssets(); return a.walkSpeed;" | grep -o '"result":[^,]*'
play_run inspector35 AirUnlocks_All 0 "$INSPECT"

# Put the scene back exactly as saved on disk and restore editor settings.
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP01_MapMovement.unity"); return "reopened";' >/dev/null
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo "done"; cat "$OUT/runs.txt"; ls -la "$OUT" | tail -25
