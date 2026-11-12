#!/usr/bin/env bash
# R1 recorded runs on clean HEAD: WP-07 main + Inspector hitstop 0.10; WP-08 main + Inspector parry window 0.10 / 0.20.
W=C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad
source $W/uhelpers.sh
P=/d/ProjectR/Corum2Remake
setf() { # asset property value
  ev "var a = UnityEditor.AssetDatabase.LoadMainAssetAtPath(\"$1\"); var so = new UnityEditor.SerializedObject(a); so.FindProperty(\"$2\").floatValue = $3f; so.ApplyModifiedProperties(); UnityEditor.EditorUtility.SetDirty(a); UnityEditor.AssetDatabase.SaveAssets(); return \"$2=$3\";"
}
restore() {
  (cd $P && git checkout -- $1)
  ev "UnityEditor.AssetDatabase.ImportAsset(\"$1\", UnityEditor.ImportAssetOptions.ForceUpdate); return \"reimported\";"
  (cd $P && git status --short)
}
C7=Assets/Corum2/Data/WP07/CombatConfig_WP07.asset
C2=Assets/Corum2/Data/WP02/CombatConfig.asset
rm -rf $P/Diagnostics/WP07/r1main $P/Diagnostics/WP07/r1hs $P/Diagnostics/WP08/r1main $P/Diagnostics/WP08/r1pw10 $P/Diagnostics/WP08/r1pw20
bash $W/wp07_evidence.sh r1main main video
setf $C7 "groundNormals.Array.data[0].hitstop" 0.10
bash $W/wp07_evidence.sh r1hs hs video
restore $C7
bash $W/wp08_evidence.sh r1main main video
setf $C2 parryWindow 0.10
bash $W/wp08_evidence.sh r1pw10 pw video
restore $C2
setf $C2 parryWindow 0.20
bash $W/wp08_evidence.sh r1pw20 pw video
restore $C2
echo RECORD_DONE
