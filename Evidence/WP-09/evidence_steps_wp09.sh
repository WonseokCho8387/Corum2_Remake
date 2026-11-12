#!/usr/bin/env bash
# WP-09 evidence: P0-01..03 residual TCs (main: WP06_ComboState scene; plane: WP04_AI scene) (zones 30 m apart), virtual keyboard, recorded video.
# Keys: J attack, K special (launcher; in the air with a direction = dive), L guard (RB), Space jump, A/D/S/W directions.
# In the air: direction + J = air dash, neutral J = air normal attack.
# Usage: wp06_evidence.sh NAME KIND [novideo]   KIND = main | plane
set -u
NAME=${1:-main}
KIND=${2:-main}
VIDEO=${3:-video}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP09/$NAME"
mkdir -p "$OUT"

MAIN='0.00:hp=1000;0.20:tc=TC-02-01;0.20:spawn=Start;0.25:mark=MapJump;0.30:kbtap=Space;1.20:padtap=A;2.10:mark=MapAttack;2.20:kbtap=J;2.70:padtap=X;3.30:mark=MapSpecial;3.40:kbtap=K;4.10:padtap=Y;4.90:mark=MapGuard;5.00:kb=L:down;5.20:kb=L:up;5.40:padbtn=RightShoulder:down;5.60:padbtn=RightShoulder:up;5.90:mark=MapMove;6.00:kb=D:down;6.30:kb=D:up;6.50:pad=1,0;6.80:pad=0,0;7.10:mark=MapRing;7.20:kbtap=Q;7.40:kbtap=Z;7.60:kbtap=C;7.80:kbtap=E;8.00:kbtap=Q;8.30:padtap=LeftShoulder;8.50:padtap=LeftTrigger;8.70:padtap=RightTrigger;8.90:padtap=B;9.10:padtap=LeftShoulder;9.70:mark=MapQuickslot;9.80:kbtap=Digit3;11.10:padtap=DpadDown;11.70:tc=TC-03-01;11.70:spawn=Start;11.75:mark=StrongTiltWalk;11.80:pad=1,0;12.90:pad=0,0;13.30:spawn=Start;13.35:mark=PadDoubleTapRun;13.40:pad=1,0;13.46:pad=0,0;13.52:pad=1,0;14.50:pad=0,0;14.90:spawn=Start;14.95:mark=KeyDoubleTapRun;15.00:kb=D:down;15.06:kb=D:up;15.12:kb=D:down;16.10:kb=D:up;16.50:tc=TC-03-06;16.50:spawn=Start;16.55:mark=AirDirectionOnly;16.60:kbtap=Space;16.75:kb=D:down;17.15:kb=D:up;17.90:spawn=Start;17.95:mark=AirDoubleTap;18.00:kbtap=Space;18.15:kb=D:down;18.20:kb=D:up;18.26:kb=D:down;18.50:kb=D:up;19.30:spawn=Start;19.35:mark=AirA;19.40:kbtap=Space;19.70:kbtap=Space;20.90:spawn=Start;20.95:mark=AirDirAttack;21.00:kbtap=Space;21.20:kb=D:down;21.22:kbtap=J;21.40:kb=D:up;22.30:spawn=Start;22.35:mark=AirDirSpecial;22.40:kbtap=Space;22.60:kb=D:down;22.62:kbtap=K;22.80:kb=D:up;23.70:spawn=Start;23.75:mark=AirRB;23.80:kbtap=Space;24.00:kb=L:down;24.30:kb=L:up;25.10:tc=TC-03-07;25.10:spawn=Start;25.15:mark=DashThenJump;25.20:kbtap=Space;25.35:kb=D:down;25.37:kbtap=J;25.50:kb=D:up;25.70:kbtap=Space;25.85:kb=D:down;25.87:kbtap=J;25.95:kb=D:up;26.05:kbtap=Space;27.10:spawn=Start;27.15:mark=JumpThenDash;27.20:kbtap=Space;27.40:kbtap=Space;27.60:kb=D:down;27.62:kbtap=J;27.70:kb=D:up;28.00:kbtap=Space;29.10:spawn=Start;29.15:mark=AirAttackKeepsCounts;29.20:kbtap=Space;29.35:kbtap=J;29.70:kbtap=Space;29.85:kb=D:down;29.87:kbtap=J;30.00:kb=D:up;31.10:tc=TC-03-08;31.10:spawn=Start;31.15:mark=DiveThenAAndDash;31.20:kbtap=Space;31.45:kb=D:down;31.47:kbtap=K;31.50:kbtap=Space;31.53:kbtap=J;31.70:kb=D:up;32.70:tc=TC-03-09;32.70:spawn=Start;32.75:mark=LandThenAttack;32.80:kbtap=Space;33.00:kbtap=Space;33.84:kbtap=J;34.30:spawn=Start;34.35:mark=LandThenGuard;34.40:kbtap=Space;35.18:kb=L:down;35.38:kb=L:up;35.90:spawn=Start;35.95:mark=LandThenJumpAgain;36.00:kbtap=Space;36.20:kbtap=Space;37.04:kbtap=Space;37.35:kbtap=Space;37.90:mark=ScriptEnd;38.00:flush'

PLANE='0.00:hp=100000;0.00:tc=TC-01-01;0.20:spawn=Z2Chaser;0.25:mark=Plane_Z2Chaser;0.40:kbtap=J;0.80:kbtap=Space;1.40:kb=A:down;1.90:kb=A:up;2.10:kbtap=Space;2.40:kb=D:down;2.90:kb=D:up;3.40:spawn=Z3Fleer;3.45:mark=Plane_Z3Fleer;3.60:kbtap=J;4.00:kbtap=Space;4.60:kb=A:down;5.10:kb=A:up;5.30:kbtap=Space;5.60:kb=D:down;6.10:kb=D:up;6.60:spawn=Z5Jumper;6.65:mark=Plane_Z5Jumper;6.80:kbtap=J;7.20:kbtap=Space;7.80:kb=A:down;8.30:kb=A:up;8.50:kbtap=Space;8.80:kb=D:down;9.30:kb=D:up;9.80:spawn=Z6Ranged;9.85:mark=Plane_Z6Ranged;10.00:kbtap=J;10.40:kbtap=Space;11.00:kb=A:down;11.50:kb=A:up;11.70:kbtap=Space;12.00:kb=D:down;12.50:kb=D:up;13.00:spawn=Z7Boss;13.05:mark=Plane_Z7Boss;13.20:kbtap=J;13.60:kbtap=Space;14.20:kb=A:down;14.70:kb=A:up;14.90:kbtap=Space;15.20:kb=D:down;15.70:kb=D:up;16.20:mark=ScriptEnd;16.30:flush'

case "$KIND" in
  plane) S="$PLANE"; SCENE=Assets/Corum2/Scenes/WP04_AI.unity ;;
  *) S="$MAIN"; SCENE=Assets/Corum2/Scenes/WP06_ComboState.unity ;;
esac
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
[ "$VIDEO" = video ] && ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP09/$NAME/$NAME-$run\", 1280, 800, 50);"
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
