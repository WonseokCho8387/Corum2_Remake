#!/usr/bin/env bash
# WP-02-R1 evidence: WP02_Combat scene, virtual keyboard input, recorded video.
# Usage: wp02r1_evidence.sh <outdir-name> [r1|main|both]
set -u
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUTNAME=$1
WHICH=${2:-both}
OUT="$P/Diagnostics/WP02R1/$OUTNAME"
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
  ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP02R1/$OUTNAME/$name-$run\", 1280, 800, 50);"
  ev "Corum2.Unity.GameHost.Instance.RunInputScript(\"$script\"); return \"started\";" >/dev/null
  wait_script_done; echo "[$name] script done"
  ev 'return Corum2.EditorTools.EvidenceRecorder.Stop();'
  unity command editor.playmode stop $C >/dev/null 2>&1
  wait_edit
  ev "$INPUT_FOCUS_OFF" >/dev/null
  cp "$P/Diagnostics/Runs/$run".* "$OUT/" 2>/dev/null
  echo "$name $run" >> "$OUT/runs.txt"
}

# F01 multi Dodge (TC-06-15/16) -> F02 piercing dash one hit event (TC-06-03) -> F03 cap at 10 (TC-04-01)
R1='0.3:tc=TC-06-16;0.3:spawn=TestMultiDodge;0.3:await=md1:AttackStartup;0.54:kbtap=J;1.4:mark=multiDodgeDone;'\
'2.0:tc=TC-06-03;2.0:cunlock=1,0;2.0:spawn=TestPierce;2.2:kbtap=Space;2.28:kb=D:down;2.3:kbtap=J;2.45:kb=D:up;3.6:cunlock=0,0;3.6:mark=pierceDone;'\
'4.0:tc=TC-04-01;4.0:spawn=TestCap;'\
'4.2:kbtap=K;4.6:kbtap=K;5.0:kbtap=K;5.4:kbtap=K;5.8:kbtap=K;6.2:kbtap=K;6.6:kbtap=K;7.0:kbtap=K;7.4:kbtap=K;7.8:kbtap=K;8.2:kbtap=K;8.4:mark=eleventhPressed;'\
'9.5:kbtap=K;9.9:kbtap=K;10.3:kbtap=K;10.7:kbtap=K;11.1:kbtap=K;11.5:kbtap=K;11.9:kbtap=K;12.3:kbtap=K;12.7:kbtap=K;13.1:kbtap=K;'\
'13.5:kbtap=Space;13.65:kb=D:down;13.67:kbtap=K;13.8:kb=D:up;15.0:mark=ScriptEnd;15.1:flush'

MAIN=$(grep -o "^MAIN='.*" /dev/null)
source <(sed -n "/^MAIN=/,/flush'$/p" C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/wp02_evidence.sh)

case "$WHICH" in
  r1) run_scene r1 "$R1" ;;
  main) run_scene main "$MAIN" ;;
  both) run_scene r1 "$R1"; run_scene main "$MAIN" ;;
esac
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP02_Combat.unity"); return "reopened";' >/dev/null
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo done; cat "$OUT/runs.txt"
