### B-vsync1-kiosk

| Path | Scenario | Onsets/slots | Missed slots | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Criteria | After disturbance std / max (ms) | During disturbance | Scheduler skipped late | Audio clock minus wall clock (ms per run) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Unity Play()/frame | calm | 384/384 | 0 | 15.80 | 43.95 | 52.58 | -65.5 | FAIL(std,max,drift) | - | - | 0 | -96 |
| Unity PlayScheduled | calm | 384/384 | 0 | 3.71 | 7.49 | 12.39 | -17.8 | FAIL(std,max,drift) | - | - | 0 | -11 |
| Web Audio start(when) | calm | 384/384 | 0 | 0.00 | 0.00 | 0.00 | -0.0 | PASS | - | - | 0 | -9 |
| Unity Play()/frame | stress | 384/384 | 0 | 5.94 | 5.93 | 43.68 | -0.1 | FAIL(std,max) | - | - | 0 | -1 |
| Unity PlayScheduled | stress | 385/384 | 0 | 3.63 | 3.20 | 61.78 | 0.1 | FAIL(std,max) | - | - | 0 | -2 |
| Web Audio start(when) | stress | 384/384 | 0 | 0.00 | 0.00 | 0.00 | -0.0 | PASS | - | - | 0 | -3 |
