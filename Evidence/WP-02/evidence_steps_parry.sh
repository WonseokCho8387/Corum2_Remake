#!/usr/bin/env bash
# WP-02 evidence: WP02_Combat scene, virtual keyboard input, recorded video. Usage: wp02_evidence.sh <outdir-name>
set -u
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUTNAME=$1
OUT="$P/Diagnostics/WP02/$OUTNAME"
mkdir -p "$OUT"

run_scene() { # name script
  local name=$1 script=$2
  wait_editor
  ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP02_Combat.unity"); return "opened";' >/dev/null
  unity command editor set_auto_tick $C >/dev/null 2>&1
  unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
  unity command editor.playmode start $C >/dev/null 2>&1
  wait_playing; echo "[$name] playing"
  ev "$INPUT_FOCUS_ON" >/dev/null
  local run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
  echo "[$name] $run"
  ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP02/$OUTNAME/$name-$run\", 1280, 800, 50);"
  ev "Corum2.Unity.GameHost.Instance.RunInputScript(\"$script\"); return \"started\";" >/dev/null
  wait_script_done; echo "[$name] script done"
  ev 'return Corum2.EditorTools.EvidenceRecorder.Stop();'
  unity command editor.playmode stop $C >/dev/null 2>&1
  wait_edit
  ev "$INPUT_FOCUS_OFF" >/dev/null
  cp "$P/Diagnostics/Runs/$run".* "$OUT/" 2>/dev/null
  echo "$name $run" >> "$OUT/runs.txt"
}

PARRY='0.3:tc=TC-07-04;0.3:spawn=TestHitter;0.32:await=hitter1:AttackStartup;0.50:kb=L:down;0.75:kb=L:up;0.77:tc=TC-07-05;0.77:kbtap=J;''1.5:await=hitter1:Idle;1.6:tc=TC-07-04;1.6:spawn=TestHitter;1.62:await=hitter1:AttackStartup;1.70:kb=L:down;2.1:kb=L:up;3.0:mark=ScriptEnd;3.1:flush'

run_scene parry "$PARRY"
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP02_Combat.unity"); return "reopened";' >/dev/null
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo done; cat "$OUT/runs.txt"
