#!/bin/sh
# Continuation of run_s1_all.sh after A and B (kept because run C could not start, see run_s1_all.sh).
set -eu
cd "$(dirname "$0")/.."
node tools/run_s1.mjs --build day0 --scenarios blur --duration 60 --vsync 2 --bg 0 --run D-vsync2-runInBackground-off
node tools/run_s1.mjs --build day0 --ahead --vsync 2 --run E-ahead
for run in D-vsync2-runInBackground-off E-ahead; do node tools/analyze_s1.mjs "measurements/s1/$run" > /dev/null; done
node tools/run_clock_lag.mjs --run F-clock-lag
tools/run_s1_repeats.sh
echo "all S-1 runs finished"
