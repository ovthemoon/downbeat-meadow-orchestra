#!/bin/sh
# Full S-1 measurement set used for docs/day0-results.md (about 35 minutes, silent: output is muted, only the tap records).
#  A: 60 fps emulation (vsync 2) x {calm, 50 ms blocks, blocks + hidden tab 10 s, blocks + window blur 10 s}, kiosk-style Chrome flags
#  B: display rate (120 Hz here, vsync 1) x {calm, blocks}
#  C: (dropped) default Chrome throttling: with the window partly covered by other windows Chrome pauses the page,
#     so the test never started; the kiosk flags below are what the exhibition needs anyway.
#  D: Unity template default runInBackground=false, vsync 2 x {blur}
#  E: PlayScheduled(dspTime + ahead) check, 12 clicks
#  F: AudioContext clock vs wall clock without Unity
set -eu
cd "$(dirname "$0")/.."
node tools/run_s1.mjs --build day0 --scenarios calm,stress,hidden,blur --duration 60 --vsync 2 --run A-vsync2-kiosk
node tools/run_s1.mjs --build day0 --scenarios calm,stress --duration 60 --vsync 1 --run B-vsync1-kiosk
node tools/run_s1.mjs --build day0 --scenarios blur --duration 60 --vsync 2 --bg 0 --run D-vsync2-runInBackground-off
node tools/run_s1.mjs --build day0 --ahead --vsync 2 --run E-ahead
for run in A-vsync2-kiosk B-vsync1-kiosk D-vsync2-runInBackground-off E-ahead; do node tools/analyze_s1.mjs "measurements/s1/$run" > /dev/null; done
node tools/run_clock_lag.mjs --run F-clock-lag
echo "all S-1 runs finished"
