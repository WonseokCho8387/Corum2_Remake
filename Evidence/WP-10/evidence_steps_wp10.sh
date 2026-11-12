#!/usr/bin/env bash
# WP-10 evidence: integration TC-INT-01/03..08 (main: WP10_Integration, int07: WP03_Ring, int08: WP05_MagicDeath) (zones 30 m apart), virtual keyboard, recorded video.
# Keys: J attack, K special (launcher; in the air with a direction = dive), L guard (RB), Space jump, A/D/S/W directions.
# In the air: direction + J = air dash, neutral J = air normal attack.
# Usage: wp06_evidence.sh NAME KIND [novideo]   KIND = main | int07 | int08
set -u
NAME=${1:-main}
KIND=${2:-main}
VIDEO=${3:-video}
source C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad/uhelpers.sh
P=/d/ProjectR/Corum2Remake
OUT="$P/Diagnostics/WP10/$NAME"
mkdir -p "$OUT"

MAIN='0.00:hp=100000;0.20:tc=TC-INT-01;0.20:cunlock=0,1;0.20:spawn=I1Mix;0.25:mark=TenMixedTargets;0.50:kbtap=J;0.80:kbtap=J;1.10:kbtap=J;1.40:kbtap=J;1.70:kbtap=K;2.40:kbtap=Space;2.60:kbtap=J;2.75:kbtap=J;3.05:kbtap=J;3.35:kbtap=J;3.65:kb=D:down;3.70:kbtap=K;3.90:kb=D:up;5.60:cunlock=0,0;5.70:tc=TC-INT-03;5.70:cunlock=1,0;5.70:spawn=I3Air;5.75:mark=DashJumpDiveWithInputs;5.80:kbtap=Space;5.95:kb=D:down;5.97:kbtap=J;6.00:kb=D:up;6.01:kbtap=L;6.05:kbtap=J;6.30:kbtap=Space;6.32:kbtap=L;6.55:kb=D:down;6.57:kbtap=K;6.60:kb=D:up;6.61:kbtap=L;6.63:kbtap=J;8.60:cunlock=0,0;8.70:tc=TC-INT-04;8.70:spawn=I4ArmorFar;8.75:mark=BossPhase2Setup;8.80:ehp=wb:110;8.80:ehp=wb3:110;8.80:ehp=wb4:110;9.90:spawn=I4Armor;9.90:await=wb:AttackStartup:6;9.90:mark=BossArmorPhysical;9.95:kbtap=J;12.40:spawn=I4ArmorBFar;12.90:spawn=I4ArmorB;12.90:await=wb3:Idle:6;12.90:await=wb3:AttackStartup:6;12.90:mark=BossArmorMagic;12.90:kb=S:down;12.93:kb=D:down;12.96:kb=S:up;12.99:kbtap=J;13.10:kb=D:up;14.90:spawn=I4ArmorCFar;15.40:spawn=I4ArmorC;15.40:await=wb4:AttackStartup:6;15.40:mark=BossArmorSimultaneous;15.58:kbtap=J;17.40:tc=TC-INT-05;17.40:spawn=I5Mixed;17.45:mark=MixedAiBattle;17.90:kb=L:down;19.40:kb=L:up;19.70:kbtap=J;20.00:kbtap=J;20.60:kb=S:down;20.62:kb=D:down;20.65:kbtap=Space;20.80:kb=D:up;20.82:kb=S:up;21.40:kb=L:down;22.90:kb=L:up;23.40:kbtap=Space;23.60:kbtap=J;24.40:kb=L:down;25.90:kb=L:up;26.40:tc=TC-INT-06;26.40:spawn=I6Bosses;26.45:mark=TwoBossTransitions;27.40:ehp=wb2:110;27.45:kbtap=J;29.40:bossevent=sn:shift;30.40:kb=L:down;32.40:kb=L:up;34.40:mark=ScriptEnd;34.50:flush'

INT07='0.00:tc=TC-INT-07;0.00:spawn=TestBags;0.20:mark=RegisterThenCancel;0.30:padtap=LeftShoulder;0.35:pad=0,1;0.45:padtap=Y;0.60:padtap=B;0.75:pad=0,0;1.40:mark=QuestDiscardRefused;1.45:pad=0,-1;1.60:padtap=B;1.80:padtap=B;2.00:pad=0,0;2.40:mark=NormalDiscardDone;2.45:pad=-0.7,-0.7;2.60:padtap=B;2.80:padtap=A;2.85:pad=0,0;3.60:mark=RegisterNeutralDpad;3.70:padtap=LeftShoulder;3.75:pad=0,1;3.85:padtap=Y;3.95:padtap=DpadUp;4.05:pad=0,0;4.20:padtap=DpadUp;5.20:mark=AfterClose;5.50:padtap=X;6.20:mark=ScriptEnd;6.30:flush'

INT08='0.00:tc=TC-INT-08;0.00:hp=1000;0.20:spawn=TestBags;0.25:mark=WakeupTargetAttack;0.30:kbtap=K;0.40:await=bag1:Wakeup:6;0.42:kbtap=J;0.70:kbtap=J;3.70:spawn=TestCluster;3.75:mark=DeadTargetAttack;3.90:kbtap=J;4.20:kbtap=J;4.50:kbtap=J;4.80:kbtap=J;5.10:kbtap=J;5.40:kbtap=J;6.70:spawn=TestHitter;6.70:item=feather:1;6.70:hp=1;6.75:mark=DeathRevive;10.70:padtap=A;12.70:item=feather:1;12.70:hp=1;12.75:mark=DeathDecline;16.70:padtap=B;17.70:pad=0,-1;17.80:pad=0,0;18.00:pad=0,-1;18.10:pad=0,0;18.70:mark=SelectLobby;18.75:padtap=A;19.50:pad=0,1;19.60:pad=0,0;19.90:mark=SelectLoadSave;19.90:padtap=A;20.50:pad=0,1;20.60:pad=0,0;20.90:mark=SelectCheckpoint;20.90:padtap=A;21.70:mark=ScriptEnd;21.80:flush'

case "$KIND" in
  int07) S="$INT07"; SCENE=Assets/Corum2/Scenes/WP03_Ring.unity ;;
  int08) S="$INT08"; SCENE=Assets/Corum2/Scenes/WP05_MagicDeath.unity ;;
  *) S="$MAIN"; SCENE=Assets/Corum2/Scenes/WP10_Integration.unity ;;
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
[ "$VIDEO" = video ] && ev "return Corum2.EditorTools.EvidenceRecorder.Start(\"D:/ProjectR/Corum2Remake/Diagnostics/WP10/$NAME/$NAME-$run\", 1280, 800, 50);"
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
