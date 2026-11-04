Unity frame-time window 20 s each (first 10 frames skipped), Unity 6000.3.9f1 Release, Chrome Chrome/156.0.0.0.

| Window | Unity fps (mean) | Frame ms p50 | p95 | p99 | max | Frames >25 ms | >50 ms | JS rAF interval p50 / p95 / max (ms) |
|---|---|---|---|---|---|---|---|---|
| No tracking (before) | 72.0 | 14.0 | 15.0 | 15.0 | 20.0 | 0 | 0 | 6.9 / 7.7 / 12.5 |
| Pull every frame, no tracker | 72.0 | 14.0 | 15.0 | 15.0 | 18.0 | 0 | 0 | 6.9 / 7.7 / 7.9 |
| Tracking, GPU delegate | 71.8 | 14.0 | 25.0 | 26.0 | 31.0 | 74 | 0 | 6.9 / 7.7 / 14.0 |
| Tracking, CPU delegate | 36.2 | 31.0 | 65.0 | 67.9 | 74.0 | 390 | 71 | 7.0 / 28.2 / 35.5 |
| No tracking (after) | 72.0 | 14.0 | 15.0 | 15.0 | 16.0 | 0 | 0 | 6.9 / 7.7 / 7.9 |

| Tracker (same windows) | Frames | With hand | Inference ms p50 | p95 | p99 | max | Model create (ms) | Hand-frame delivery to Unity, inference end to C# read (ms) p50 / p95 / max |
|---|---|---|---|---|---|---|---|---|
| Tracking, GPU delegate | 601 | 601 (100%) | 9.9 | 11.6 | 13.4 | 17.0 | 101 | 0.2 / 0.3 / 0.5 (n=601) |
| Tracking, CPU delegate | 468 | 468 (100%) | 29.8 | 30.7 | 31.7 | 36.8 | 58 | 0.2 / 0.3 / 19.2 (n=396) |

Bridge, 10000 calls, tracker off (performance.now resolution is 0.1 ms in this Chrome, so totals are multiples of 0.1 ms):

| Call | Total (ms) | Per call (us) | Managed allocation during loop |
|---|---|---|---|
| Day0_NowMs() | 0.90 | 0.090 | - |
| Day0_ReadHands(double[135]) pull | 0.60 | 0.060 | 0 B |
| JSON string of the same frame (2534 chars), 1000 calls | 16.60 | 16.60 | 8192000 B (8192 B/call) |
| JS write into the C# double[] visible to C# | true (after 48 MB allocation / memory growth: true) | | |
| SendMessage(JSON string), no parse, 10000 calls | 151.0 | 15.10 (stringify alone 3.64) | received by Unity: 10000 |
| SendMessage(JSON string) + JsonUtility parse, 10000 calls | 192.5 | 19.25 (stringify alone 2.90) | received by Unity: 10000 |
