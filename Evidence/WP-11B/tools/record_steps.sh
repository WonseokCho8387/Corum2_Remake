#!/usr/bin/env bash
# WP-11 Parts B/C recorded runs on the clean commit: EditMode XML; Part B base, base2 and five one-value Inspector variants
# (set with SerializedObject, restored from git and reimported right after each run); Part C 50 Hz and 60 Hz runs.
W=C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad
source $W/uhelpers.sh
P=/d/ProjectR/Corum2Remake
cd $P; git status --short; git log --oneline -1
setb() { # asset property 0|1
  ev "var a = UnityEditor.AssetDatabase.LoadMainAssetAtPath(\"$1\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"$2\").boolValue = $3 == 1; so.ApplyModifiedProperties(); UnityEditor.EditorUtility.SetDirty(a); UnityEditor.AssetDatabase.SaveAssets(); return \"$2=$3\";"
}
setf() { # asset property value
  ev "var a = UnityEditor.AssetDatabase.LoadMainAssetAtPath(\"$1\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"$2\").floatValue = $3f; so.ApplyModifiedProperties(); UnityEditor.EditorUtility.SetDirty(a); UnityEditor.AssetDatabase.SaveAssets(); return \"$2=$3\";"
}
restore() {
  (cd $P && git checkout -- $1)
  ev "UnityEditor.AssetDatabase.ImportAsset(\"$1\", UnityEditor.ImportAssetOptions.ForceUpdate); return \"reimported\";"
  (cd $P && git status --short)
}
AU=Assets/Corum2/Data/WP11/AirUnlocks_Ops.asset
CU=Assets/Corum2/Data/WP11/CombatUnlocks_Ops.asset
CC=Assets/Corum2/Data/WP11/CombatConfig_Ops.asset
OPS=Assets/Corum2/Scenes/WP11_Ops.unity
wait_editor
unity command test run --mode EditMode --filter Corum2 --timeout 400 $C --format json > $W/wp11bc_tests.json 2>&1
grep -m1 -A5 '"Summary"' $W/wp11bc_tests.json
cp /c/Users/wscho/AppData/LocalLow/DefaultCompany/Corum2Remake/TestResults.xml $W/wp11bc_TestResults.xml
rm -rf $P/Diagnostics/WP11B $P/Diagnostics/WP11C
python $W/wp11b_gen.py
python $W/wp11c_gen.py 50
python $W/wp11c_gen.py 60
R() { bash $W/wp11_run.sh $1 $W/wp11b_main.txt $OPS video WP11B 50; }
R base
R base2
setb $AU extraJump 0; R v1; restore $AU
setb $AU airDash 0; R v2; restore $AU
setb $CU dashPierce 1; R v3; restore $CU
setb $CU diveArea 1; R v4; restore $CU
setf $CC comboWindow 0.3; R v5; restore $CC
bash $W/wp11_run.sh s50 $W/wp11c_50.txt $OPS video WP11C 50
bash $W/wp11_run.sh s60 $W/wp11c_60.txt $OPS video WP11C 60
ev 'return UnityEngine.Time.fixedDeltaTime.ToString();'
cd $P; git status --short
echo RECORD_DONE
