#!/usr/bin/env bash
# Runs the Unity test runner for this project in batchmode and prints pass/fail counts.
#
# usage: tools/unity_test.sh <EditMode|PlayMode> [extra Unity args...]
#   Unity writes the result XML to the git-ignored unity/Downbeat/Logs/test-results/<mode>.xml (mode in lower case) and the editor log to
#   unity/Downbeat/Logs/unity_test_<mode>.log. Only a passing run (at least one test ran, none failed) copies the XML to the tracked
#   measurements/unit-tests/<mode>.xml, so a failing or half-finished run never overwrites the last good evidence.
#   exit code 0 only when the XML exists, at least one test ran and none failed.
#
# Notes
#   - Uses only the batchmode CLI of the editor named below, on this project path. It never talks to a running editor.
#   - Does NOT pass -nographics: Day 0 found that the first import of this URP project crashes the editor with it.
#   - Environment: UNITY_EDITOR overrides the editor binary, UNITY_BUILD_TARGET the active build target (default StandaloneOSX).
set -euo pipefail

MODE="${1:-}"
case "$MODE" in
  EditMode|PlayMode) shift ;;
  *) echo "usage: $0 <EditMode|PlayMode> [extra Unity args...]" >&2; exit 64 ;;
esac

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity}"
TARGET="${UNITY_BUILD_TARGET:-StandaloneOSX}"
PROJECT="$ROOT/unity/Downbeat"
MODE_LOWER="$(printf '%s' "$MODE" | tr '[:upper:]' '[:lower:]')"
RESULTS="$PROJECT/Logs/test-results/$MODE_LOWER.xml"
EVIDENCE="$ROOT/measurements/unit-tests/$MODE_LOWER.xml"
LOG="$PROJECT/Logs/unity_test_$MODE_LOWER.log"

[ -x "$UNITY" ] || { echo "Unity editor not found: $UNITY" >&2; exit 66; }
mkdir -p "$(dirname "$RESULTS")" "$(dirname "$LOG")" "$(dirname "$EVIDENCE")"
rm -f "$RESULTS"

echo "[unity_test] $MODE on $PROJECT (target $TARGET)"
set +e
"$UNITY" -batchmode -projectPath "$PROJECT" -buildTarget "$TARGET" \
  -runTests -testPlatform "$MODE" -testResults "$RESULTS" -logFile "$LOG" "$@"
UNITY_EXIT=$?
set -e

if [ ! -s "$RESULTS" ]; then
  echo "[unity_test] no result file (Unity exit $UNITY_EXIT). Last log lines:" >&2
  tail -n 25 "$LOG" >&2 || true
  exit 1
fi

python3 -I - "$RESULTS" "$MODE" "$UNITY_EXIT" "$EVIDENCE" <<'PY'
import shutil
import sys
import xml.etree.ElementTree as ET

path, mode, unity_exit, evidence = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
root = ET.parse(path).getroot()
total = int(root.get("total", "0"))
passed = int(root.get("passed", "0"))
failed = int(root.get("failed", "0"))
skipped = int(root.get("skipped", "0")) + int(root.get("inconclusive", "0"))
print(f"[unity_test] {mode}: total={total} passed={passed} failed={failed} skipped_or_inconclusive={skipped} (unity exit {unity_exit})")
for case in root.iter("test-case"):
    if case.get("result") == "Failed":
        message = case.findtext("failure/message") or ""
        print(f"  FAILED {case.get('fullname')}: {message.strip().splitlines()[0] if message.strip() else ''}")
passed_run = total > 0 and failed == 0
if passed_run:
    shutil.copyfile(path, evidence)
    print(f"[unity_test] evidence copied to {evidence}")
else:
    print(f"[unity_test] NOT copied to the tracked evidence file (the run did not pass): {path}")
sys.exit(0 if passed_run else 1)
PY
