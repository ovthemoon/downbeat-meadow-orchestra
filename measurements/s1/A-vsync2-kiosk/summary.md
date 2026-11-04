### A-vsync2-kiosk

| Path | Scenario | Onsets/slots | Missed slots | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Criteria | After disturbance std / max (ms) | During disturbance | Scheduler skipped late | Audio clock minus wall clock (ms per run) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Unity Play()/frame | calm | 384/384 | 0 | 4.21 | 7.12 | 9.08 | 0.8 | FAIL(std) | - | - | 0 | 0 |
| Unity PlayScheduled | calm | 384/384 | 0 | 1.74 | 2.87 | 5.84 | -1.6 | PASS | - | - | 0 | 0 |
| Web Audio start(when) | calm | 384/384 | 0 | 0.00 | 0.00 | 0.00 | -0.0 | PASS | - | - | 0 | -4 |
| Unity Play()/frame | stress | 384/384 | 0 | 6.38 | 8.03 | 39.09 | 0.7 | FAIL(std,max) | - | - | 0 | -5 |
| Unity PlayScheduled | stress | 384/384 | 0 | 2.48 | 4.72 | 6.23 | -8.1 | FAIL(drift) | - | - | 0 | -7 |
| Web Audio start(when) | stress | 384/384 | 0 | 0.00 | 0.00 | 0.00 | -0.0 | PASS | - | - | 0 | -3 |
| Unity Play()/frame | hidden | 341/384 | 64 | 6.72 | 11.01 | 34.91 | -56.8 | FAIL(std,max,drift) | 18.82 / 75.85 | 1 on, max 11.30 | 0 | -47 |
| Unity PlayScheduled | hidden | 321/384 | 64 | 1.76 | 2.90 | 4.37 | 3.9 | PASS | 5.19 / 65.10 | 0 on, max - | 64 | -4 |
| Web Audio start(when) | hidden | 321/384 | 63 | 0.00 | 0.00 | 0.00 | 0.0 | PASS | 0.00 / 0.00 | 1 on, max 0.00 | 63 | -67 |
| Unity Play()/frame | blur | 384/384 | 0 | 7.42 | 12.26 | 36.25 | -42.2 | FAIL(std,max,drift) | 8.60 / 58.12 | 64 on, max 42.68 | 0 | -20 |
| Unity PlayScheduled | blur | 384/384 | 0 | 1.69 | 2.90 | 3.17 | 1.5 | PASS | 1.86 / 5.28 | 64 on, max 3.40 | 0 | -0 |
| Web Audio start(when) | blur | 384/384 | 0 | 0.00 | 0.00 | 0.00 | 0.0 | PASS | 0.00 / 0.00 | 64 on, max 0.00 | 0 | -113 |
