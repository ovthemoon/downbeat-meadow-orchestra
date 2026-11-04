#!/usr/bin/env bash
# Builds the zip that goes to the Windows PC.
#
#   tools/package_windows.sh [REPO_ROOT]                 builds/downbeat-windows-<date>.zip       player + tracker + supervisor + scripts + READMEs (hand setup, run-order sheet, latency method)
#   tools/package_windows.sh --tracker-only [REPO_ROOT]  builds/downbeat-hand-tracker-<date>.zip  tracker + supervisor + scripts + READMEs (no player, under 25 MB)
#
# REPO_ROOT defaults to the repository this script lives in; pass another checkout (for example the main one after a merge) to package that one.
# An existing zip of the same name is never overwritten: the new one gets a -HHMMSS suffix.
set -euo pipefail

MAX_TRACKER_ONLY_BYTES=$((25 * 1024 * 1024))
BOM_HEX="efbbbf"

usage() {
  sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
}

die() {
  echo "package_windows: $*" >&2
  exit 1
}

tracker_only=0
repo=""
for arg in "$@"; do
  case "$arg" in
    --tracker-only) tracker_only=1 ;;
    -h | --help) usage; exit 0 ;;
    -*) usage >&2; exit 2 ;;
    *) repo="$arg" ;;
  esac
done
if [ -z "$repo" ]; then
  repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fi
repo="$(cd "$repo" && pwd)" || die "repository root not found: $repo"

for needed in tracker/pyproject.toml tracker/requirements.lock.txt tracker/downbeat_tracker/__main__.py tests/fixtures/video/hand-strikes.mp4 \
  tools/windows/_common.ps1 tools/windows/setup-hand-tracker.ps1 tools/windows/play-with-hands.ps1 tools/windows/remove-hand-tracker.ps1 docs/windows-hand-setup.md \
  tools/windows/_supervisor-common.ps1 tools/windows/run-supervised.ps1 tools/windows/stop-supervised.ps1 tools/windows/install-autostart.ps1 tools/windows/uninstall-autostart.ps1 tools/windows/collect-results.ps1 \
  supervisor/downbeat_supervisor/__main__.py supervisor/configs/windows.json docs/windows-runbook.md; do
  [ -f "$repo/$needed" ] || die "missing $needed in $repo"
done

has_bom() {
  [ "$(head -c 3 "$1" | od -An -tx1 | tr -d ' \n')" = "$BOM_HEX" ]
}

