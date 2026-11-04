# UDP hand input smoke run (Task 4)

Python tracker (`tracker/`) -> UDP `127.0.0.1:47800` -> Mac player (`--input hand`). **The tracker reads a looping video file, never a camera**: `tests/fixtures/video/hand-strikes.mp4` (640x480, 30 fps, 6 s, one moving picture of a hand, CC0 photo; strikes about once a second).
Command: `tools/run_udp_smoke.sh` (raw logs in `measurements/day3/udp-smoke/<A|B|C>/`).

## Render frame times, 5 s windows summed up (player log, `--log-frame-stats 5`)

| Run | What | Seconds | Frames | fps | p50 ms | p95 ms | p99 ms | max ms | > 25 ms | > 50 ms | Worst 5 s window p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A | hand input, tracker running on a video (tracking on) | 44.5 | 5331 | 119.8 | 8.3 | 9.1 | 9.3 | 15.5 | 0 | 0 | 9.3 |
| B | hand input, no tracker (UDP socket open, nothing arrives) | 44.5 | 5333 | 119.6 | 8.3 | 8.8 | 9.3 | 84.2 | 1 | 1 | 9.2 |
| C | mouse input (no UDP socket) | 44.5 | 5332 | 119.6 | 8.3 | 9.0 | 9.3 | 75.5 | 1 | 1 | 9.3 |
| A2 | repeat of A | 44.5 | 5335 | 119.7 | 8.3 | 9.2 | 9.3 | 83.8 | 1 | 1 | 9.3 |
| A3 | repeat of A | 44.5 | 5331 | 119.8 | 8.3 | 9.0 | 9.3 | 16.1 | 0 | 0 | 9.3 |

## Longest frame in each 5 s window (ms, in time order)

| Run | Windows 1, 2, 3, ... |
|---|---|
| A | 9.3, 9.3, 15.5, 14.3, 12.1, 12.4, 12.0, 10.5 |
| B | 84.2, 16.4, 15.4, 10.8, 8.6, 12.2, 12.6, 8.7 |
| C | 75.5, 16.6, 16.7, 14.4, 14.5, 15.4, 12.6, 13.3 |
| A2 | 83.8, 9.3, 16.5, 12.5, 8.5, 17.5, 8.5, 16.8 |
| A3 | 11.9, 12.4, 13.2, 11.2, 12.0, 13.3, 16.1, 13.2 |

Windows with a frame over 50 ms: B window 1 (84.2 ms), C window 1 (75.5 ms), A2 window 1 (83.8 ms). Compare them with the table above: runs B and C have no hand tracking at all.
The screenshot (`--capture`) is requested 30 s after the process starts, which is about window 6; a screenshot costs the frame it is taken in.

## What the hand input did

| Run | Tracker state at the end | Frames with a hand in view | Notes scheduled (strikes) | Bad datagrams | Datagrams dropped in the ring | Tracker fps (median) | Tracker detection ms p50 (median) | Load average after (1/5/15 min) |
|---|---|---|---|---|---|---|---|---|
| A | Running | 5331/5331 | 37 | 0 | 0 | 30.0 | 11.1 | 8.04 8.72 7.88 |
| B | CameraMissing | 0/5333 | 0 | 0 | 0 | - | - | 6.25 8.12 7.70 |
| C | Off | 0/5332 | 0 | - | - | - | - | 5.54 7.66 7.56 |
| A2 | Running | 5335/5335 | 37 | 0 | 0 | 30.0 | 11.2 | 4.73 7.16 7.38 |
| A3 | Running | 5331/5331 | 37 | 0 | 0 | 30.0 | 11.2 | 6.19 7.16 7.36 |

## Conditions and limits

- Mac mini M4 Pro, macOS 15.7.3, Unity 6000.3.9f1 Mono player, window 960x540, quality level High (vSync on, URP). The frame rate is capped by the display's refresh rate, so read p95/max and the counts above 25 ms, not the fps alone.
- The Day 2 scene (stage and audience sprites) is what renders: it is light, so this shows that receiving and decoding datagrams does not disturb the main thread, not that the meadow scene holds its frame rate (Tasks 5-8 measure that).
- `--mute`: the notes are scheduled on the audio engine and counted, but the output is silenced, so nobody has *listened* to them. The tracker is a different process; its CPU use is shared with the player on this machine.
- Shared machine (other Unity editors and apps running): the load averages above are those after each run.
- A picture of a hand moved by a script, not a person: the strikes are the video's, the rule that turns them into notes is the Day 2 placeholder (`HandInterpreter`).
- Windows is not tested here.
