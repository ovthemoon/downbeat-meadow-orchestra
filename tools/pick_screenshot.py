#!/usr/bin/env python3
"""Picks the screenshot candidate with the strongest visible flash on the stage (brightest performer regions) and
writes it as the final screenshot. Usage: python3 tools/pick_screenshot.py docs/media docs/media/day1-screenshot.png"""
import sys
from pathlib import Path

from PIL import Image, ImageStat

# performer regions in a 1600x600 page screenshot (stage view is the left 2/3): x0, y0, x1, y1
REGIONS = [(40, 150, 360, 540), (400, 190, 680, 560), (760, 180, 1040, 560)]


def score(path: Path) -> float:
    image = Image.open(path).convert("L")
    return sum(ImageStat.Stat(image.crop(region)).mean[0] for region in REGIONS)


def main(folder: str, target: str) -> int:
    candidates = sorted(Path(folder).glob("day1-screenshot-candidate-*.png"))
    if not candidates:
        print("no candidates", file=sys.stderr)
        return 1
    scored = sorted(((score(p), p) for p in candidates), reverse=True)
    best = scored[0][1]
    Image.open(best).convert("RGB").save(target, optimize=True)
    print(f"picked {best.name} (score {scored[0][0]:.1f}; range {scored[-1][0]:.1f}..{scored[0][0]:.1f}) -> {target}")
    for p in candidates:
        p.unlink()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
