#!/usr/bin/env python3
"""WP-03 run checks from the NDJSON log (run from the repository root).

python Evidence/WP-03/tools/run_checks.py Evidence/WP-03/runs/RUN-20261110-122933.ndjson
Writes JSON to stdout:
  slow_motion: expected recorder frames from the logged time scale (one frame = 0.02 s * timeScale of game time
               under Time.captureFramerate = 50) vs the frame count the recorder logged; ticks spent with the ring open
  ring_open_states: player state at every ring open (TC-02-11, TC-05-04)
  closes: ring close reasons (TC-02-09)
  uses: every item use with request/exec tick and wait reason (TC-02-06/10, TC-05-05, CF-07/CF-08 EXP)
  rejects: rejected item requests by reason
  quickslot_assignments, discards, ignored ring inputs, magic requests
"""
import collections
import json
import sys


def main(path):
    ev = [json.loads(l) for l in open(path, encoding="utf-8")]
    start = next(e for e in ev if e["type"] == "marker" and e["reason"] == "VideoRecordingStarted")
    stop = next(e for e in ev if e["type"] == "marker" and e["reason"] == "VideoRecordingStopped")
    changes = [(e["simulationTick"], e["timeScale"]) for e in ev if e["type"] == "time.scale"]

    def scale(t):
        s = 1.0
        for k, v in changes:
            if k < t:
                s = v
        return s

    t0, t1 = start["simulationTick"], stop["simulationTick"]
    frames = sum(1 / scale(t) for t in range(t0 + 1, t1 + 1))
    slow_ticks = sum(1 for t in range(t0 + 1, t1 + 1) if scale(t) < 1)
    stopped = next((e for e in ev if e["type"] == "marker" and e["reason"] == "VideoRecordingStopped"), {})

    route_open = [e for e in ev if e["type"] == "input.route" and e["reason"] == "Open"]
    out = {
        "log": path,
        "slow_motion": {
            "recording_ticks": [t0, t1], "time_scale_changes": len(changes),
            "scales_seen": sorted({v for _, v in changes}),
            "ticks_with_ring_open": slow_ticks, "expected_frames": round(frames, 1),
            "recorder_frames_logged": stopped.get("frames"),
            "note": "Time.captureFramerate=50: each rendered frame advances 0.02 s * timeScale of game time, "
                    "so 5 frames per tick while the ring is open (0.2) and 1 frame per tick otherwise",
        },
        "ring_open_states": [{"tick": e["simulationTick"], "testCase": e["testCaseId"], "playerState": e.get("playerState")}
                             for e in route_open],
        "closes": collections.Counter(e["reason"] for e in ev if e["type"] == "ring.close"),
        "uses": [{"tick": e["simulationTick"], "tc": e["testCaseId"], "item": e["targetId"], "source": e.get("source"),
                  "requestTick": e.get("requestTick"), "execTick": e.get("execTick"), "waitedFor": e.get("waitedFor"),
                  "hp": [e.get("hpBefore"), e.get("hpAfter")], "countAfter": e.get("countAfter"),
                  "quickslotsCleared": e.get("quickslotsCleared")} for e in ev if e["type"] == "item.use"],
        "rejects": collections.Counter(e["reason"] for e in ev if e["type"] == "item.request" and e["reason"].startswith("Rejected")),
        "quickslot_assignments": [{"tick": e["simulationTick"], "slot": e.get("slot"), "item": e["stateAfter"],
                                   "itemConsumed": e.get("itemConsumed"), "stickNeutral": e.get("stickNeutral")}
                                  for e in ev if e["type"] == "quickslot.assign"],
        "discards": [{"tick": e["simulationTick"], "reason": e["reason"], "item": e.get("item"),
                      "count": [e.get("countBefore"), e.get("countAfter")]} for e in ev if e["type"] == "ring.discard"],
        "ignored": collections.Counter(e["reason"] for e in ev if e["type"] == "ring.ignore"),
        "magic_requests": [{"tick": e["simulationTick"], "magicId": e.get("magicId"), "power": e.get("power"),
                            "castTime": e.get("castTime")} for e in ev if e["type"] == "magic.request"],
        "pages": [(e["simulationTick"], e["stateBefore"], e["stateAfter"], e["reason"]) for e in ev if e["type"] == "ring.page"],
        "game_inputs_while_open": sum(1 for e in ev if e["type"] == "input.route" and e.get("consumer") == "game"
                                      and e.get("ringMode") not in ("Closed",) and e["reason"] == "Forward"),
    }
    # TC-02-05: while the ring is open the stick is the cursor; the player must not be moved by it.
    ticks = {e["simulationTick"]: e for e in ev if e["type"] == "tick"}
    closes = [e["simulationTick"] for e in ev if e["type"] == "ring.close"]
    moves = []
    for o in route_open:
        a = o["simulationTick"]
        b = next((c for c in closes if c >= a), None)
        if b is None or a not in ticks or b not in ticks:
            continue
        cursor_moves = sum(1 for e in ev if e["type"] == "ring.cursor" and a <= e["simulationTick"] <= b)
        held = [ticks[t]["heldDir"] for t in range(a + 1, b + 1) if t in ticks]
        moves.append({"open": a, "close": b, "tc": o["testCaseId"], "state": o.get("playerState"),
                      "x_open": ticks[a]["x"], "x_close": ticks[b]["x"], "vx_open": ticks[a]["vx"], "vx_close": ticks[b]["vx"],
                      "cursorChanges": cursor_moves, "gameHeldDirWhileOpen": sorted(set(held))})
    out["player_motion_while_open"] = moves
    json.dump(out, sys.stdout, ensure_ascii=False, indent=2, default=list)
    print()


if __name__ == "__main__":
    main(sys.argv[1])
