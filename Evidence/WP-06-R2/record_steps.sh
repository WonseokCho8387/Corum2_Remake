#!/usr/bin/env bash
# R2 recorded runs on the clean R2 commit: EditMode XML, then WP-06 (main, cw 0.2 / 0.6), WP-07 (main, hitstop 0.10),
# WP-08 (main, parry window 0.10 / 0.20), WP-10-R1 (main, int07, int08). Inspector runs are set with SerializedObject and
# restored from git (and reimported) right after each run.
W=C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad
source $W/uhelpers.sh
P=/d/ProjectR/Corum2Remake
cd $P; git status --short; git log --oneline -1
setf() { # asset property value
  ev "var a = UnityEditor.AssetDatabase.LoadMainAssetAtPath(\"$1\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"$2\").floatValue = $3f; so.ApplyModifiedProperties(); UnityEditor.EditorUtility.SetDirty(a); UnityEditor.AssetDatabase.SaveAssets(); return \"$2=$3\";"
}
restore() {
  (cd $P && git checkout -- $1)
  ev "UnityEditor.AssetDatabase.ImportAsset(\"$1\", UnityEditor.ImportAssetOptions.ForceUpdate); return \"reimported\";"
  (cd $P && git status --short)
}
C2=Assets/Corum2/Data/WP02/CombatConfig.asset
C7=Assets/Corum2/Data/WP07/CombatConfig_WP07.asset
wait_editor
unity command test run --mode EditMode --filter Corum2 --timeout 300 $C --format json > $W/r2_tests.json 2>&1
grep -m1 -A4 '"Summary"' $W/r2_tests.json
cp /c/Users/wscho/AppData/LocalLow/DefaultCompany/Corum2Remake/TestResults.xml $W/r2_TestResults.xml
rm -rf $P/Diagnostics/WP06R2 $P/Diagnostics/WP07R2 $P/Diagnostics/WP08R2 $P/Diagnostics/WP10R1
bash $W/wp06r2_evidence.sh r2main main video
setf $C2 comboWindow 0.2; bash $W/wp06r2_evidence.sh r2cw02 cw video; restore $C2
setf $C2 comboWindow 0.6; bash $W/wp06r2_evidence.sh r2cw06 cw video; restore $C2
bash $W/wp07r2_evidence.sh r2main main video
setf $C7 "groundNormals.Array.data[0].hitstop" 0.10; bash $W/wp07r2_evidence.sh r2hs hs video; restore $C7
bash $W/wp08r2_evidence.sh r2main main video
setf $C2 parryWindow 0.10; bash $W/wp08r2_evidence.sh r2pw10 pw video; restore $C2
setf $C2 parryWindow 0.20; bash $W/wp08r2_evidence.sh r2pw20 pw video; restore $C2
bash $W/wp10r1_evidence.sh r1main main video
bash $W/wp10r1_evidence.sh r1int07 int07 video
bash $W/wp10r1_evidence.sh r1int08 int08 video
cd $P; git status --short
echo RECORD_DONE
