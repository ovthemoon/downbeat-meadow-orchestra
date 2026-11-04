#!/usr/bin/env python3
"""usage: python3 tools/measure_boot_time.py [RUNS] [OUT.json]   (needs builds/mac from tools/unity_build.sh mac)

Boot-to-first-heartbeat on the Mac player with a fresh copy of the build (a cold content copy: the app and its content folder are copied anew for every run).

For each run: copy builds/mac to a new folder, start the player with --health-dir there, and poll every 10 ms for
  first_beat  heartbeat.txt exists (the app is alive)
  audio_ok    its second line is audio=ok
  ready       the player log says "[Downbeat] ready"
all in seconds from just before the process was started.
"""
import json
import os
import shutil
import statistics
import subprocess
import sys
import tempfile
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SP = tempfile.mkdtemp(prefix="downbeat-boot-")   # the copies of the build live here and are removed at the end
RUNS = int(sys.argv[1]) if len(sys.argv) > 1 else 5
OUT_JSON = sys.argv[2] if len(sys.argv) > 2 else None   # the numbers are printed; with a second argument they are also written there
TIMEOUT = 60.0


def one_run(index: int) -> dict:
    base = os.path.join(SP, "boot", f"run{index}")
    shutil.rmtree(base, ignore_errors=True)
    os.makedirs(base)
    copy_started = time.monotonic()
    for name in os.listdir(os.path.join(REPO, "builds", "mac")):
        source = os.path.join(REPO, "builds", "mac", name)
        target = os.path.join(base, name)
        if os.path.isdir(source):
            shutil.copytree(source, target, symlinks=True)
        else:
            shutil.copy2(source, target)
    copy_sec = time.monotonic() - copy_started
    logs = os.path.join(base, "logs")
    player_log = os.path.join(base, "player.log")
    exe = os.path.join(base, "Downbeat.app", "Contents", "MacOS", "Downbeat")
    heartbeat = os.path.join(logs, "heartbeat.txt")
    command = [exe, "-screen-width", "960", "-screen-height", "540", "-screen-fullscreen", "0", "--mute", "--no-record", "--input", "replay",
               "--health-dir", logs, "--quit-after", "20", "-logFile", player_log]
    t0 = time.monotonic()
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    marks: dict = {}
    first_line2 = None
    while time.monotonic() - t0 < TIMEOUT and process.poll() is None:
        now = time.monotonic() - t0
        if "first_beat" not in marks and os.path.exists(heartbeat):
            try:
                lines = open(heartbeat, encoding="utf-8").read().splitlines()
            except OSError:
                lines = []
            if len(lines) >= 2:
                marks["first_beat"] = now
                first_line2 = lines[1]
        if "first_beat" in marks and "audio_ok" not in marks:
            try:
                lines = open(heartbeat, encoding="utf-8").read().splitlines()
            except OSError:
                lines = []
            if len(lines) >= 2 and lines[1] == "audio=ok":
                marks["audio_ok"] = now
        if "ready" not in marks and os.path.exists(player_log):
            try:
                if b"[Downbeat] ready" in open(player_log, "rb").read():
                    marks["ready"] = now
            except OSError:
                pass
        if len(marks) == 3:
            break
        time.sleep(0.01)
    process.terminate()
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        process.kill()
    return {"run": index, "copy_sec": round(copy_sec, 2), "first_beat_sec": round(marks.get("first_beat", float("nan")), 2),
            "first_beat_audio_line": first_line2, "audio_ok_sec": round(marks.get("audio_ok", float("nan")), 2), "ready_sec": round(marks.get("ready", float("nan")), 2)}


def main() -> None:
    results = [one_run(i + 1) for i in range(RUNS)]
    for r in results:
        print(r)
    summary = {}
    for key in ("first_beat_sec", "audio_ok_sec", "ready_sec"):
        values = [r[key] for r in results if r[key] == r[key]]
        summary[key] = {"min": min(values), "median": statistics.median(values), "max": max(values), "n": len(values)}
    print(json.dumps(summary, indent=2))
    if OUT_JSON:
        with open(OUT_JSON, "w", encoding="utf-8") as handle:
            json.dump({"runs": results, "summary": summary}, handle, indent=2)
    shutil.rmtree(SP, ignore_errors=True)


if __name__ == "__main__":
    main()
