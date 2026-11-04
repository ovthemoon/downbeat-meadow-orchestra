#!/usr/bin/env bash
# Measures how fast the Mac player draws the meadow in a 1920x1080 window, per quality tier, and merges the result into captures/perf/grass-mac.json (measurements/perf/ with DOWNBEAT_KEEP_CAPTURES=1)
# (source "player-windowed"). Opens a real window for about SECONDS+6 s per tier, so keep it short. Needs builds/mac/Downbeat.app (tools/unity_build.sh mac).
#
# usage: tools/run_grass_perf.sh [SECONDS=15] [TIER ...]      default tiers: High Low
#   PERF_FILE   result file name in measurements/perf (default grass-mac.json); PERF_ARGS extra player arguments;
#   PERF_NAME   log prefix (default grass-mac). Particles: PERF_FILE=particles-mac.json PERF_NAME=particles-mac PERF_ARGS="--input replay --loop --fireflies 1" tools/run_grass_perf.sh
#   vSync and the frame cap are off (--uncapped): the fps is what the machine can do, not the display's refresh rate.
#   The window is requested at 1920x1080; macOS may make it a little shorter than the screen (title bar and menu bar), the real size is in the result.
set -euo pipefail

SECONDS_TO_RUN="${1:-15}"
shift || true
TIERS=("$@")
[ ${#TIERS[@]} -gt 0 ] || TIERS=(High Low)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/builds/mac/Downbeat.app/Contents/MacOS/Downbeat"
# Like the PlayMode tests: results go to the git-ignored captures/perf; DOWNBEAT_KEEP_CAPTURES=1 writes them to the tracked measurements/perf (the numbers kept as evidence).
if [ "${DOWNBEAT_KEEP_CAPTURES:-}" = "1" ]; then OUT="$ROOT/measurements/perf"; else OUT="$ROOT/captures/perf"; fi
PERF_FILE="${PERF_FILE:-grass-mac.json}"
PERF_NAME="${PERF_NAME:-grass-mac}"
PERF_ARGS="${PERF_ARGS:---input mouse}"
[ -x "$APP" ] || { echo "no player at $APP (run tools/unity_build.sh mac)" >&2; exit 66; }
mkdir -p "$OUT/player-logs"

for TIER in "${TIERS[@]}"; do
  LOG="$OUT/player-logs/$PERF_NAME-player-$TIER.log"
  QUIT=$((SECONDS_TO_RUN + 6))   # about 4 s of loading and warm-up come before the counted frames
  echo "[grass_perf] $TIER: window 1920x1080, ${QUIT}s"
  "$APP" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 $PERF_ARGS --mute --uncapped --quality "$TIER" \
    --log-frame-stats "$SECONDS_TO_RUN" --quit-after "$QUIT" -logFile "$LOG" >/dev/null 2>&1 || true
  sed -i '' "s#$HOME#~#g" "$LOG"   # no home-directory paths in committed logs
  python3 -I - "$LOG" "$OUT/$PERF_FILE" "$TIER" "$PERF_ARGS" <<'PY'
import json, re, sys, platform, subprocess

log_path, json_path, tier, extra = sys.argv[1:5]
text = open(log_path, encoding="utf-8", errors="replace").read()
total = re.search(r"\[frame-stats\] TOTAL seconds=([\d.]+) frames=(\d+) fps=([\d.]+) p50=([\d.]+) p95=([\d.]+) p99=([\d.]+) max=([\d.]+) over25ms=(\d+)", text)
screen = re.search(r"\[RunAutomation\] screen (\d+)x(\d+) fullscreen=(\w+)", text)
blades = re.search(r"quality (\w+): [\d.]+ blades per m2, (\d+) blades", text)
if not total or not screen:
    sys.exit(f"no TOTAL frame-stats line in {log_path}: the player did not finish")
seconds, frames, fps, p50, p95, p99, mx, over25 = total.groups()
run = {
    "source": "player-windowed", "tier": tier, "width": int(screen.group(1)), "height": int(screen.group(2)),
    "seconds": float(seconds), "frames": int(frames), "fpsAverage": float(fps),
    "frameMsP50": float(p50), "frameMsP95": float(p95), "frameMsP99": float(p99), "frameMsMax": float(mx), "framesOver25ms": int(over25),
    "grassBlades": int(blades.group(2)) if blades else -1,
    "note": f"Mac player (Mono, Release), window {screen.group(1)}x{screen.group(2)} (requested 1920x1080), fullscreen={screen.group(3)}, vSync and frame cap off, grass tier {tier}, arguments: {extra}; log: measurements/perf/player-logs/",
}
try:
    data = json.load(open(json_path))
except FileNotFoundError:
    data = {"runs": []}
data["runs"] = [r for r in data.get("runs", []) if not (r.get("source") == run["source"] and r.get("tier") == tier)] + [run]
json.dump(data, open(json_path, "w"), indent=4)
open(json_path, "a").write("\n")
print(f"[grass_perf] {tier}: {run['fpsAverage']} fps average, frame p95 {run['frameMsP95']} ms, max {run['frameMsMax']} ms, {run['grassBlades']} blades, window {run['width']}x{run['height']}")
PY
done
