Criteria (plan 6.3): std <= 3 ms, max <= 10 ms, |drift| <= 5 ms/min. Values are min - max over the runs; "pass" counts runs meeting all three.

| Scenario | Path | Runs | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Missed slots | Audio clock - wall clock (ms/run) | Pass |
|---|---|---|---|---|---|---|---|---|---|
| calm | Unity PlayScheduled | 3 | 1.74 - 3.88 (median 3.39) | 2.87 - 5.20 | 5.84 - 55.72 | -14.1 - 2.4 | 0 - 0 | -9 - 0 | 1/3 |
| calm | Unity Play()/frame | 3 | 4.21 - 4.87 (median 4.23) | 7.12 - 9.15 | 8.62 - 12.38 | -15.2 - 0.8 | 0 - 0 | -16 - 0 | 0/3 |
| calm | Web Audio start(when) | 3 | 0.00 - 0.00 (median 0.00) | 0.00 - 0.00 | 0.00 - 0.00 | -0.0 - -0.0 | 0 - 0 | -7 - -3 | 3/3 |
| stress | Unity PlayScheduled | 3 | 2.48 - 7.76 (median 3.55) | 3.29 - 11.56 | 6.23 - 69.59 | -27.4 - -1.1 | 0 - 0 | -24 - -2 | 0/3 |
| stress | Unity Play()/frame | 3 | 6.38 - 10.77 (median 6.75) | 8.03 - 20.17 | 39.09 - 47.09 | -48.7 - 0.7 | 0 - 0 | -56 - -5 | 0/3 |
| stress | Web Audio start(when) | 3 | 0.00 - 0.00 (median 0.00) | 0.00 - 0.00 | 0.00 - 0.00 | -0.0 - -0.0 | 0 - 0 | -3 - 3 | 3/3 |
