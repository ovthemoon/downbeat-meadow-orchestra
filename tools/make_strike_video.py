#!/usr/bin/env python3
"""Builds fake-camera videos with a hand that strikes downward, for Chrome's --use-file-for-fake-video-capture (y4m, 640x480, 30 fps).

The only picture used is one still photo of a hand (CC0, see NOTICE). It is cut out with a soft edge, put on a flat background of the
photo's own colour and moved frame by frame: strikes (fast downward moves), slow lifts, and slow moves between positions.
Coordinates in the schedules are what the game sees: x is MIRRORED (0 = left side as in a mirror), y is the palm center from the top.

usage: make_strike_video.py --image hand.png --out out.y4m --hands 1 --schedule demo
       --hands 0 makes an empty scene, --hands 2 uses a second, mirrored copy of the hand with its own schedule.
"""
import argparse
import math
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFilter

WIDTH, HEIGHT, FPS = 640, 480, 30

# (mirrored x, palm y at strike start, strike distance in frame heights, strike duration in seconds)
SCHEDULES = {
    # 14 strikes, about 0.9 s apart: sweeps through the three thirds and most of the height range
    'demo': [
        (0.17, 0.40, 0.16, 0.14), (0.17, 0.30, 0.14, 0.13), (0.50, 0.38, 0.16, 0.14), (0.50, 0.26, 0.17, 0.12),
        (0.83, 0.44, 0.18, 0.12), (0.83, 0.32, 0.16, 0.13), (0.50, 0.46, 0.15, 0.15), (0.17, 0.50, 0.17, 0.12),
        (0.83, 0.52, 0.12, 0.18), (0.50, 0.34, 0.20, 0.10), (0.17, 0.28, 0.14, 0.14), (0.83, 0.28, 0.18, 0.12),
        (0.50, 0.42, 0.13, 0.17), (0.17, 0.46, 0.18, 0.11),
    ],
    # second hand of the two-hand scene (right side of the picture mostly)
    'second': [
        (0.80, 0.36, 0.16, 0.13), (0.62, 0.44, 0.15, 0.14), (0.80, 0.30, 0.17, 0.12), (0.62, 0.38, 0.16, 0.13),
        (0.80, 0.48, 0.14, 0.15), (0.62, 0.30, 0.18, 0.11), (0.80, 0.40, 0.15, 0.14), (0.62, 0.50, 0.17, 0.12),
        (0.80, 0.34, 0.16, 0.13), (0.62, 0.42, 0.14, 0.15), (0.80, 0.28, 0.18, 0.11), (0.62, 0.36, 0.15, 0.14),
        (0.80, 0.46, 0.16, 0.12), (0.62, 0.32, 0.17, 0.13),
    ],
}
REST = (0.5, 0.45)
MOVE_SEC, HOLD_SEC, RETURN_SEC, LEAD_SEC = 0.55, 0.05, 0.26, 0.8  # slow moves between strikes stay below the trigger speed


def smooth(t):
    return t * t * (3 - 2 * t)


def build_track(strikes, offset_sec=0.0):
    """Keyframes (t0, t1, from, to, easing) for a hand that visits each strike position and strikes."""
    track = []
    t = LEAD_SEC + offset_sec
    here = REST
    for x, y0, depth, duration in strikes:
        track.append((t, t + MOVE_SEC, here, (x, y0), 'smooth'))
        t += MOVE_SEC + HOLD_SEC
        track.append((t, t + duration, (x, y0), (x, y0 + depth), 'accel'))
        t += duration
        top = (x, y0 + depth * 0.2)  # lift to a little below where it started, slowly
        track.append((t, t + RETURN_SEC, (x, y0 + depth), top, 'smooth'))
        t += RETURN_SEC
        here = top
    track.append((t, t + MOVE_SEC, here, REST, 'smooth'))
    return track, t + MOVE_SEC + LEAD_SEC


def position(track, t):
    pos = REST
    for t0, t1, a, b, easing in track:
        if t < t0:
            return pos
        if t >= t1:
            pos = b
            continue
        u = (t - t0) / (t1 - t0)
        k = u * u if easing == 'accel' else smooth(u)
        return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)
    return pos


def load_hand(path, scale, palm, flip):
    image = Image.open(path).convert('RGB')
    # the photo's table color: median of a band along the top edge, away from the hand
    band = sorted(image.getpixel((x, y)) for x in range(0, image.width, 6) for y in range(4, 40, 6))
    bg = band[len(band) // 2]
    size = (round(image.width * scale), round(image.height * scale))
    image = image.resize(size, Image.LANCZOS)
    inset = round(min(size) * 0.08)
    mask = Image.new('L', size, 0)
    ImageDraw.Draw(mask).rectangle([inset, inset, size[0] - inset, size[1] - inset], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(inset * 0.6))
    palm = (palm[0] * scale, palm[1] * scale)
    if flip:
        image = image.transpose(Image.FLIP_LEFT_RIGHT)
        mask = mask.transpose(Image.FLIP_LEFT_RIGHT)
        palm = (size[0] - palm[0], palm[1])
    return image, mask, palm, bg


def render_frame(bg, hands, t):
    canvas = Image.new('RGB', (WIDTH, HEIGHT), bg)
    for (image, mask, palm, track) in hands:
        mx, my = position(track, t)
        # the camera picture is the mirror of what the player sees: raw x = 1 - mirrored x
        px, py = (1 - mx) * WIDTH, my * HEIGHT
        coeffs = (1, 0, -(px - palm[0]), 0, 1, -(py - palm[1]))
        layer = image.transform((WIDTH, HEIGHT), Image.AFFINE, coeffs, resample=Image.BILINEAR, fillcolor=bg)
        alpha = mask.transform((WIDTH, HEIGHT), Image.AFFINE, coeffs, resample=Image.BILINEAR, fillcolor=0)
        canvas = Image.composite(layer, canvas, alpha)
    return canvas


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--image', required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--hands', type=int, choices=[0, 1, 2], default=1)
    parser.add_argument('--schedule', default='demo', choices=['demo'])
    parser.add_argument('--scale', type=float, default=0.30, help='hand image scale (hand size in the picture)')
    parser.add_argument('--palm', default='450,420', help='palm center in the source image, px')
    args = parser.parse_args()
    palm = tuple(float(v) for v in args.palm.split(','))

    first_image, first_mask, first_palm, bg = load_hand(args.image, args.scale, palm, flip=False)
    hands, end = [], 3.0
    if args.hands >= 1:
        track, end1 = build_track(SCHEDULES['demo'])
        hands.append((first_image, first_mask, first_palm, track))
        end = end1
    if args.hands == 2:
        image2, mask2, palm2, _ = load_hand(args.image, args.scale, palm, flip=True)
        track2, end2 = build_track(SCHEDULES['second'], offset_sec=0.45)
        hands.append((image2, mask2, palm2, track2))
        end = max(end, end2)
    frames = round(end * FPS)

    cmd = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{WIDTH}x{HEIGHT}', '-r', str(FPS),
           '-i', '-', '-pix_fmt', 'yuv420p', args.out]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for i in range(frames):
        proc.stdin.write(render_frame(bg, hands, i / FPS).tobytes())
    proc.stdin.close()
    if proc.wait() != 0:
        sys.exit('ffmpeg failed')
    print(f'{args.out}: {frames} frames, {frames / FPS:.1f} s, hands={args.hands}', file=sys.stderr)


if __name__ == '__main__':
    main()