# The scripts must be UTF-8 with BOM, or Windows PowerShell 5.1 garbles the Korean text.
for script in "$repo"/tools/windows/*.ps1; do
  has_bom "$script" || die "$script has no UTF-8 BOM"
done

# Windows text files: CRLF line ends. A .ps1 keeps the BOM it has; a README .txt gets one.
to_crlf() { # <source> <target>
  perl -pe 's/\r?\n\z/\r\n/' "$1" > "$2"
}
to_txt() { # <source> <target>
  { printf '\357\273\277'; perl -pe 's/^\xEF\xBB\xBF//; s/\r?\n\z/\r\n/' "$1"; } > "$2"
}

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

if [ "$tracker_only" -eq 1 ]; then
  top="downbeat-hand-tracker"
  zip_base="downbeat-hand-tracker"
else
  top="downbeat-windows"
  zip_base="downbeat-windows"
fi
bundle="$stage/$top"
mkdir -p "$bundle/tracker/selfcheck" "$bundle/scripts"

# The player (full bundle only). The burst debug symbols are not needed to run it.
if [ "$tracker_only" -eq 0 ]; then
  [ -f "$repo/builds/windows/Downbeat.exe" ] || die "no player at $repo/builds/windows/Downbeat.exe (build it first)"
  rsync -a --exclude 'Downbeat_BurstDebugInformation_DoNotShip' --exclude '.DS_Store' "$repo/builds/windows/" "$bundle/"
fi

# The tracker: source, project file, lock and the video the self-check needs. No venv, egg-info, caches or tests.
rsync -a --exclude '__pycache__' --exclude '*.pyc' --exclude '.DS_Store' "$repo/tracker/downbeat_tracker" "$bundle/tracker/"
cp "$repo/tracker/pyproject.toml" "$repo/tracker/requirements.lock.txt" "$repo/tracker/README.md" "$bundle/tracker/"
cp "$repo/tests/fixtures/video/hand-strikes.mp4" "$bundle/tracker/selfcheck/"

for script in "$repo"/tools/windows/*.ps1; do
  to_crlf "$script" "$bundle/scripts/$(basename "$script")"
done

# The watcher (supervisor): plain Python, no packages to install; it runs with the tracker's Python. Its Windows configuration is a JSON file (LF is fine).
mkdir -p "$bundle/supervisor/configs"
rsync -a --exclude '__pycache__' --exclude '*.pyc' --exclude '.DS_Store' "$repo/supervisor/downbeat_supervisor" "$bundle/supervisor/"
cp "$repo/supervisor/configs/windows.json" "$bundle/supervisor/configs/"

to_txt "$repo/docs/windows-hand-setup.md" "$bundle/README-hand-setup.txt"
to_txt "$repo/docs/windows-runbook.md" "$bundle/README-runbook.txt"
to_txt "$repo/docs/latency-method.md" "$bundle/README-latency.txt"
if [ "$tracker_only" -eq 0 ]; then
  check_doc="$(ls "$repo"/docs/windows-check-*.md 2>/dev/null | sort | tail -n 1 || true)"
  if [ -n "$check_doc" ]; then
    to_txt "$check_doc" "$bundle/README-windows-check.txt"
  else
    echo "package_windows: warning: no docs/windows-check-*.md found, skipped" >&2
  fi
fi

mkdir -p "$repo/builds"
today="$(date +%Y-%m-%d)"
out="$repo/builds/${zip_base}-${today}.zip"
if [ -e "$out" ]; then
  out="$repo/builds/${zip_base}-${today}-$(date +%H%M%S).zip"
  echo "package_windows: the dated zip already exists, writing $(basename "$out") instead"
fi
(cd "$stage" && zip -r -X -q "$out" "$top")

# Checks on the finished zip.
listing="$(unzip -Z1 "$out")"
if grep -E '(^|/)(\.venv|__pycache__|\.pytest_cache|\.DS_Store|Downbeat_BurstDebugInformation_DoNotShip)(/|$)|\.egg-info(/|$)|\.pyc$' <<< "$listing" > /dev/null; then
  die "the zip contains files that must not ship (venv, caches, debug symbols)"
fi
grep -qx "$top/scripts/play-with-hands.ps1" <<< "$listing" || die "play-with-hands.ps1 is missing from the zip"
for needed_in_zip in scripts/run-supervised.ps1 scripts/install-autostart.ps1 scripts/uninstall-autostart.ps1 scripts/collect-results.ps1 scripts/stop-supervised.ps1 scripts/_supervisor-common.ps1 \
  supervisor/downbeat_supervisor/__main__.py supervisor/configs/windows.json README-runbook.txt README-latency.txt tracker/downbeat_tracker/heartbeat.py; do
  grep -qx "$top/$needed_in_zip" <<< "$listing" || die "$needed_in_zip is missing from the zip"
done
grep -qx "$top/tracker/selfcheck/hand-strikes.mp4" <<< "$listing" || die "the self-check video is missing from the zip"
if [ "$tracker_only" -eq 0 ]; then
  grep -qx "$top/Downbeat.exe" <<< "$listing" || die "Downbeat.exe is missing from the zip"
  # the content: the folder next to the program (what the operator edits) and the built-in copy the program starts with when that folder is missing or broken
  grep -qx "$top/content/content.json" <<< "$listing" || die "content/content.json is missing from the zip"
  grep -qx "$top/Downbeat_Data/StreamingAssets/default-content/content.json" <<< "$listing" || die "the built-in content (Downbeat_Data/StreamingAssets/default-content) is missing from the zip: rebuild the Windows player"
  # the pictures of the hands of the tutorial, in both copies (without them the tutorial draws its hands in code)
  for picture in tutorial/hand-open.png tutorial/hand-fist.png; do
    grep -qx "$top/content/$picture" <<< "$listing" || die "content/$picture is missing from the zip"
    grep -qx "$top/Downbeat_Data/StreamingAssets/default-content/$picture" <<< "$listing" || die "the built-in copy of $picture is missing from the zip: rebuild the Windows player"
  done
fi

bytes="$(stat -f%z "$out")"
echo "package_windows: wrote $out"
echo "package_windows: $(grep -vc '/$' <<< "$listing") files, $(du -h "$out" | cut -f1) ($bytes bytes)"
if [ "$tracker_only" -eq 1 ] && [ "$bytes" -ge "$MAX_TRACKER_ONLY_BYTES" ]; then
  die "the tracker-only zip is $bytes bytes, over the $MAX_TRACKER_ONLY_BYTES byte limit"
fi
