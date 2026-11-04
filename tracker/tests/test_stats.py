"""Stats line and run summary."""

from __future__ import annotations

import pytest

from downbeat_tracker.stats import StatsReporter, percentile


def test_percentile_is_nearest_rank():
    values = [10.0, 20.0, 30.0, 40.0, 50.0]

    assert percentile(values, 0.5) == 30.0
    assert percentile(values, 0.95) == 50.0
    assert percentile(values, 0.0) == 10.0
    assert percentile([], 0.5) == 0.0


def test_the_line_comes_once_a_second_with_fps_p50_and_hands():
    stats = StatsReporter(start_sec=0.0)
    for i in range(30):
        stats.record(infer_ms=10.0 + i, hand_count=1, now_sec=i / 30.0)

    assert stats.line(0.5) is None
    line = stats.line(1.0)
    assert line == "fps=30.0 infer_ms_p50=24.0 hands=1"
    assert stats.line(1.2) is None, "the window starts over after a line"


def test_the_summary_fps_ignores_start_up_time():
    stats = StatsReporter(start_sec=0.0)
    for i in range(10):
        stats.record(infer_ms=8.0, hand_count=i % 2, now_sec=20.0 + i * 0.1)  # the first frame only came after 20 s (model download)

    summary = stats.summary()

    assert summary.frames == 10
    assert summary.frames_with_hands == 5
    assert summary.seconds == pytest.approx(0.9)
    assert summary.fps == pytest.approx(10.0)
    assert summary.infer_ms_p50 == 8.0
    assert summary.infer_ms_max == 8.0


def test_an_empty_summary_is_all_zero():
    summary = StatsReporter(start_sec=0.0).summary()

    assert (summary.frames, summary.fps, summary.infer_ms_p50) == (0, 0.0, 0.0)


def test_a_long_run_keeps_a_bounded_number_of_samples_and_exact_max_and_mean():
    stats = StatsReporter(start_sec=0.0, reservoir_size=500)
    total = 200_000
    for i in range(total):
        stats.record(infer_ms=float(i % 100), hand_count=i % 2, now_sec=i / 30.0)

    assert stats.stored_samples <= 500, "a night of frames must not grow the memory"
    summary = stats.summary()
    assert summary.frames == total
    assert summary.frames_with_hands == total // 2
    assert summary.infer_ms_max == 99.0, "the largest value is exact, not sampled"
    assert summary.infer_ms_mean == pytest.approx(49.5), "the mean is exact, not sampled"
    assert abs(summary.infer_ms_p50 - 49.0) <= 6.0
    assert abs(summary.infer_ms_p95 - 94.0) <= 6.0


def test_a_short_run_is_exact_as_before():
    stats = StatsReporter(start_sec=0.0)
    for i in range(1, 101):
        stats.record(infer_ms=float(i), hand_count=0, now_sec=i / 30.0)

    summary = stats.summary()

    assert stats.stored_samples == 100
    assert (summary.infer_ms_p50, summary.infer_ms_p95, summary.infer_ms_max) == (50.0, 95.0, 100.0)


def test_the_sampling_is_repeatable():
    def run() -> float:
        stats = StatsReporter(start_sec=0.0, reservoir_size=50)
        for i in range(5_000):
            stats.record(infer_ms=float((i * 37) % 101), hand_count=0, now_sec=i / 30.0)
        return stats.summary().infer_ms_p95

    assert run() == run()
