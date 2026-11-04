#!/usr/bin/env python3
"""Measures how a meadow picture sequence moves: how much, and how much of it is flicker rather than slow sway.

usage: tools/wind_metrics.py DIR [DIR ...] [--rows 0.5:1.0] [--json OUT.json]
       (run with the tracker venv's Python: it has numpy and Pillow)

DIR holds f00000.png ... and frames.txt from the player's --frame-dump (an even rate: Unity's clock stepped 1/fps per picture, so every run of the same
settings gives the same pictures). Only the rows of the picture given by --rows (a share of the height, from the top; default the lower half, the 3D grass) are measured, on
luminance (0..255). For each sequence:
  motionRms        RMS of the change of a pixel from one picture to the next (how much the picture moves)
  flickerRms       RMS of the second difference I[t+1] - 2 I[t] + I[t-1] (what a slow, smooth sway does not have: change of the change)
  flickerRatio     flickerRms / motionRms. For one pixel that swings smoothly at f hertz this is about 2 pi f / fps, so it is the typical frequency of the picture's motion
  typicalHz        flickerRatio * fps / (2 pi): the temporal frequency that the motion has (a gentle sway is below 1 Hz; a shiver is several Hz)
  peakToMedian     the strongest picture-to-picture change over the median one (how pulsed the motion is: a beat or a gust shows as a peak)
  stillShare       share of picture pairs in which less than 0.05 of a grey step changed (a frozen picture)
  unexplained      (the main number) how much of the change between pictures --lag steps apart is NOT a smooth local shift: each 32 x 32 block of the later picture is matched to the earlier one
                   at its best shift (up to --search pixels); the share of the block's change that no shift removes, averaged over the blocks and weighted by how much they changed.
                   Grass that sways as one surface moves its blades together: a block is explained by a shift (small number). Blades that move out of step with their neighbours (crawl, boil, tremble)
                   cannot be explained by any shift (number near 1).
  flowRoughness    how far (pixels) the best shift of a block is from the best shift of its right and lower neighbours, on average: coherent motion changes slowly from place to place
"""
import argparse
import json
import math
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
STILL_LEVEL = 0.05


def read_meta(directory: Path) -> dict:
    meta = {}
    path = directory / "frames.txt"
    if path.exists():
        for line in path.read_text().splitlines():
            if "=" in line:
                key, value = line.split("=", 1)
                meta[key.strip()] = value.strip()
    return meta


def luminance(path: Path, row_from: float, row_to: float) -> np.ndarray:
    with Image.open(path) as image:
        rgb = np.asarray(image.convert("RGB"), dtype=np.float32)
    height = rgb.shape[0]
    block = rgb[int(row_from * height):int(row_to * height)]
    return block @ LUMA


def measure(directory: Path, row_from: float, row_to: float) -> dict:
    files = sorted(directory.glob("f*.png"), key=lambda p: int(re.sub(r"\D", "", p.stem)))
    if len(files) < 3:
        raise SystemExit(f"{directory}: need at least 3 pictures, found {len(files)}")
    meta = read_meta(directory)
    fps = float(meta.get("fps", 20))
    prev2 = None
    prev = None
    first_sq = 0.0
    first_n = 0
    second_sq = 0.0
    second_n = 0
    steps = []
    still = 0
    for path in files:
        current = luminance(path, row_from, row_to)
        if prev is not None:
            diff = current - prev
            steps.append(float(np.sqrt(np.mean(diff * diff))))
            first_sq += float(np.sum(diff * diff))
            first_n += diff.size
            if steps[-1] < STILL_LEVEL:
                still += 1
        if prev2 is not None:
            second = current - 2.0 * prev + prev2
            second_sq += float(np.sum(second * second))
            second_n += second.size
        prev2, prev = prev, current
    motion = math.sqrt(first_sq / first_n)
    flicker = math.sqrt(second_sq / second_n)
    ratio = flicker / motion if motion > 0 else 0.0
    median = float(np.median(steps))
    return {
        "name": directory.name,
        "pictures": len(files),
        "fps": fps,
        "rows": [row_from, row_to],
        "quality": meta.get("quality", ""),
        "motionRms": round(motion, 4),
        "flickerRms": round(flicker, 4),
        "flickerRatio": round(ratio, 4),
        "typicalHz": round(ratio * fps / (2.0 * math.pi), 3),
        "peakToMedian": round(max(steps) / median, 2) if median > 0 else None,
        "stillShare": round(still / len(steps), 3),
    }


