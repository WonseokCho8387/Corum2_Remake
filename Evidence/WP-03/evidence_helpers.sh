#!/usr/bin/env bash
# Shared helpers for driving the Corum2Remake Editor from bash.
export UNITY_PROJECT_PATH="D:\\ProjectR\\Corum2Remake"
C="--caller plugin --skill unity-cli --no-banner"

# ev '<C# code>' -> prints the JSON "result" value (or error) on one line
ev() { local out; out=$(unity command script eval $C --result-only -- --code "$1" 2>&1 | tr -d '
'); local r; r=$(echo "$out" | grep -o '"result": *"[^"]*"\|"result": *[^,}]*' | head -1); if [ -n "$r" ]; then echo "$r"; else echo "ERR: $(echo "$out" | cut -c1-200)"; fi; }

wait_editor() { # until script eval answers and nothing compiles
  for k in $(seq 1 150); do
    st=$(ev 'return UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating ? "BUSY" : "IDLE";')
    case "$st" in *IDLE*) return 0;; esac
    sleep 2
  done
  echo "editor not ready"; return 1
}

wait_playing() {
  for k in $(seq 1 90); do
    st=$(ev 'return UnityEngine.Application.isPlaying && Corum2.Unity.GameHost.Instance != null && Corum2.Unity.GameHost.Instance.Ready ? "PLAYING" : "WAIT";')
    case "$st" in *PLAYING*) return 0;; esac
    sleep 1
  done
  echo "not playing"; return 1
}

wait_edit() {
  for k in $(seq 1 90); do
    st=$(ev 'return UnityEngine.Application.isPlaying ? "PLAY" : "EDIT";')
    case "$st" in *EDIT*) return 0;; esac
    sleep 1
  done
}

wait_script_done() {
  for k in $(seq 1 600); do
    st=$(ev 'return Corum2.Unity.GameHost.Instance.ScriptRunning ? "RUNNING" : "DONE";')
    case "$st" in *DONE*) return 0;; esac
    sleep 1
  done
}

INPUT_FOCUS_ON='var s = UnityEngine.InputSystem.InputSystem.settings; s.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus; s.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView; return "inputFocusIgnored";'
INPUT_FOCUS_OFF='var s = UnityEngine.InputSystem.InputSystem.settings; s.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices; s.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.PointersAndKeyboardsRespectGameViewFocus; return "inputFocusDefault";'
