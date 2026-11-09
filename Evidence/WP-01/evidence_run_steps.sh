#!/usr/bin/env bash
# WP-01 evidence run: enter Play mode, run a timed input script in GameHost, capture the game view periodically.
set -u
export UNITY_PROJECT_PATH="D:\\ProjectR\\Corum2Remake"
C="--caller plugin --skill unity-cli --no-banner"
P=/d/ProjectR/Corum2Remake
SHOTS_REL="Diagnostics/Shots/$1"
mkdir -p "$P/$SHOTS_REL"

SCRIPT='1.0:tc=TC-03-01;1.0:dir=Right;2.0:dir=None;2.3:dir=Right;2.35:dir=None;2.4:dir=Right;3.3:dir=None;'\
'3.6:tc=TC-03-02;3.6:tap=Jump;4.8:dir=Right;5.0:tap=Jump;5.05:dir=None;6.2:dir=Left;6.25:dir=None;6.3:dir=Left;6.6:tap=Jump;6.65:dir=None;'\
'7.8:tc=TC-03-05;7.8:dir=DownRight;7.9:tap=Jump;8.5:dir=None;8.7:dir=DownLeft;8.8:tap=Jump;9.4:dir=None;'\
'9.8:tc=TC-03-06;9.8:tap=Jump;10.0:dir=Right;10.05:dir=None;10.1:dir=Right;10.25:tap=Jump;10.4:tap=Attack;10.8:dir=DownRight;10.85:tap=SpecialAttack;11.4:dir=None;'\
'11.8:tc=TC-03-08;11.8:tap=Jump;12.1:dir=Right;12.15:tap=SpecialAttack;12.2:tap=Jump;12.22:tap=Attack;12.6:dir=None;'\
'13.0:tc=TC-03-04;13.0:spawn=TestPassageWest;13.3:dir=Right;14.0:dir=Down;14.2:tap=Jump;14.7:dir=None;'\
'15.0:tc=TC-01-02;15.0:dir=Right;18.5:dir=None;'\
'18.8:tc=TC-01-04;18.8:dir=Up;19.0:dir=None;19.5:dir=Down;19.7:dir=None;20.0:dir=Left;20.6:dir=None;20.8:dir=Up;21.0:dir=None;'\
'21.3:tc=TC-01-03;21.3:dir=Right;21.35:dir=None;21.4:dir=Right;25.6:dir=None;26.0:dir=Left;27.6:dir=None;'\
'28.0:mark=ScriptEnd;28.1:flush'

unity command editor set_auto_tick $C >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C >/dev/null 2>&1
unity command editor.playmode start $C >/dev/null 2>&1
for k in $(seq 1 60); do
  st=$(unity command script eval $C -- --code 'return UnityEngine.Application.isPlaying && Corum2.Unity.GameHost.Instance != null && Corum2.Unity.GameHost.Instance.Ready ? "PLAYING" : "WAIT";' 2>/dev/null | tail -1)
  case "$st" in *PLAYING*) echo "playing after $k polls"; break;; esac
  sleep 1
done
unity command job wait $C -- --condition '{"member":"UnityEngine.Time.frameCount","op":"changed"}' --timeout 15 2>&1 | tail -1 | cut -c1-200
unity command script eval $C -- --code 'return Corum2.Unity.GameHost.Instance != null && Corum2.Unity.GameHost.Instance.Ready ? Corum2.Unity.GameHost.Instance.RunId : "NOT READY: " + (Corum2.Unity.GameHost.Instance == null ? "no host" : string.Join("|", Corum2.Unity.GameHost.Instance.ValidationErrors));' 2>&1 | tail -1 | cut -c1-200

unity command script eval $C -- --code 'UnityEngine.Camera.main.aspect = 1.6f; return "aspect 1.6 (capture only; Game view " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height + ")";' 2>&1 | tail -1 | cut -c1-200
T0=$(date +%s%3N)
unity command script eval $C -- --code "Corum2.Unity.GameHost.Instance.RunInputScript(\"$SCRIPT\"); return \"started\";" 2>&1 | tail -1 | cut -c1-80
i=0
while :; do
  now=$(date +%s%3N); el=$((now - T0))
  [ $el -gt 29500 ] && break
  f=$(printf "%s/%03d_t%05d.png" "$SHOTS_REL" $i $el)
  unity command capture game_view $C --source camera --width 1280 --height 800 --output-path "D:/ProjectR/Corum2Remake/$f" >/dev/null 2>&1
  i=$((i+1))
done
echo "captures: $i"
unity command console get $C -- --tail 30 --level warn 2>&1 | tail -1 | cut -c1-1500
unity command script eval $C -- --code 'var h = Corum2.Unity.GameHost.Instance; return h.RunId + " tick=" + h.Sim.Tick + " map=" + h.Sim.CurrentMap.Id + " transitions=" + h.Sim.TransitionCount + " log=" + h.LogPath;' 2>&1 | tail -1 | cut -c1-400
unity command editor.playmode stop $C >/dev/null 2>&1
unity status --until-ready --timeout 300 --no-banner >/dev/null 2>&1
unity command editor.playmode set_run_in_background $C -- --enable false >/dev/null 2>&1
echo done
