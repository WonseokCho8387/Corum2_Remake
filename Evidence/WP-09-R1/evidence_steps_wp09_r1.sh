#!/usr/bin/env bash
# WP-09-R1 evidence: P0-01..03 residual TCs (main: WP06_ComboState scene; plane: WP04_AI scene) (zones 30 m apart), virtual keyboard, recorded video.
# Keys: J attack, K special (launcher; in the air with a direction = dive), L guard (RB), Space jump, A/D/S/W directions.
# In the air: direction + J = air dash, neutral J = air normal attack.
# Usage: wp06_evidence.sh NAME KIND [novideo]   KIND = main | plane | dev | slide
set -u
NAME=${1:-main}
KIND=${2:-main}
VIDEO=${3:-video}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP09/$NAME"
mkdir -p "$OUT"

MAIN='0.00:hp=1000;0.20:tc=TC-02-01;0.20:spawn=Start;0.25:mark=MapJump;0.30:kbtap=Space;1.20:padtap=A;2.10:mark=MapAttack;2.20:kbtap=J;2.70:padtap=X;3.30:mark=MapSpecial;3.40:kbtap=K;4.10:padtap=Y;4.90:mark=MapGuard;5.00:kb=L:down;5.20:kb=L:up;5.40:padbtn=RightShoulder:down;5.60:padbtn=RightShoulder:up;5.90:mark=MapMove;6.00:kb=D:down;6.30:kb=D:up;6.50:pad=1,0;6.80:pad=0,0;7.10:mark=MapRing;7.20:kbtap=Q;7.40:kbtap=Z;7.60:kbtap=C;7.80:kbtap=E;8.00:kbtap=Q;8.30:padtap=LeftShoulder;8.50:padtap=LeftTrigger;8.70:padtap=RightTrigger;8.90:padtap=B;9.10:padtap=LeftShoulder;9.70:mark=MapQuickslot;9.80:kbtap=Digit3;11.10:padtap=DpadDown;11.70:tc=TC-03-01;11.70:spawn=Start;11.75:mark=StrongTiltWalk;11.80:pad=1,0;12.90:pad=0,0;13.30:spawn=Start;13.35:mark=PadDoubleTapRun;13.40:pad=1,0;13.46:pad=0,0;13.52:pad=1,0;14.50:pad=0,0;14.90:spawn=Start;14.95:mark=KeyDoubleTapRun;15.00:kb=D:down;15.06:kb=D:up;15.12:kb=D:down;16.10:kb=D:up;16.50:tc=TC-03-06;16.50:spawn=Start;16.55:mark=AirDirectionOnly;16.60:kbtap=Space;16.75:kb=D:down;17.15:kb=D:up;17.90:spawn=Start;17.95:mark=AirDoubleTap;18.00:kbtap=Space;18.15:kb=D:down;18.20:kb=D:up;18.26:kb=D:down;18.50:kb=D:up;19.30:spawn=Start;19.35:mark=AirA;19.40:kbtap=Space;19.70:kbtap=Space;20.90:spawn=Start;20.95:mark=AirDirAttack;21.00:kbtap=Space;21.20:kb=D:down;21.22:kbtap=J;21.40:kb=D:up;22.30:spawn=Start;22.35:mark=AirDirSpecial;22.40:kbtap=Space;22.60:kb=D:down;22.62:kbtap=K;22.80:kb=D:up;23.70:spawn=Start;23.75:mark=AirRB;23.80:kbtap=Space;24.00:kb=L:down;24.30:kb=L:up;25.10:tc=TC-03-07;25.10:spawn=Start;25.15:mark=DashThenJump;25.20:kbtap=Space;25.35:kb=D:down;25.37:kbtap=J;25.50:kb=D:up;25.70:kbtap=Space;25.85:kb=D:down;25.87:kbtap=J;25.95:kb=D:up;26.05:kbtap=Space;27.10:spawn=Start;27.15:mark=JumpThenDash;27.20:kbtap=Space;27.40:kbtap=Space;27.60:kb=D:down;27.62:kbtap=J;27.70:kb=D:up;28.00:kbtap=Space;29.10:spawn=Start;29.15:mark=AirAttackKeepsCounts;29.20:kbtap=Space;29.35:kbtap=J;29.70:kbtap=Space;29.85:kb=D:down;29.87:kbtap=J;30.00:kb=D:up;31.10:tc=TC-03-08;31.10:spawn=Start;31.15:mark=DiveThenAAndDash;31.20:kbtap=Space;31.45:kb=D:down;31.47:kbtap=K;31.50:kbtap=Space;31.53:kbtap=J;31.70:kb=D:up;32.70:tc=TC-03-09;32.70:spawn=Start;32.75:mark=LandThenAttack;32.80:kbtap=Space;33.00:kbtap=Space;33.84:kbtap=J;34.30:spawn=Start;34.35:mark=LandThenGuard;34.40:kbtap=Space;35.18:kb=L:down;35.38:kb=L:up;35.90:spawn=Start;35.95:mark=LandThenJumpAgain;36.00:kbtap=Space;36.20:kbtap=Space;37.04:kbtap=Space;37.35:kbtap=Space;37.90:mark=ScriptEnd;38.00:flush'

