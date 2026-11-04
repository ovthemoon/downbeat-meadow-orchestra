# Native beat clock (Unity PlayScheduled on the DSP clock)

Criteria (Day 0 S-1): std <= 3 ms, max <= 10 ms, |drift| <= 5 ms/min, no missed or duplicate slots.

- **Batch mode (no window), verdict PASS:** 6/6 runs pass; worst std 0.003 ms, max 0.019 ms, |drift| 0.003 ms/min.
- **Windowed player, verdict FAIL:** 4/7 runs pass; worst std 0.936 ms, max 18.197 ms, |drift| 0.055 ms/min. 3 of 7 windowed runs had one main-thread stall of 0.93-0.98 s; the 6 batch runs had none (their longest frame is 67.9 ms, the deliberate 50 ms block of the stress runs). The failing windowed runs are exactly the stalled ones. The stall is read from three fields of each run's `*.meta.json`: the longest interval between two rendered frames (`rafIntervalsMs.max`, a name inherited from the Web harness; here it is one Unity frame), the longest time the DSP clock reading did not advance between two frames (`summary.maxDspStallSec`), and, only in W2-W6 which have it, the list of frames over 100 ms (`runtime.framesOver100ms`, W5: `t=14.4s:983ms`; W2, W3, W4, W6: `none`).

| Run | `rafIntervalsMs.max` (ms) | `summary.maxDspStallSec` (s) | `runtime.framesOver100ms` | Slots not scheduled | Std / max (ms) |
|---|---|---|---|---|---|
| W1 calm | 930.1 | 0.939 | not recorded (field added later) | 5 | 0.002 / 0.020 |
| W1 stress | 928.1 | 0.917 | not recorded (field added later) | 5 | 0.936 / 18.197 |
| W5 calm | 982.8 | 0.981 | `t=14.4s:983ms` | 6 | 0.000 / 0.000 |
| W2, W3, W4, W6 calm | 17.7-27.1 | 0.021-0.043 | `none` | 0 | 0.000-0.006 / 0.000-0.020 |
| R1-R3 batch, calm | 18.0-18.9 | 0.021 | (not recorded) | 0 | 0.000 / 0.000 |
| R1-R3 batch, stress | 67.4-67.9 | 0.064-0.085 | (not recorded) | 0 | 0.000-0.003 / 0.000-0.019 |

A clock scheduled 0.1 s ahead cannot survive a main-thread stall longer than that: the clicks due during the stall are not scheduled (or one is scheduled late). The clicks that were scheduled stayed sample exact (std <= 0.002 ms in the calm windowed runs). The cause of the stall is NOT diagnosed (see Conditions).

## What was measured

- The player (`builds/mac/Downbeat.app`, `--measure-clock`) schedules one click per 1/16 slot of the song in content.json through the same `NativeAudioEngine` the game uses (`AudioSource.PlayScheduled`, 0.1 s ahead, 32 voices), for 60 s per run.
- `AudioOutputTap` (`OnAudioFilterRead` on the AudioListener) records the mixer output; `tools/analyze_s1.mjs` finds the click onsets and compares them with the ideal grid. The first click absorbs a constant delay; a linear trend is the drift.
- Two ways of running the same measurement: R runs use `-batchmode` (no window); W runs open a 640x360 window that is rendered (vSync off, 60 fps cap). W2-W6 are calm-only repeats started by hand with the same flags after the player gained the long-frame list (`framesOver100ms` in the metadata), so they have no run-summary or load figures.
- "stress" blocks the main thread for 50 ms every 2 s (`Thread.Sleep`). "calm" does not.
- **Scope (same as Day 0 for Web Audio):** this is the position of the click inside Unity's mixer output. It does not include the audio device, the OS output buffer or the speaker. The output latency that Unity can estimate is listed below; it was not measured.

## Runs

