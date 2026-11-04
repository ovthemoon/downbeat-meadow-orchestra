#!/usr/bin/env python3
"""Puts a reference picture and a capture of the app side by side (two rows are not used: both are scaled to the same width and sit in one row).

usage: tools/visual_compare.py --ref PATH --capture PATH --out PATH [--width 1920] [--ref-label TEXT] [--capture-label TEXT]

The result is for a person to look at; it measures nothing. Needs Pillow (`pip install pillow`; the tracker venv has it through opencv/mediapipe's requirements,
or use the system python of macOS with Pillow installed).
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw

LABEL_HEIGHT = 28
GAP = 8
BACKGROUND = (16, 16, 20)
LABEL_COLOR = (230, 230, 235)


def fit(image: Image.Image, width: int) -> Image.Image:
    height = round(image.height * width / image.width)
    return image.resize((width, height), Image.LANCZOS)


def compare(ref: Image.Image, capture: Image.Image, total_width: int, ref_label: str, capture_label: str) -> Image.Image:
    tile_width = (total_width - GAP) // 2
    left = fit(ref.convert("RGB"), tile_width)
    right = fit(capture.convert("RGB"), tile_width)
    height = max(left.height, right.height) + LABEL_HEIGHT
    sheet = Image.new("RGB", (tile_width * 2 + GAP, height), BACKGROUND)
    sheet.paste(left, (0, LABEL_HEIGHT))
    sheet.paste(right, (tile_width + GAP, LABEL_HEIGHT))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 8), ref_label, fill=LABEL_COLOR)
    draw.text((tile_width + GAP + 8, 8), capture_label, fill=LABEL_COLOR)
    return sheet


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ref", type=Path, required=True)
    parser.add_argument("--capture", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--width", type=int, default=1920)
    parser.add_argument("--ref-label", default="reference: meadow_front_b")
    parser.add_argument("--capture-label", default="capture of the app")
    args = parser.parse_args()
    sheet = compare(Image.open(args.ref), Image.open(args.capture), args.width, args.ref_label, args.capture_label)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.out)
    print(f"wrote {args.out} ({sheet.width}x{sheet.height})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
