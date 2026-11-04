#!/usr/bin/env python3
"""Reads the player and tracker logs of tools/run_udp_smoke.sh and writes measurements/day3/udp-smoke.md."""
from __future__ import annotations

import re
import statistics
import sys
from pathlib import Path

LABELS = {
    "A": "hand input, tracker running on a video (tracking on)",
    "B": "hand input, no tracker (UDP socket open, nothing arrives)",
    "C": "mouse input (no UDP socket)",
    "A2": "repeat of A",
    "A3": "repeat of A",
}
SCENARIOS = ("A", "B", "C", "A2", "A3")


def parse_kv(line: str) -> dict[str, str]:
    return dict(re.findall(r"(\w+)=(\S+)", line))


def total_of(player_log: Path) -> dict[str, str]:
    for line in reversed(player_log.read_text(errors="replace").splitlines()):
        if "[frame-stats] TOTAL" in line:
            return parse_kv(line)
    return {}


def windows_of(player_log: Path) -> list[dict[str, str]]:
    return [parse_kv(line) for line in player_log.read_text(errors="replace").splitlines() if "[frame-stats] window" in line]


def tracker_numbers(tracker_log: Path) -> tuple[float, float] | None:
    if not tracker_log.exists():
        return None
    rows = [parse_kv(line) for line in tracker_log.read_text(errors="replace").splitlines() if "infer_ms_p50=" in line]
    rows = rows[1:]  # the first window includes start-up
    if not rows:
        return None
    return statistics.median(float(r["fps"]) for r in rows), statistics.median(float(r["infer_ms_p50"]) for r in rows)


def main(out_dir: Path) -> None:
    rows = []
    for scenario in SCENARIOS:
        directory = out_dir / scenario
        total = total_of(directory / "player.log")
        windows = windows_of(directory / "player.log")
        worst_window_p95 = max((float(w["p95"]) for w in windows), default=float("nan"))
        tracker = tracker_numbers(directory / "tracker.log")
        load = (directory / "load-after.txt").read_text().strip() if (directory / "load-after.txt").exists() else "-"
        rows.append((scenario, total, windows, worst_window_p95, tracker, load))

    lines = [
        "# UDP hand input smoke run (Task 4)",
        "",
        "Python tracker (`tracker/`) -> UDP `127.0.0.1:47800` -> Mac player (`--input hand`). **The tracker reads a looping video file, never a camera**: "
        "`tests/fixtures/video/hand-strikes.mp4` (640x480, 30 fps, 6 s, one moving picture of a hand, CC0 photo; strikes about once a second).",
        "Command: `tools/run_udp_smoke.sh` (raw logs in `measurements/day3/udp-smoke/<A|B|C>/`).",
        "",
        "## Render frame times, 5 s windows summed up (player log, `--log-frame-stats 5`)",
        "",
        "| Run | What | Seconds | Frames | fps | p50 ms | p95 ms | p99 ms | max ms | > 25 ms | > 50 ms | Worst 5 s window p95 ms |",
        "|---|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for scenario, total, _windows, worst, _tracker, _load in rows:
        if not total:
            lines.append(f"| {scenario} | {LABELS[scenario]} | no TOTAL line in the log |")
            continue
        lines.append(f"| {scenario} | {LABELS[scenario]} | {total['seconds']} | {total['frames']} | {total['fps']} | {total['p50']} | {total['p95']} | {total['p99']} | {total['max']} | {total['over25ms']} | {total['over50ms']} | {worst:.1f} |")

    lines += ["", "## Longest frame in each 5 s window (ms, in time order)", "", "| Run | Windows 1, 2, 3, ... |", "|---|---|"]
    for scenario, _total, windows, _worst, _tracker, _load in rows:
        lines.append(f"| {scenario} | " + ", ".join(w["max"] for w in windows) + " |")
    long_frames = [
        f"{scenario} window {index + 1} ({w['max']} ms)"
        for scenario, _t, windows, _w, _tr, _l in rows
        for index, w in enumerate(windows)
        if float(w["max"]) > 50.0
    ]
    lines += [
        "",
        "Windows with a frame over 50 ms: " + (", ".join(long_frames) if long_frames else "none") + ". "
        "Compare them with the table above: runs B and C have no hand tracking at all.",
        "The screenshot (`--capture`) is requested 30 s after the process starts, which is about window 6; a screenshot costs the frame it is taken in.",
        "",
        "## What the hand input did", "", "| Run | Tracker state at the end | Frames with a hand in view | Notes scheduled (strikes) | Bad datagrams | Datagrams dropped in the ring | Tracker fps (median) | Tracker detection ms p50 (median) | Load average after (1/5/15 min) |", "|---|---|---|---|---|---|---|---|---|"]
    for scenario, total, _windows, _worst, tracker, load in rows:
        if not total:
            continue
        tracker_text = f"{tracker[0]:.1f} | {tracker[1]:.1f}" if tracker else "- | -"
        lines.append(f"| {scenario} | {total.get('tracker', '-')} | {total.get('frames_with_hands', '-')} | {total.get('notes', '-')} | {total.get('bad_packets', '-')} | {total.get('dropped_packets', '-')} | {tracker_text} | {load} |")

    lines += [
        "",
        "## Conditions and limits",
        "",
        "- Mac mini M4 Pro, macOS 15.7.3, Unity 6000.3.9f1 Mono player, window 960x540, quality level High (vSync on, URP). The frame rate is capped by the display's refresh rate, so read p95/max and the counts above 25 ms, not the fps alone.",
        "- The Day 2 scene (stage and audience sprites) is what renders: it is light, so this shows that receiving and decoding datagrams does not disturb the main thread, not that the meadow scene holds its frame rate (Tasks 5-8 measure that).",
        "- `--mute`: the notes are scheduled on the audio engine and counted, but the output is silenced, so nobody has *listened* to them. The tracker is a different process; its CPU use is shared with the player on this machine.",
        "- Shared machine (other Unity editors and apps running): the load averages above are those after each run.",
        "- A picture of a hand moved by a script, not a person: the strikes are the video's, the rule that turns them into notes is the Day 2 placeholder (`HandInterpreter`).",
        "- Windows is not tested here.",
        "",
    ]
    out = "\n".join(lines)
    (out_dir.parent / "udp-smoke.md").write_text(out, encoding="utf-8")
    print(out)


if __name__ == "__main__":
    main(Path(sys.argv[1]))
