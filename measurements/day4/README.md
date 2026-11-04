# Day 4 measurements (Task 9: energy, flow of a visit, nature layers)

All numbers come from the Mac player (Mono, Release) in a 1280x720 window with `--input replay --replay-loops 3` (the recorded hand file played three times); pictures
and video are drawn off-screen at exactly 1920x1080 (`--record-size`). Sound and picture files themselves are git-ignored (`captures/day4/`); only the numbers are kept here.

| File | What it is | How it was made |
|---|---|---|
| `experience-run.json` | The energy, its level 0..3, the state of the visit and the sky/firefly levels once a second, plus every state and level change | `--log-experience` (a 100 s run: `--quit-after 100 --mute`) |
| `wav-scan-session.json` | Clipping and click scan of the whole 100 s session's sound | `--record-dir ... --record-frames 0 --record-seconds 100`, then `tools/scan_wav.py` |
| `wav-scan-preview.json` | The same scan for the 31 s of the preview video | `tools/scan_wav.py` |
| `recording-session.json`, `recording-preview.json` | What the recorder saw: audio sample rate, the audio-clock time of the first recorded sample, peak, gaps, pictures taken and the rate they were taken at | `RunRecorder` |
| `video-sync.json` | Picture-to-sound offsets measured on the finished MP4 (haze jump of the far hills against the drum) | `tools/check_video_sync.py` |

The preview video is built with `tools/assemble_run_video.py captures/day4/preview captures/day4/task9-preview-30s.mp4 --start 0.8 --seconds 30` from the run
`--record-dir captures/day4/preview --record-size 1920x1080 --record-fps 24 --record-silent --record-seconds 31`. Each picture is stamped with the audio-clock time that was
being heard when it was drawn, so picture and sound are put together by those stamps, not by when the files started.

The sound is the final mix, first channel only (`AudioOutputTap`); all voices are mono 2D sources, so the other channel carries the same signal. Whether it sounds right is
for a person to judge: nothing here says the bells, the pad and the drum are balanced against the hand instruments.
