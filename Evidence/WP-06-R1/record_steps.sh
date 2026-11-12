#!/usr/bin/env bash
# WP-06 R1 recorded runs on a clean HEAD: main, then the two Inspector combo-window runs (dirty, restored from git after each).
W=C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad
source $W/uhelpers.sh
P=/d/ProjectR/Corum2Remake
CFG=Assets/Corum2/Data/WP02/CombatConfig.asset
setcw() {
  ev "var a = UnityEditor.AssetDatabase.LoadMainAssetAtPath(\"$CFG\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"comboWindow\").floatValue = $1f; so.ApplyModifiedProperties(); UnityEditor.EditorUtility.SetDirty(a); UnityEditor.AssetDatabase.SaveAssets(); return \"cw=$1\";"
}
restore() {
  (cd $P && git checkout -- $CFG)
  ev "UnityEditor.AssetDatabase.ImportAsset(\"$CFG\", UnityEditor.ImportAssetOptions.ForceUpdate); return \"reimported\";"
  (cd $P && git status --short)
}
rm -rf $P/Diagnostics/WP06/r1cw02 $P/Diagnostics/WP06/r1cw06

setcw 0.2
bash $W/wp06_evidence.sh r1cw02 cw video
restore
setcw 0.6
bash $W/wp06_evidence.sh r1cw06 cw video
restore
echo RECORD_DONE
