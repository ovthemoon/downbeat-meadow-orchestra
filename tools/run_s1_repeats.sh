#!/bin/sh
# Two more repeats of the calm and stress cells at 60 fps (vsync 2), so each cell has N=3 together with run A.
# The machine is shared with other work (load average is recorded per run), so single runs are noisy.
set -eu
cd "$(dirname "$0")/.."
for n in 2 3; do
  node tools/run_s1.mjs --build day0 --scenarios calm,stress --duration 60 --vsync 2 --run "A$n-vsync2-kiosk-repeat"
  node tools/analyze_s1.mjs "measurements/s1/A$n-vsync2-kiosk-repeat" > /dev/null
done
echo "repeats finished"
