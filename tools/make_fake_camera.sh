#!/bin/sh
# Builds the fake-camera video for S-2 from a still image of a hand (ffmpeg 7.x).
# Chrome flag: --use-fake-device-for-media-stream --use-file-for-fake-video-capture=<out.y4m>
# usage: tools/make_fake_camera.sh <image> <out.y4m> [sway]   (sway=1 moves the hand left-right with a 2 s period, loops seamlessly)
set -eu
image="$1"
out="$2"
sway="${3:-0}"
if [ "$sway" = "1" ]; then
  x_expr="(iw-640)/2+(iw-640)/2*0.9*sin(2*PI*t/2)"
else
  x_expr="(iw-640)/2"
fi
ffmpeg -hide_banner -loglevel error -y -loop 1 -i "$image" -t 2 -r 30 \
  -vf "scale=800:-1,crop=640:480:x='${x_expr}':y='(ih-480)/2'" -pix_fmt yuv420p "$out"
ls -l "$out"
