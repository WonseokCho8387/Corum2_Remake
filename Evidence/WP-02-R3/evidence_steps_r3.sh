#!/usr/bin/env bash
# WP-02-R3 source-list check run (arg: clean|dirty): short WP02_Combat run, manifest copied at revision 1 and at the end.
set -u
NAME=${1:-clean}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP02R3/$NAME"
mkdir -p "$OUT"
SCRIPT='0.2:tc=WP02-M01-R1;0.3:kbtap=J;0.8:kb=D:down;1.6:kb=D:up;1.8:kbtap=Space;2.6:mark=ScriptEnd;2.7:flush'
wait_editor
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP02_Combat.unity"); return "opened";' >/dev/null
unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
wait_playing; echo "playing"
run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
echo "$run"
cp "$P/Diagnostics/Runs/$run.manifest.yaml" "$OUT/$run.manifest.at-start.yaml"
sleep 3
ev "$INPUT_FOCUS_ON" >/dev/null
ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP02R3/$NAME/$NAME-$run\", 1280, 800, 50);"
ev "Corum2.Unity.GameHost.Instance.RunInputScript(\"$SCRIPT\"); return \"started\";" >/dev/null
wait_script_done; echo "script done"
ev 'return Corum2.EditorTools.EvidenceRecorder.Stop();'
unity command editor.playmode stop $C >/dev/null 2>&1
wait_edit
ev "$INPUT_FOCUS_OFF" >/dev/null
cp "$P/Diagnostics/Runs/$run".* "$OUT/"
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo "done $run"