BLOCK = 32
SEARCH = 6


def block_match(a: np.ndarray, b: np.ndarray, search: int) -> tuple:
    """Per block of a (the later picture): the error at zero shift, the best error over shifts within +-search, and the best shift."""
    height = (a.shape[0] - 2 * search) // BLOCK * BLOCK
    width = (a.shape[1] - 2 * search) // BLOCK * BLOCK
    core = a[search:search + height, search:search + width]
    base = None
    best = None
    best_dx = np.zeros((height // BLOCK, width // BLOCK), dtype=np.int32)
    best_dy = np.zeros_like(best_dx)
    for dy in range(-search, search + 1):
        for dx in range(-search, search + 1):
            moved = b[search + dy:search + dy + height, search + dx:search + dx + width]
            err = ((core - moved) ** 2).reshape(height // BLOCK, BLOCK, width // BLOCK, BLOCK).mean(axis=(1, 3))
            if dx == 0 and dy == 0:
                base = err
            if best is None:
                best, best_dx[:], best_dy[:] = err.copy(), dx, dy
                continue
            better = err < best
            best = np.where(better, err, best)
            best_dx = np.where(better, dx, best_dx)
            best_dy = np.where(better, dy, best_dy)
    return base, best, best_dx, best_dy


def coherence(files: list, row_from: float, row_to: float, lag: int, pairs: int, search: int) -> dict:
    last = len(files) - lag - 1
    picks = np.linspace(0, last, min(pairs, last + 1)).astype(int)
    weight = 0.0
    unexplained = 0.0
    rough_sum = 0.0
    rough_n = 0
    for i in picks:
        earlier = luminance(files[i], row_from, row_to)
        later = luminance(files[i + lag], row_from, row_to)
        base, best, dx, dy = block_match(later, earlier, search)
        weight += float(base.sum())
        unexplained += float(best.sum())
        rough_sum += float(np.abs(dx[:, 1:] - dx[:, :-1]).sum() + np.abs(dy[:, 1:] - dy[:, :-1]).sum() + np.abs(dx[1:] - dx[:-1]).sum() + np.abs(dy[1:] - dy[:-1]).sum())
        rough_n += dx[:, 1:].size * 2 + dx[1:].size * 2
    return {"lagPictures": lag, "unexplained": round(unexplained / weight, 4) if weight > 0 else None, "flowRoughness": round(rough_sum / rough_n, 3) if rough_n else None}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("dirs", nargs="+")
    parser.add_argument("--rows", default="0.5:1.0")
    parser.add_argument("--json")
    parser.add_argument("--lag", type=int, default=5, help="pictures between the two that are matched (default 5)")
    parser.add_argument("--pairs", type=int, default=16, help="how many picture pairs are matched per sequence (default 16)")
    parser.add_argument("--search", type=int, default=SEARCH)
    args = parser.parse_args()
    row_from, row_to = (float(part) for part in args.rows.split(":"))
    results = []
    for d in args.dirs:
        result = measure(Path(d), row_from, row_to)
        files = sorted(Path(d).glob("f*.png"), key=lambda p: int(re.sub(r"\D", "", p.stem)))
        result.update(coherence(files, row_from, row_to, args.lag, args.pairs, args.search))
        results.append(result)
    for r in results:
        print(f"{r['name']:<24} motion {r['motionRms']:<8} unexplained {r['unexplained']:<7} flowRoughness {r['flowRoughness']:<6} peak/median {r['peakToMedian']}  still {r['stillShare']}")
    if args.json:
        Path(args.json).write_text(json.dumps({"runs": results}, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
