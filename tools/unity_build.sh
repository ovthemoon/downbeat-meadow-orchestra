#!/usr/bin/env bash
# Builds a native player of the Unity project in batchmode and records its size.
#
# usage: tools/unity_build.sh <mac|windows>
#   output   builds/mac/Downbeat.app   or   builds/windows/Downbeat.exe (+ Downbeat_Data, content/ next to it)
#   size     measurements/build/<label>.json, key "mac" or "windows" (label: $BUILD_LABEL, default "latest")
#   exit 0 only when Unity reports a successful build and the player exists.
#
# Notes
#   - Only the batchmode CLI of the editor named below runs, on this project path. It never talks to a running editor.
#   - Does NOT pass -nographics (Day 0: the first import of this URP project crashes the editor with it).
#   - The Windows build needs the "Windows Build Support (Mono)" module of the editor (see measurements/env/unity-modules.txt).
#   - Environment: UNITY_EDITOR overrides the editor binary, BUILD_LABEL the size file name.
set -euo pipefail

TARGET="${1:-}"
case "$TARGET" in
  mac)     UNITY_TARGET=StandaloneOSX;       METHOD=BuildMac;     PLAYER=Downbeat.app ;;
  windows) UNITY_TARGET=StandaloneWindows64; METHOD=BuildWindows; PLAYER=Downbeat.exe ;;
  *) echo "usage: $0 <mac|windows>" >&2; exit 64 ;;
esac

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="$ROOT/unity/Downbeat"
OUT="$ROOT/builds/$TARGET"
LOG="$PROJECT/Logs/unity_build_$TARGET.log"
LABEL="${BUILD_LABEL:-latest}"
SIZE_FILE="$ROOT/measurements/build/$LABEL.json"

[ -x "$UNITY" ] || { echo "Unity editor not found: $UNITY" >&2; exit 66; }
mkdir -p "$(dirname "$LOG")" "$(dirname "$SIZE_FILE")"
rm -rf "$OUT" "$OUT.build-report.json"

echo "[unity_build] $TARGET -> $OUT"
set +e
"$UNITY" -batchmode -quit -projectPath "$PROJECT" -buildTarget "$UNITY_TARGET" \
  -executeMethod "Downbeat.EditorTools.NativeBuild.$METHOD" -logFile "$LOG"
UNITY_EXIT=$?
set -e

if [ "$UNITY_EXIT" -ne 0 ] || [ ! -e "$OUT/$PLAYER" ]; then
  echo "[unity_build] FAILED (Unity exit $UNITY_EXIT). Errors from the log:" >&2
  grep -E "error CS|\[NativeBuild\]|Error building|BuildFailedException|Exception" "$LOG" | head -20 >&2 || true
  exit 1
fi

python3 -I - "$OUT" "$OUT.build-report.json" "$SIZE_FILE" "$TARGET" "$PLAYER" <<'PY'
import json, os, sys

out, report_path, size_file, target, player = sys.argv[1:6]

def tree_size(path):
    total, count = 0, 0
    if os.path.isfile(path):
        return os.path.getsize(path), 1
    for base, _dirs, names in os.walk(path):
        for name in names:
            full = os.path.join(base, name)
            if os.path.islink(full):
                continue
            total += os.path.getsize(full)
            count += 1
    return total, count

player_bytes, player_files = tree_size(os.path.join(out, player))
total_bytes, total_files = tree_size(out)
content_bytes, content_files = tree_size(os.path.join(out, "content"))
data_dir = os.path.join(out, "Downbeat_Data")  # Windows only; a Mac player keeps its data inside the .app
data_bytes = tree_size(data_dir)[0] if os.path.isdir(data_dir) else None
report = json.load(open(report_path)) if os.path.exists(report_path) else {}

entry = {
    "player": player,
    "playerBytes": player_bytes,
    "playerFiles": player_files,
    **({"dataFolderBytes": data_bytes} if data_bytes is not None else {}),
    "contentFolderBytes": content_bytes,
    "contentFolderFiles": content_files,
    "totalBytes": total_bytes,
    "totalFiles": total_files,
    "unity": report.get("unity"),
    "scriptingBackend": report.get("scriptingBackend"),
    "colorSpace": report.get("colorSpace"),
    "buildSeconds": report.get("buildSeconds"),
    "note": "bytes are the sum of file sizes on disk (not compressed); buildSeconds is Unity's build time and is much shorter when its caches are warm (clean first builds took about 44 s Mac and 46 s Windows)",
}
existing = json.load(open(size_file)) if os.path.exists(size_file) else {}
existing[target] = entry
json.dump(existing, open(size_file, "w"), indent=2, sort_keys=True)
open(size_file, "a").write("\n")
mib = lambda b: round(b / 1048576, 2)
print(f"[unity_build] {target}: player {mib(player_bytes)} MiB ({player_files} files), total {mib(total_bytes)} MiB ({total_files} files) incl. content {mib(content_bytes)} MiB; built in {entry['buildSeconds'] and round(entry['buildSeconds'], 1)} s")
PY
