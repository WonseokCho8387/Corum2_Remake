#!/usr/bin/env bash
# WP-11 runs: opens a scene, plays one input script (from a file), optionally records video, copies the run log.
# Keys: J attack, K special, L guard (RB), Q ring (LB), Space jump, A/D/S/W directions. Pad: A jump, X attack, Y special, RB guard, LB ring.
# Usage: wp11_run.sh NAME SCRIPTFILE [SCENE] [video|novideo] [OUTDIR]
set -u
NAME=$1
SF=$2
SCENE=${3:-Assets/Corum2/Scenes/WP11_Evidence.unity}
VIDEO=${4:-video}
OUTD=${5:-WP11}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/$OUTD/$NAME"
mkdir -p "$OUT"
S=$(cat "$SF")
wait_editor
ev "UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\"$SCENE\"); return \"opened\";" >/dev/null
unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
wait_playing; echo "playing"
run=$(ev 'return Corum2.Unity.GameHost.Instance.RunId;' | grep -o 'RUN-[0-9-]*')
echo "$run"
ev "$INPUT_FOCUS_ON" >/dev/null
ev 'UnityEngine.Camera.main.aspect = 1.6f; return "aspect";' >/dev/null
[ "$VIDEO" = video ] && ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/$OUTD/$NAME/$NAME-$run\", 1280, 800, 50);"
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
