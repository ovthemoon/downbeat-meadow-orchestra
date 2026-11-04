#!/usr/bin/env bash
# Native beat-clock measurement (Task 2): runs the Mac player in --measure-clock mode and analyzes it with tools/analyze_s1.mjs.
#
# usage: tools/run_native_clock.sh [--no-build] [--windowed] [--repeats 3] [--duration 60]
#   --windowed: a 640x360 window with vSync off (folders W1..Wn) instead of -batchmode (folders R1..Rn).
#   For each repeat R1..Rn: a "calm" run and a "stress" run (main thread blocked for 50 ms every 2 s), each DURATION seconds.
#   Output: measurements/native-clock/R<n>/ (<path>__<scenario>.meta.json, raw-audio/*.f32 [git-ignored], player logs, analysis.json,
#   summary.csv, run-summary.json) and measurements/native-clock/summary.md (all runs, pass criteria, conditions).
#
# Conditions worth knowing: the player runs with -batchmode (no window) unless --windowed, vSync off, targetFrameRate 60, no camera, the click is silenced
# after it is recorded. The system load (the Mac is shared with other Unity editors) is recorded before and after every run.
set -euo pipefail

BUILD=1; REPEATS=3; DURATION=60; WINDOWED=0
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) BUILD=0; shift ;;
    --windowed) WINDOWED=1; shift ;;
    --repeats)  REPEATS="$2"; shift 2 ;;
    --duration) DURATION="$2"; shift 2 ;;
    *) echo "usage: $0 [--no-build] [--windowed] [--repeats N] [--duration SEC]" >&2; exit 64 ;;
  esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLAYER="$ROOT/builds/mac/Downbeat.app/Contents/MacOS/Downbeat"
OUT_ROOT="$ROOT/measurements/native-clock"
if [ "$WINDOWED" -eq 1 ]; then PREFIX=W; DISPLAY_FLAGS="-screen-fullscreen 0 -screen-width 640 -screen-height 360"; else PREFIX=R; DISPLAY_FLAGS="-batchmode"; fi
GRACE=45   # seconds a run may take beyond its duration (content load, start delay, tail) before it is killed

[ "$BUILD" -eq 1 ] && BUILD_LABEL=task2 "$ROOT/tools/unity_build.sh" mac
[ -x "$PLAYER" ] || { echo "no Mac player at $PLAYER (run tools/unity_build.sh mac)" >&2; exit 66; }
mkdir -p "$OUT_ROOT"

load_avg() { sysctl -n vm.loadavg | tr -d '{}' | xargs; }
unity_editors() { pgrep -fl "Unity.app/Contents/MacOS/Unity " 2>/dev/null | grep -v AssetImportWorker | grep -c . || true; }

run_one() { # run_one DIR SCENARIO
  local dir="$1" scenario="$2" log="$1/player-$2.log" pid waited=0
  mkdir -p "$dir"
  echo "[native_clock] $(basename "$dir") $scenario: load before $(load_avg)"
  "$PLAYER" $DISPLAY_FLAGS --measure-clock --out "$dir" --scenario "$scenario" --duration "$DURATION" -logFile "$log" >/dev/null 2>&1 &
  pid=$!
  while kill -0 "$pid" 2>/dev/null; do
    sleep 1; waited=$((waited + 1))
    if [ "$waited" -gt $((DURATION + GRACE)) ]; then echo "[native_clock] timeout, killing $pid" >&2; kill "$pid"; break; fi
  done
  local code=0; wait "$pid" 2>/dev/null || code=$?
  echo "[native_clock] $(basename "$dir") $scenario: exit $code after ${waited}s, load after $(load_avg)"
  printf '{"scenario":"%s","exitCode":%s,"wallSeconds":%s,"loadAvgAfter":"%s"}\n' "$scenario" "$code" "$waited" "$(load_avg)" > "$dir/exit-$scenario.json"
  return "$code"
}

for n in $(seq 1 "$REPEATS"); do
  dir="$OUT_ROOT/$PREFIX$n"; rm -rf "$dir"; mkdir -p "$dir"
  printf '{"run":"%s%s","startedUtc":"%s","loadAvgAtStart":"%s","otherUnityEditorProcesses":%s,"macos":"%s","model":"%s","durationSec":%s}\n' \
    "$PREFIX" "$n" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(load_avg)" "$(unity_editors)" "$(sw_vers -productVersion)" "$(sysctl -n machdep.cpu.brand_string)" "$DURATION" > "$dir/run-summary.json"
  for scenario in calm stress; do run_one "$dir" "$scenario"; done
  node "$ROOT/tools/analyze_s1.mjs" "$dir" > "$dir/analyze.txt"
done

node "$ROOT/tools/summarize_native_clock.mjs" "$OUT_ROOT"/[RW]*
echo "[native_clock] summary: $OUT_ROOT/summary.md"
