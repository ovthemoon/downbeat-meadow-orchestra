#!/usr/bin/env bash
# Starts the Downbeat demo on this Mac: the hand tracker and the player together, watched by the supervisor (restarts what stops, restarts the player when it freezes).
# Double-click it in Finder, or run it in a terminal.
#
#   ops/mac/run-demo.command                 hands when a camera is connected, otherwise the recorded hands on a loop
#   DOWNBEAT_INPUT=hand|replay|mouse         ask for one explicitly
#   ops/mac/run-demo.command --dry-run       print what would be started and exit (opens no camera)
#
# One screen is the Mac default: the record screen is a strip over the lower part of the meadow, Tab opens the settings panel, whose strip switch hides it (display.mode auto = single on a Mac).
# The camera is only opened by the hand tracker, and only when hands are chosen; a camera that is connected is found with system_profiler, which does not open it.
# Needs builds/mac/Downbeat.app (tools/unity_build.sh mac) and, for hands, tracker/.venv (see tracker/README.md).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="$ROOT/builds/mac"
PYTHON="$ROOT/tracker/.venv/bin/python"
DRY=0
[ "${1:-}" = "--dry-run" ] && DRY=1

[ -x "$APP/Downbeat.app/Contents/MacOS/Downbeat" ] || { echo "run-demo: no player at $APP (run tools/unity_build.sh mac first)" >&2; exit 66; }
[ -x "$PYTHON" ] || { echo "run-demo: no Python at $PYTHON (the supervisor runs with the tracker's virtual environment; see tracker/README.md)" >&2; exit 66; }

has_camera() {
  # Lists the cameras without opening one.
  /usr/sbin/system_profiler SPCameraDataType 2> /dev/null | grep -q "Model ID"
}

INPUT="${DOWNBEAT_INPUT:-}"
if [ -z "$INPUT" ]; then
  if has_camera && [ -x "$PYTHON" ] && "$PYTHON" -c "import downbeat_tracker" 2> /dev/null; then
    INPUT="hand"
  else
    INPUT="replay"
    echo "run-demo: no camera found (or no tracker installed): the recorded hands play (--input replay)"
  fi
fi
case "$INPUT" in
  hand | replay | mouse) ;;
  *) echo "run-demo: DOWNBEAT_INPUT must be hand, replay or mouse, not '$INPUT'" >&2; exit 64 ;;
esac

ARGS=(--config "$ROOT/supervisor/configs/mac.json" --root "$APP" --var "input=$INPUT")
[ "$DRY" -eq 1 ] && ARGS+=(--dry-run)
echo "run-demo: input $INPUT; the record is in $APP/logs (supervisor-yyyyMMdd.csv). Ctrl+C ends everything."
cd "$ROOT"
PYTHONPATH="$ROOT/supervisor" exec "$PYTHON" -m downbeat_supervisor "${ARGS[@]}"
