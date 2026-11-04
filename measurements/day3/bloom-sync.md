# Bloom sync (Task 7)

What is measured: for every flower that opened during a replay run (`--input replay --loop`, Mac player, 1920x1080 window, vSync on, display 120 Hz so a frame is 8.3 ms), how long after the moment its sound
is heard the bloom first reached 0.5 ("half open") on a frame of the clock the picture follows. Negative lag = the flower was half open before the sound. Raw files: `bloom-sync/`.
Command: `Downbeat.app --input replay --loop --mute --log-bloom-sync PATH --quit-after 22` (72 flowers open in a run).

| Variant | Clock the flowers follow | `meadow.flowers.bloom.leadSec` | Median lag | Median abs lag | In frames of 8.3 ms | p95 abs lag |
|---|---|---|---|---|---|---|
| a | raw audio clock (`HeardNow`, steps once per 21.3 ms audio block) | 0.065 | 16.7 ms | 17.8 ms | 2.14 | 35.1 ms (`bloom-sync/variant-a-rawclock-lead065.json`) |
| b | smoothed (`VisualNow`) | 0.065 | 10.9 / 9.8 ms (2 runs) | 11.7 / 10.1 ms | 1.41 / 1.22 | 49 ms (console output only, the raw files were overwritten by variant c) |
| c (shipped) | smoothed (`VisualNow`) | 0.075 | 1.1 / 2.0 / 2.8 ms (3 runs) | 2.8 / 4.0 / 4.6 ms | 0.34 / 0.48 / 0.55 | 58 ms |

Reading: the plan's target (median |lag| <= 1 frame) is met by variant c, also at a 60 Hz frame (16.7 ms: 0.17-0.28 frame). The smoothed clock (`SmoothClock`) removed most of the error; the audio clock
alone cannot do better than its block step of 21.3 ms. The tail (p95 about 58 ms, max about 64 ms) is flowers that are played again while they are still closing from an earlier bloom (they are already
part open and cross 0.5 earlier), not a late bloom. What this does not measure: the delay between the audio system's clock and the speaker (`OutputLatencySec` is Unity's estimate, 85 ms, not measured here), and
the display. The sound is placed on the 1/16 grid; the flower follows the time the sound is heard.
