#!/usr/bin/env python3
"""Compares a capture of the app with a reference picture band by band: mean luminance and local contrast.

usage: tools/visual_bands.py --ref PATH --capture PATH [--out JSON] [--tile 32]

Both pictures are brought to the reference's own size (the capture is shrunk, nothing is enlarged). For each band of picture rows (fractions of the height from the
top) it prints, for the reference and the capture:
  mean luminance   mean of the relative luminance Y of linear light (0..1)
  local contrast   standard deviation of the gamma-encoded luma over tiles of --tile pixels, averaged over the tiles of the band (0..1; how much texture a tile has)
and the capture's value relative to the reference (+10 % = 10 % above). It measures; it does not judge. Needs Pillow and numpy (the tracker venv has both).
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

BANDS = [(0.40, 0.45), (0.45, 0.50), (0.50, 0.55), (0.55, 0.65), (0.65, 0.75), (0.75, 0.85), (0.85, 1.00)]
LUMA = np.array([0.2126, 0.7152, 0.0722])


def srgb_to_linear(values: np.ndarray) -> np.ndarray:
    return np.where(values <= 0.04045, values / 12.92, ((values + 0.055) / 1.055) ** 2.4)


def load(path: Path, size: tuple[int, int] | None) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    if size is not None and image.size != size:
        image = image.resize(size, Image.LANCZOS)
    return np.asarray(image).astype(float) / 255.0


def band_stats(picture: np.ndarray, top: float, bottom: float, tile: int) -> tuple[float, float]:
    height = picture.shape[0]
    rows = picture[int(top * height) : int(bottom * height)]
    mean_luminance = float((srgb_to_linear(rows) @ LUMA).mean())
    luma = rows @ LUMA  # gamma-encoded
    tile_rows, tile_columns = luma.shape[0] // tile, luma.shape[1] // tile
    tiles = luma[: tile_rows * tile, : tile_columns * tile].reshape(tile_rows, tile, tile_columns, tile)
    contrast = float(tiles.std(axis=(1, 3)).mean())
    return mean_luminance, contrast


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ref", type=Path, required=True)
    parser.add_argument("--capture", type=Path, required=True)
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--tile", type=int, default=32)
    args = parser.parse_args()

    reference = load(args.ref, None)
    capture = load(args.capture, (reference.shape[1], reference.shape[0]))
    rows = []
    print(f"{'band (rows from top)':22} {'lum ref':>8} {'lum cap':>8} {'diff':>7}   {'contrast ref':>12} {'contrast cap':>12} {'diff':>7}")
    for top, bottom in BANDS:
        lum_ref, con_ref = band_stats(reference, top, bottom, args.tile)
        lum_cap, con_cap = band_stats(capture, top, bottom, args.tile)
        lum_diff, con_diff = lum_cap / lum_ref - 1.0, con_cap / con_ref - 1.0
        rows.append({"band": [top, bottom], "luminanceRef": round(lum_ref, 4), "luminanceCapture": round(lum_cap, 4), "luminanceDiff": round(lum_diff, 3),
                     "contrastRef": round(con_ref, 4), "contrastCapture": round(con_cap, 4), "contrastDiff": round(con_diff, 3)})
        print(f"{top:.2f} - {bottom:.2f}{'':10} {lum_ref:8.4f} {lum_cap:8.4f} {lum_diff:+7.0%}   {con_ref:12.4f} {con_cap:12.4f} {con_diff:+7.0%}")
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps({"tile": args.tile, "reference": args.ref.name, "capture": args.capture.name, "bands": rows}, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
