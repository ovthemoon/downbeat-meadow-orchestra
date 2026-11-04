#!/usr/bin/env python3
"""Counts clipping and clicks in a 16-bit mono WAV (the final mix recorded by the player's --record-dir).

usage: tools/scan_wav.py FILE.wav [--json OUT.json]

  clipped      samples at full scale (-32768 or 32767): the mix hit the rails
  jumps        a "click" is a jump between two neighbouring samples that no musical tone at this volume makes. Counted at three sizes (a share of full scale):
               0.10, 0.20, 0.30. A 1 kHz tone at 0.3 full scale has steps of 0.04, a bell onset or a drum hit of this mix has steps well under 0.1.
               Neighbouring jumps (within 2 ms) count as one event. The first ones are listed with their time.
  silence      the share of 10 ms blocks below -80 dBFS (a meadow with nobody in it is quiet); dc is the mean (a click-free mix has none).
"""
import argparse
import json
import sys
import wave

import numpy as np

THRESHOLDS = (0.10, 0.20, 0.30)
MERGE_SEC = 0.002
SILENCE_DBFS = -80.0
LIST_LIMIT = 12


def read_mono16(path: str):
    with wave.open(path, "rb") as handle:
        if handle.getnchannels() != 1 or handle.getsampwidth() != 2:
            raise SystemExit("expected a 16-bit mono WAV")
        rate = handle.getframerate()
        data = np.frombuffer(handle.readframes(handle.getnframes()), dtype="<i2")
    return data, rate


def events(positions: np.ndarray, rate: int) -> list:
    merged = []
    for p in positions:
        if merged and (p - merged[-1]) / rate <= MERGE_SEC:
            continue
        merged.append(int(p))
    return merged


def scan(path: str) -> dict:
    samples, rate = read_mono16(path)
    x = samples.astype(np.float64) / 32768.0
    diff = np.abs(np.diff(x))
    block = max(1, int(rate * 0.010))
    usable = (len(x) // block) * block
    rms = np.sqrt(np.mean(x[:usable].reshape(-1, block) ** 2, axis=1)) if usable else np.array([0.0])
    result = {
        "file": path, "sampleRate": rate, "seconds": round(len(x) / rate, 3),
        "peakDbfs": round(20 * np.log10(max(np.max(np.abs(x)), 1e-9)), 2),
        "rmsDbfs": round(20 * np.log10(max(np.sqrt(np.mean(x ** 2)), 1e-9)), 2),
        "clipped": int(np.sum((samples == 32767) | (samples == -32768))),
        "dc": round(float(np.mean(x)), 6),
        "silentBlockShare": round(float(np.mean(20 * np.log10(np.maximum(rms, 1e-9)) < SILENCE_DBFS)), 3),
        "maxStep": round(float(np.max(diff)), 4) if len(diff) else 0.0,
        "jumpEvents": {},
        "firstJumps": [],
    }
    for threshold in THRESHOLDS:
        result["jumpEvents"][f"step>{threshold:.2f}"] = len(events(np.nonzero(diff > threshold)[0], rate))
    smallest = events(np.nonzero(diff > THRESHOLDS[0])[0], rate)
    result["firstJumps"] = [{"seconds": round(p / rate, 4), "step": round(float(diff[p]), 4)} for p in smallest[:LIST_LIMIT]]
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("wav")
    parser.add_argument("--json")
    args = parser.parse_args()
    result = scan(args.wav)
    print(json.dumps(result, indent=2))
    if args.json:
        with open(args.json, "w") as handle:
            json.dump(result, handle, indent=2)
            handle.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
