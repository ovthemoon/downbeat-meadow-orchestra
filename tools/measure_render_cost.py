#!/usr/bin/env python3
"""usage: python3 tools/measure_render_cost.py OUT.json [SECONDS] [WIDTHxHEIGHT] [variant ...]   (needs builds/mac from tools/unity_build.sh mac, or PLAYER_DIR=another copy of it; EXTRA_ARGS adds launch options)

How much each rendering lever costs, on this Mac: the Mac player runs the recorded demo hands uncapped in a window for SECONDS, once per variant, and the TOTAL line of --log-frame-stats gives the
mean frame time and the mean GPU time (Unity's FrameTimingManager). A variant is a name from VARIANTS: it edits the tier that is measured in a copy of the content folder next to the player (the content
folder is read without a rebuild), so the numbers differ only by that lever. Numbers are for this Mac (an Apple GPU, tile based): they say which lever is big, not what an Intel UHD will give.
"""
import copy
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = sys.argv[1]
SECONDS = int(sys.argv[2]) if len(sys.argv) > 2 else 22
SIZE = sys.argv[3] if len(sys.argv) > 3 else "1920x1080"
ONLY = sys.argv[4:]

BLOOM_CHEAP = {"enabled": True, "filter": "Dual", "downscale": "Quarter", "maxIterations": 4}
VARIANTS = {
    "baseline": {},
    "scale0.85": {"renderScale": 0.85},
    "scale0.7": {"renderScale": 0.7},
    "scale0.6": {"renderScale": 0.6},
    "bloom-dual-quarter-4": {"bloom": BLOOM_CHEAP},
    "bloom-off": {"bloom": {"enabled": False, "filter": "Gaussian", "downscale": "Half", "maxIterations": 6}},
    "sky-cheap": {"skyDetail": 0},
    "msaa2": {"msaa": 2},
    "msaa4": {"msaa": 4},
    "low-levers": {"renderScale": 0.7, "bloom": BLOOM_CHEAP, "skyDetail": 0},
}


def run(app_dir: str, tier: str, name: str, changes: dict) -> dict:
    path = os.path.join(app_dir, "content", "content.json")
    base = json.load(open(os.path.join(REPO, "content", "content.json"), encoding="utf-8"))
    for entry in base["meadow"]["quality"]:
        if entry["name"] == tier:
            entry.update(copy.deepcopy(changes))
            entry["targetFps"] = 0
    json.dump(base, open(path, "w", encoding="utf-8"))
    log = os.path.join(app_dir, f"{name}.log")
    width, height = SIZE.split("x")
    exe = os.path.join(app_dir, "Downbeat.app", "Contents", "MacOS", "Downbeat")
    command = [exe, "-screen-width", width, "-screen-height", height, "-screen-fullscreen", "0", "--quality", tier, "--mute", "--no-record", "--input", "replay",
               "--log-frame-stats", "5", "--quit-after", str(SECONDS), "-logFile", log]
    command += os.environ.get("EXTRA_ARGS", "").split()   # EXTRA_ARGS="--wind-mute hands": more launch options, to find which part of the picture costs
    if not os.environ.get("CAPPED"):
        command.insert(command.index("--mute"), "--uncapped")   # CAPPED=1: the display's own vertical sync, as the app runs (the GPU time is then not inflated by frames queued up)
    subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=SECONDS + 90)
    text = open(log, encoding="utf-8", errors="replace").read()
    total = re.findall(r"\[frame-stats\] TOTAL (.*)", text)
    fields = dict(re.findall(r"(\w+)=([^\s]+)", total[-1])) if total else {}
    return {"fps": float(fields.get("fps", "nan")), "p50_ms": float(fields.get("p50", "nan")), "p95_ms": float(fields.get("p95", "nan")),
            "gpu_ms": float(fields["gpu_ms"]) if fields.get("gpu_ms", "n/a") != "n/a" else None, "frames": int(fields.get("frames", "0")), "look": re.findall(r"quality=(.*?) tracker=", total[-1])[0] if total else None}


def main() -> None:
    app_dir = tempfile.mkdtemp(prefix="downbeat-render-")
    player = os.environ.get("PLAYER_DIR") or os.path.join(REPO, "builds", "mac")   # PLAYER_DIR: another copy of the Mac player (a build from before a change, to compare with)
    for name in os.listdir(player):
        source = os.path.join(player, name)
        (shutil.copytree if os.path.isdir(source) else shutil.copy2)(source, os.path.join(app_dir, name), **({"symlinks": True} if os.path.isdir(source) else {}))
    results = {}
    try:
        for tier in os.environ.get("TIERS", "Low High").split():   # TIERS=Low: only the weak tier
            for name, changes in VARIANTS.items():
                if ONLY and name not in ONLY:
                    continue
                if tier == "High" and name not in ("baseline", "scale0.85", "bloom-dual-quarter-4", "msaa2"):
                    continue
                runs = [run(app_dir, tier, f"{tier}-{name}-{n}", changes) for n in range(int(os.environ.get("REPS", "1")))]
                best = dict(runs[0])
                best["fps"] = round(sum(r["fps"] for r in runs) / len(runs), 1)          # the mean of the repetitions; a single run varies by about 5 per cent
                best["fps_runs"] = [r["fps"] for r in runs]
                best["frame_ms"] = round(1000.0 / best["fps"], 3)
                results[f"{tier}/{name}"] = best
                print(tier, name, best, flush=True)
    finally:
        shutil.rmtree(app_dir, ignore_errors=True)
    json.dump({"window": SIZE, "seconds": SECONDS, "runs": results}, open(OUT, "w"), indent=2)


if __name__ == "__main__":
    main()