PLANE='0.00:hp=100000;0.00:tc=TC-01-01;0.20:spawn=Z2Chaser;0.25:mark=Plane_Z2Chaser;0.40:kbtap=J;0.80:kbtap=Space;1.40:kb=A:down;1.90:kb=A:up;2.10:kbtap=Space;2.40:kb=D:down;2.90:kb=D:up;3.40:pad=0,1;3.50:padtap=A;3.70:pad=0.7,0.7;4.00:pad=1,0;4.30:pad=0.7,-0.7;4.60:pad=0,-1;4.90:pad=-0.7,-0.7;5.20:pad=-1,0;5.50:pad=-0.7,0.7;5.60:padtap=A;5.80:pad=0,0;5.90:kb=W:down;5.90:kb=D:down;6.00:kbtap=Space;6.20:kb=D:up;6.20:kb=A:down;6.50:kb=A:up;6.50:kb=W:up;6.80:spawn=Z3Fleer;6.85:mark=Plane_Z3Fleer;7.00:kbtap=J;7.40:kbtap=Space;8.00:kb=A:down;8.50:kb=A:up;8.70:kbtap=Space;9.00:kb=D:down;9.50:kb=D:up;10.00:pad=0,1;10.10:padtap=A;10.30:pad=0.7,0.7;10.60:pad=1,0;10.90:pad=0.7,-0.7;11.20:pad=0,-1;11.50:pad=-0.7,-0.7;11.80:pad=-1,0;12.10:pad=-0.7,0.7;12.20:padtap=A;12.40:pad=0,0;12.50:kb=W:down;12.50:kb=D:down;12.60:kbtap=Space;12.80:kb=D:up;12.80:kb=A:down;13.10:kb=A:up;13.10:kb=W:up;13.40:spawn=Z5Jumper;13.45:mark=Plane_Z5Jumper;13.60:kbtap=J;14.00:kbtap=Space;14.60:kb=A:down;15.10:kb=A:up;15.30:kbtap=Space;15.60:kb=D:down;16.10:kb=D:up;16.60:pad=0,1;16.70:padtap=A;16.90:pad=0.7,0.7;17.20:pad=1,0;17.50:pad=0.7,-0.7;17.80:pad=0,-1;18.10:pad=-0.7,-0.7;18.40:pad=-1,0;18.70:pad=-0.7,0.7;18.80:padtap=A;19.00:pad=0,0;19.10:kb=W:down;19.10:kb=D:down;19.20:kbtap=Space;19.40:kb=D:up;19.40:kb=A:down;19.70:kb=A:up;19.70:kb=W:up;20.00:spawn=Z6Ranged;20.05:mark=Plane_Z6Ranged;20.20:kbtap=J;20.60:kbtap=Space;21.20:kb=A:down;21.70:kb=A:up;21.90:kbtap=Space;22.20:kb=D:down;22.70:kb=D:up;23.20:pad=0,1;23.30:padtap=A;23.50:pad=0.7,0.7;23.80:pad=1,0;24.10:pad=0.7,-0.7;24.40:pad=0,-1;24.70:pad=-0.7,-0.7;25.00:pad=-1,0;25.30:pad=-0.7,0.7;25.40:padtap=A;25.60:pad=0,0;25.70:kb=W:down;25.70:kb=D:down;25.80:kbtap=Space;26.00:kb=D:up;26.00:kb=A:down;26.30:kb=A:up;26.30:kb=W:up;26.60:spawn=Z7Boss;26.65:mark=Plane_Z7Boss;26.80:kbtap=J;27.20:kbtap=Space;27.80:kb=A:down;28.30:kb=A:up;28.50:kbtap=Space;28.80:kb=D:down;29.30:kb=D:up;29.80:pad=0,1;29.90:padtap=A;30.10:pad=0.7,0.7;30.40:pad=1,0;30.70:pad=0.7,-0.7;31.00:pad=0,-1;31.30:pad=-0.7,-0.7;31.60:pad=-1,0;31.90:pad=-0.7,0.7;32.00:padtap=A;32.20:pad=0,0;32.30:kb=W:down;32.30:kb=D:down;32.40:kbtap=Space;32.60:kb=D:up;32.60:kb=A:down;32.90:kb=A:up;32.90:kb=W:up;33.20:mark=ScriptEnd;33.30:flush'

