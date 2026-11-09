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

MAIN='0.3:spawn=TestBags;0.5:tc=TC-04-01;0.6:kbtap=J;0.85:kbtap=J;1.1:kbtap=J;1.35:kbtap=J;1.6:kbtap=K;1.75:kbtap=J;'\
'2.3:kbtap=Space;2.5:kbtap=J;2.65:kbtap=J;2.95:kbtap=J;3.25:kbtap=J;3.55:kb=D:down;3.6:kbtap=K;3.8:kb=D:up;'\
'5.0:tc=TC-05-01;5.0:spawn=TestOpenWest;5.2:kbtap=J;5.25:kb=D:down;5.45:kb=D:up;'\
'5.8:tc=TC-05-02;5.8:kbtap=J;5.85:kbtap=L;6.1:kbtap=K;6.15:kbtap=L;6.5:kbtap=Space;6.6:kbtap=J;6.65:kbtap=L;'\
'7.5:tc=TC-07-01;7.5:spawn=TestHitter;7.55:await=hitter1:AttackStartup;7.6:kb=L:down;8.3:await=hitter1:AttackStartup;8.4:kb=L:up;'\
'8.6:tc=TC-07-04;8.6:spawn=TestHitter;8.6:await=hitter1:AttackStartup;8.78:kb=L:down;9.0:kb=L:up;9.02:tc=TC-07-05;9.02:kbtap=J;'\
'10.0:tc=TC-07-01;10.0:spawn=TestHitter;10.0:await=hitter1:AttackStartup;10.05:kb=L:down;10.1:kb=A:down;10.2:kb=A:up;10.8:kb=L:up;'\
'11.5:spawn=TestHitter;11.5:await=hitter1:AttackStartup;11.55:kb=L:down;11.6:kb=D:down;11.7:kb=D:up;12.2:kb=L:up;'\
'12.6:tc=TC-06-15;12.6:spawn=TestHitter;12.6:await=hitter1:AttackStartup;12.84:kbtap=J;13.6:mark=dodgeDone;'\
'14.0:tc=TC-06-11;14.0:spawn=TestArmor;14.05:await=armor1:AttackStartup;14.1:kbtap=J;15.2:mark=armorDone;'\
'15.5:tc=TC-04-05;15.5:spawn=TestBoss;15.6:kbtap=K;16.2:kbtap=J;'\
'17.0:tc=TC-04-04;17.0:spawn=TestCluster;17.1:kbtap=J;17.4:kbtap=J;17.7:kbtap=J;18.2:tc=TC-06-17;'\
'19.5:tc=TC-06-09;19.5:kbtap=K;20.2:await=c2:Down;20.3:kb=S:down;20.32:kb=D:down;20.4:kbtap=K;20.7:kb=D:up;20.72:kb=S:up;21.6:mark=ScriptEnd;21.7:flush'

run_scene main "$MAIN"
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Corum2/Scenes/WP02_Combat.unity"); return "reopened";' >/dev/null
unity command editor.playmode set_run_in_background $C -- --enabled false >/dev/null 2>&1
unity command editor set_auto_tick $C -- --enabled false >/dev/null 2>&1
echo done; cat "$OUT/runs.txt"
