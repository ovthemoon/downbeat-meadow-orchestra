"""The tracker's own heartbeat file: it is written from the frame loop, so a camera read that never returns stops it (and the supervisor restarts the tracker)."""

from __future__ import annotations

import logging
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path

import numpy as np
import pytest

from downbeat_tracker.config import TrackerConfig, parse_args
from downbeat_tracker.heartbeat import HEARTBEAT_INTERVAL_SEC, HeartbeatWriter
from downbeat_tracker.landmarker import ModelError
from downbeat_tracker.pipeline import TrackerPipeline
from downbeat_tracker.protocol import TrackerState

from test_pipeline_fakes import BLANK, CollectingSender, FakeCamera, FakeClock, FakeDetector, FakeVideoSource

STAMP = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$")
UTC0 = datetime(2026, 10, 30, 1, 0, 0, tzinfo=timezone.utc)


class RecordingSink:
    """Stands in for the writer in the pipeline tests: remembers what the loop reported and when (on the fake clock)."""

    def __init__(self, clock: FakeClock) -> None:
        self._clock = clock
        self.beats: list[tuple[float, str]] = []

    def beat(self, status: str, now: float) -> None:
        self.beats.append((now, status))


def writer_for(tmp_path, utc_offset=lambda: 0.0, name="tracker-heartbeat.txt") -> HeartbeatWriter:
    return HeartbeatWriter(tmp_path / name, utc_now=lambda: UTC0 + timedelta(seconds=utc_offset()))


def test_the_file_holds_the_utc_time_and_the_state_of_the_camera(tmp_path):
    writer = writer_for(tmp_path)

    writer.beat("ok", now=10.0)

    lines = (tmp_path / "tracker-heartbeat.txt").read_text(encoding="utf-8").splitlines()
    assert STAMP.match(lines[0]), lines[0]
    assert lines[0] == "2026-10-30T01:00:00.000Z"
    assert lines[1] == "camera=ok"


def test_it_is_written_at_most_once_per_interval_but_at_once_when_the_state_changes(tmp_path):
    clock = {"t": 0.0}
    writer = writer_for(tmp_path, utc_offset=lambda: clock["t"])
    path = tmp_path / "tracker-heartbeat.txt"

    writer.beat("ok", now=0.0)
    clock["t"] = HEARTBEAT_INTERVAL_SEC / 2
    writer.beat("ok", now=HEARTBEAT_INTERVAL_SEC / 2)
    assert path.read_text(encoding="utf-8").startswith("2026-10-30T01:00:00.000Z"), "half an interval later: not written again"

    clock["t"] = HEARTBEAT_INTERVAL_SEC / 2 + 0.1
    writer.beat("lost", now=HEARTBEAT_INTERVAL_SEC / 2 + 0.1)
    assert path.read_text(encoding="utf-8").splitlines()[1] == "camera=lost", "a new state is not held back"

    clock["t"] = 10.0
    writer.beat("lost", now=10.0)
    assert path.read_text(encoding="utf-8").startswith("2026-10-30T01:00:10.000Z"), "a full interval later: written"


def test_the_folder_is_made_when_it_is_missing(tmp_path):
    writer = HeartbeatWriter(tmp_path / "logs" / "deeper" / "hb.txt", utc_now=lambda: UTC0)

    writer.beat("ok", now=0.0)

    assert (tmp_path / "logs" / "deeper" / "hb.txt").exists()


def test_a_file_that_cannot_be_written_is_one_warning_and_never_an_error(tmp_path, caplog):
    caplog.set_level(logging.WARNING, logger="downbeat_tracker")
    (tmp_path / "hb.txt").mkdir()                                    # a folder where the file should be
    writer = HeartbeatWriter(tmp_path / "hb.txt", utc_now=lambda: UTC0)

    for i in range(5):
        writer.beat("ok", now=i * 10.0)

    warnings = [r for r in caplog.records if "heartbeat" in r.getMessage()]
    assert len(warnings) == 1, "said once, not every beat"


def test_the_option_names_the_file():
    assert parse_args([]).heartbeat is None
    assert parse_args(["--heartbeat", "logs/hb.txt"]).heartbeat == Path("logs/hb.txt")   # a Path: on Windows its text is logs\hb.txt