DEV='0.00:spawn=TestBags;0.00:tc=TC-02-01;0.30:mark=DevKb_RegisterUp;0.32:kbtap=Q;0.38:kb=W:down;0.44:kbtap=K;0.50:kb=W:up;0.56:kbtap=Digit1;0.80:mark=DevKb_RegisterRight;0.82:kbtap=Q;0.88:kb=D:down;0.94:kbtap=K;1.00:kb=D:up;1.06:kbtap=Digit2;1.30:mark=DevKb_RegisterDown;1.32:kbtap=Q;1.38:kb=S:down;1.38:kb=A:down;1.44:kbtap=K;1.50:kb=S:up;1.50:kb=A:up;1.56:kbtap=Digit3;1.80:mark=DevKb_RegisterLeft;1.82:kbtap=Q;1.88:kb=A:down;1.94:kbtap=K;2.00:kb=A:up;2.06:kbtap=Digit4;2.30:mark=DevKb_UseQuickslots;2.32:hp=10;2.40:kbtap=Digit1;3.60:kbtap=Digit2;4.80:kbtap=Digit3;6.00:kbtap=Digit4;7.30:mark=DevKb_Pages;7.32:kbtap=Q;7.38:kbtap=C;7.44:kb=W:down;7.50:kb=W:up;7.56:kbtap=Z;7.64:kbtap=Q;7.90:mark=DevKb_Discard;7.92:kbtap=Q;7.98:kb=W:down;7.98:kb=A:down;8.04:kbtap=E;8.10:kb=W:up;8.10:kb=A:up;8.16:kbtap=Space;8.50:item=potion:3;8.50:item=hipotion:1;8.50:item=herb:2;8.50:item=scroll:2;8.50:item=key:1;8.50:item=bread:2;8.50:item=water:1;8.50:item=apple:3;8.50:item=elixir:1;8.50:item=meat:1;8.60:mark=DevPad_RegisterUp;8.62:padtap=LeftShoulder;8.68:pad=0,1;8.74:padtap=Y;8.80:pad=0,0;8.86:padtap=DpadUp;9.10:mark=DevPad_RegisterRight;9.12:padtap=LeftShoulder;9.18:pad=1,0;9.24:padtap=Y;9.30:pad=0,0;9.36:padtap=DpadRight;9.60:mark=DevPad_RegisterDown;9.62:padtap=LeftShoulder;9.68:pad=-0.7,-0.7;9.74:padtap=Y;9.80:pad=0,0;9.86:padtap=DpadDown;10.10:mark=DevPad_RegisterLeft;10.12:padtap=LeftShoulder;10.18:pad=-1,0;10.24:padtap=Y;10.30:pad=0,0;10.36:padtap=DpadLeft;10.60:mark=DevPad_UseQuickslots;10.62:hp=10;10.70:padtap=DpadUp;11.90:padtap=DpadRight;13.10:padtap=DpadDown;14.30:padtap=DpadLeft;15.60:mark=DevPad_Pages;15.62:padtap=LeftShoulder;15.68:padtap=RightTrigger;15.74:pad=0,1;15.80:pad=0,0;15.86:padtap=LeftTrigger;15.94:padtap=LeftShoulder;16.20:mark=DevPad_Discard;16.22:padtap=LeftShoulder;16.28:pad=-0.7,0.7;16.34:padtap=B;16.40:pad=0,0;16.46:padtap=A;16.80:mark=ScriptEnd;16.90:flush'

SLIDE='0.00:tc=TC-03-04;0.00:spawn=TestPassageSlide;0.05:mark=SlideLeftKb;0.10:kb=S:down;1.50:kb=S:up;0.30:kbtap=Space;1.00:kbtap=Space;1.70:kb=A:down;1.74:kb=A:up;1.90:kb=S:down;2.10:kbtap=Space;2.80:kbtap=Space;3.60:kb=S:up;4.20:spawn=TestPassageSlide;4.25:mark=SlideLeftPad;4.30:pad=0,-1;4.50:padtap=A;5.20:padtap=A;5.70:pad=0,0;5.90:pad=-1,0;5.94:pad=0,0;6.10:pad=0,-1;6.30:padtap=A;7.00:padtap=A;7.80:pad=0,0;8.40:mark=ScriptEnd;8.50:flush'

case "$KIND" in
  plane) S="$PLANE"; SCENE=Assets/Corum2/Scenes/WP04_AI.unity ;;
  dev) S="$DEV"; SCENE=Assets/Corum2/Scenes/WP03_Ring.unity ;;
  slide) S="$SLIDE"; SCENE=Assets/Corum2/Scenes/WP01_MapMovement.unity ;;
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