| Run | Mode | Scenario | Onsets/slots | Missed | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Audio clock - wall clock (ms/run, +-21 ms resolution) | Late / stolen (engine) | Scheduler skipped | Capture gaps | Blocks (n, max ms) | Frame ms p50 / p95 / max | Result |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| R1 | batch | calm | 384/384 | 0 | 0.000 | 0.000 | 0.000 | 0.00 | 8.5 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.6 / 18.9 | PASS |
| R1 | batch | stress | 384/384 | 0 | 0.000 | 0.000 | 0.000 | 0.00 | 6.7 | 0 / 0 | 0 | 0 | 30, 51.0 | 16.7 / 16.7 / 67.8 | PASS |
| R2 | batch | calm | 384/384 | 0 | 0.000 | 0.000 | 0.000 | 0.00 | 6.6 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.5 / 18.2 | PASS |
| R2 | batch | stress | 384/384 | 0 | 0.003 | 0.002 | 0.019 | 0.00 | -9.5 | 0 / 0 | 0 | 0 | 30, 51.1 | 16.7 / 17.2 / 67.4 | PASS |
| R3 | batch | calm | 384/384 | 0 | 0.000 | 0.000 | 0.000 | 0.00 | 6.6 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.5 / 18.0 | PASS |
| R3 | batch | stress | 384/384 | 0 | 0.003 | 0.002 | 0.019 | 0.00 | -9.3 | 0 / 0 | 0 | 0 | 30, 51.3 | 16.7 / 17.4 / 67.9 | PASS |
| W1 | window | calm | 379/384 | 5 | 0.002 | 0.001 | 0.020 | 0.00 | 6.6 | 0 / 0 | 5 | 0 | 0, 0.0 | 16.7 / 16.8 / 930.1 | FAIL |
| W1 | window | stress | 379/384 | 5 | 0.936 | 0.073 | 18.197 | -0.05 | 15.0 | 1 / 0 | 5 | 0 | 30, 51.1 | 16.7 / 16.7 / 928.1 | FAIL |
| W2 | window | calm | 384/384 | 0 | 0.002 | 0.001 | 0.020 | 0.00 | 6.1 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.2 / 17.8 | PASS |
| W3 | window | calm | 384/384 | 0 | 0.000 | 0.000 | 0.000 | -0.00 | 5.9 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.0 / 27.1 | PASS |
| W4 | window | calm | 384/384 | 0 | 0.006 | 0.017 | 0.017 | 0.01 | 5.3 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.4 / 17.7 | PASS |
| W5 | window | calm | 378/384 | 6 | 0.000 | 0.000 | 0.000 | -0.00 | 8.8 | 0 / 0 | 6 | 0 | 0, 0.0 | 16.7 / 17.6 / 982.8 | FAIL |
| W6 | window | calm | 384/384 | 0 | 0.006 | 0.017 | 0.017 | 0.01 | 4.1 | 0 / 0 | 0 | 0 | 0, 0.0 | 16.7 / 17.6 / 23.2 | PASS |

## Conditions

- Machine: Apple M4 Pro, macOS 15.7.3, Unity 6000.3.9f1 (OSXPlayer, Mono, Release player).
- Audio: output sample rate 48000 Hz, DSP buffer 1024 frames x 4 (Unity default; estimated output latency = length x count / rate = 85.3 ms, not measured). The click was silenced after recording.
- Frame loop: vSync 0, targetFrameRate 60. No camera, no hand tracker.
- "Audio clock - wall clock" compares the DSP clock with the wall clock between the start and the end of the run; the DSP clock only advances once per DSP block (21 ms) and the two are read within one frame, so read it to about +-25 ms.
- **Main-thread stalls (not diagnosed):** only the windowed player stalled, at random moments (W5 at t=14.4 s; W1's moments were not recorded), in 3 of 7 runs of 60 s. Not tied to the deliberate 50 ms blocks, to a hand input (none here) or to the audio clock (it kept running: capture gaps 0). Candidates, none verified: the macOS window / Metal presentation of a window that is not in front, other apps on this shared Mac (load average 5-8). The 0.1 s `minLeadSec` of the design is the only margin against such stalls; a longer lead or a stall-free kiosk machine would be needed to hold a metronome-like stream through them. Whether this happens on the Windows PC is unknown.
- Shared machine: other Unity editors and apps were running, as in Day 0. Load average and the number of other Unity editor processes (editors, not import workers) per run:

| Run | Started (UTC) | Load avg at start (1/5/15 min) | Other Unity editor processes | Load avg after calm / after stress |
|---|---|---|---|---|
| R1 | 2026-10-29T06:53:02Z | 6.36 7.04 7.89 | 2 | 6.65 7.08 7.85 / 7.23 7.16 7.82 |
| R2 | 2026-10-29T06:55:09Z | 7.23 7.16 7.82 | 2 | 6.17 6.89 7.67 / 6.86 7.00 7.66 |
| R3 | 2026-10-29T06:57:16Z | 6.86 7.00 7.66 | 2 | 6.85 6.97 7.60 / 6.01 6.69 7.44 |
| W1 | 2026-10-29T07:26:15Z | 5.75 6.63 7.12 | 2 | 7.21 6.91 7.19 / 5.89 6.62 7.07 |
| W2 | - | - | - | - / - |
| W3 | - | - | - | - / - |
| W4 | - | - | - | - / - |
| W5 | - | - | - | - / - |
| W6 | - | - | - | - / - |

Raw data: `R<n>/*.meta.json`, `R<n>/analysis.json`, `R<n>/summary.csv`; the raw captures (`R<n>/raw-audio/*.f32`, 11.5 MB each) are git-ignored and reproducible with `tools/run_native_clock.sh`.
