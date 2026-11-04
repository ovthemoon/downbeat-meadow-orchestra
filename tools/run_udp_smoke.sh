#!/usr/bin/env bash
# UDP hand input smoke run (Task 4): the Python tracker plays a looping video (never a camera) into the Mac player over UDP.
#
# usage: tools/run_udp_smoke.sh [--no-build] [--seconds 40]
#   A  --input hand  with the tracker running   (tracking on)
#   B  --input hand  without the tracker        (the UDP source times out, the input falls back to the mouse)
#   C  --input mouse                            (no UDP socket at all)
#   A2, A3  repeats of A (to see whether a one-off frame hitch repeats)
#   Each: 960x540 window, vSync as in the quality settings, --mute (notes are scheduled and counted but silent), frame stats every 5 s.
#   Output: measurements/day3/udp-smoke/<A|B|C>/{player.log,tracker.log,hand-cursor.png} and measurements/day3/udp-smoke.md
set -euo pipefail

BUILD=1; SECONDS_RUN=40
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) BUILD=0; shift ;;
    --seconds)  SECONDS_RUN="$2"; shift 2 ;;
    *) echo "usage: $0 [--no-build] [--seconds N]" >&2; exit 64 ;;
  esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLAYER="$ROOT/builds/mac/Downbeat.app/Contents/MacOS/Downbeat"
PYTHON="$ROOT/tracker/.venv/bin/python"
VIDEO="$ROOT/tests/fixtures/video/hand-strikes.mp4"
OUT="$ROOT/measurements/day3/udp-smoke"
QUIT_AFTER=$((SECONDS_RUN + 5))   # content loading comes first; stats start when the audio clock runs

[ "$BUILD" -eq 1 ] && BUILD_LABEL=task4 "$ROOT/tools/unity_build.sh" mac
[ -x "$PLAYER" ] || { echo "no Mac player (tools/unity_build.sh mac)" >&2; exit 66; }
rm -rf "$OUT"; mkdir -p "$OUT"

run_player() { # run_player DIR INPUT
  local dir="$1" input="$2" pid
  "$PLAYER" -screen-fullscreen 0 -screen-width 960 -screen-height 540 --input "$input" --mute --log-frame-stats 5 \
    --capture "$dir/hand-cursor.png" --capture-after $((SECONDS_RUN - 10)) --quit-after "$QUIT_AFTER" -logFile "$dir/player.log" >/dev/null 2>&1 &
  pid=$!
  for _ in $(seq 1 $((QUIT_AFTER + 60))); do sleep 1; kill -0 "$pid" 2>/dev/null || break; done
  if kill -0 "$pid" 2>/dev/null; then echo "[udp_smoke] player did not quit, killing" >&2; kill "$pid"; fi
  wait "$pid" 2>/dev/null || true
}

load() { sysctl -n vm.loadavg | tr -d '{}' | xargs; }

for scenario in A B C A2 A3; do
  dir="$OUT/$scenario"; mkdir -p "$dir"
  echo "[udp_smoke] $scenario: load before $(load)"
  tracker_pid=""
  if [ "${scenario:0:1}" = "A" ]; then
    DOWNBEAT_NO_CAMERA=1 "$PYTHON" -m downbeat_tracker --video "$VIDEO" --loop --port 47800 --max-fps 30 --max-seconds $((QUIT_AFTER + 30)) --log "$dir/tracker.log" >/dev/null 2>&1 &
    tracker_pid=$!; sleep 2
  fi
  case "$scenario" in A*|B) run_player "$dir" hand ;; C) run_player "$dir" mouse ;; esac
  [ -n "$tracker_pid" ] && { kill "$tracker_pid" 2>/dev/null || true; wait "$tracker_pid" 2>/dev/null || true; }
  echo "$(load)" > "$dir/load-after.txt"
  echo "[udp_smoke] $scenario: done, load after $(cat "$dir/load-after.txt")"
done

"$PYTHON" "$ROOT/tools/summarize_udp_smoke.py" "$OUT"
