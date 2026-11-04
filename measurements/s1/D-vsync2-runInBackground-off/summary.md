### D-vsync2-runInBackground-off

| Path | Scenario | Onsets/slots | Missed slots | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Criteria | After disturbance std / max (ms) | During disturbance | Scheduler skipped late | Audio clock minus wall clock (ms per run) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Unity Play()/frame | blur | 343/384 | 64 | 6.73 | 8.87 | 41.30 | -9.1 | FAIL(std,max,drift) | 17.62 / 77.66 | 0 on, max - | 0 | 4 |
| Unity PlayScheduled | blur | 387/384 | 176 | 20.22 | 48.21 | 51.14 | 7.8 | FAIL(std,max,drift) | 5.74 / 60.81 | 0 on, max - | 0 | 3 |
| Web Audio start(when) | blur | 384/384 | 0 | 0.00 | 0.00 | 0.00 | -0.0 | PASS | 0.00 / 0.00 | 64 on, max 0.00 | 0 | -3 |