# ---- the loop ------------------------------------------------------------------------------------------------------------------------------

def run_with_sink(source, clock, config, factory=None):
    sink = RecordingSink(clock)
    detector = FakeDetector()
    pipeline = TrackerPipeline(config, source, factory or (lambda _c: detector), CollectingSender(), clock, heartbeat=sink)
    pipeline.run()
    return sink


def test_the_loop_beats_while_frames_flow():
    clock = FakeClock()

    sink = run_with_sink(FakeVideoSource(10_000), clock, TrackerConfig(max_fps=30.0, max_seconds=10.0))

    assert len(sink.beats) >= 10 * 30 * 0.9, "at least one report per frame (the writer throttles the file)"
    assert sink.beats[0][1] == "starting", "the first one comes before the model is loaded"
    assert {status for _, status in sink.beats[1:]} == {"ok"}


def test_the_loop_keeps_beating_when_there_is_no_camera_because_the_reopen_loop_is_alive():
    clock = FakeClock()
    camera = FakeCamera(clock, lost_at=clock.t - 1.0, back_at=clock.t + 1000.0)

    sink = run_with_sink(camera, clock, TrackerConfig(max_fps=30.0, max_seconds=10.0))

    lost = [now for now, status in sink.beats if status == "lost"]
    assert len(lost) >= 10, "a report every 50 ms of the reopen loop"
    assert max(b - a for a, b in zip(lost, lost[1:])) <= 1.5, "never a silence of more than a moment"


class BlockedOnce:
    """A camera whose 40th read takes 25 s to come back (Media Foundation after a USB unplug)."""

    is_live = True
    aspect = 1.5
    failure_state = TrackerState.CAMERA_ERROR

    def __init__(self, clock: FakeClock) -> None:
        self._clock = clock
        self._reads = 0

    def open(self) -> bool:
        return True

    def read(self):
        self._reads += 1
        if self._reads == 40:
            self._clock.t += 25.0
        return BLANK

    def rewind(self) -> bool:
        return False

    def close(self) -> None:
        pass


def test_a_read_that_blocks_leaves_a_silence_as_long_as_the_block():
    clock = FakeClock()

    sink = run_with_sink(BlockedOnce(clock), clock, TrackerConfig(max_fps=30.0, max_seconds=40.0))

    times = [now for now, _ in sink.beats]
    gaps = [b - a for a, b in zip(times, times[1:])]
    assert max(gaps) >= 25.0, "the report is made after the read comes back, so the silence is what the supervisor sees"
    assert sorted(gaps)[-2] < 1.0, "and the only long silence is that one"


def test_a_model_that_cannot_load_still_beats_so_the_tracker_is_not_taken_for_hung():
    clock = FakeClock()

    def failing(_config):
        raise ModelError("no model")

    sink = run_with_sink(FakeVideoSource(10), clock, TrackerConfig(max_fps=30.0, max_seconds=3.0), factory=failing)

    assert len(sink.beats) >= 4
    assert sink.beats[0][1] == "starting"
    assert {status for _, status in sink.beats[1:]} == {"model_failed"}


def test_the_loop_beats_once_before_the_model_is_loaded_because_a_first_run_may_download_it():
    clock = FakeClock()
    sink = RecordingSink(clock)
    seen: dict = {}

    def slow_factory(_config):
        seen["beats_before"] = list(sink.beats)      # what the supervisor could already have seen when the download starts
        return FakeDetector()

    pipeline = TrackerPipeline(TrackerConfig(max_fps=30.0, max_seconds=2.0), FakeVideoSource(100), slow_factory, CollectingSender(), clock, heartbeat=sink)
    pipeline.run()

    assert [status for _, status in seen["beats_before"]] == ["starting"], "one beat is already in the file when the (possibly long) model load begins"


def test_the_writer_puts_the_starting_state_in_the_file(tmp_path):
    writer = writer_for(tmp_path)

    writer.beat("starting", now=0.0)

    assert (tmp_path / "tracker-heartbeat.txt").read_text(encoding="utf-8").splitlines()[1] == "camera=starting"
