"""--save-video: the camera picture, mirrored and at a constant rate, with the hand points when asked, into a local file."""

from __future__ import annotations

import logging

import numpy as np
import pytest

from downbeat_tracker.config import parse_args
from downbeat_tracker.protocol import LANDMARKS_PER_HAND, Hand
from downbeat_tracker.video_record import VideoRecorder, chain, open_cv_writer


def test_saving_is_off_unless_asked():
    config = parse_args([])

    assert config.save_video is None and config.save_video_points is False and config.stop_file is None


def test_the_options_reach_the_config(tmp_path):
    config = parse_args(["--save-video", str(tmp_path / "me.mp4"), "--save-video-points", "--stop-file", str(tmp_path / "stop")])

    assert config.save_video == tmp_path / "me.mp4"
    assert config.save_video_points is True
    assert config.stop_file == tmp_path / "stop"


def test_points_without_a_file_are_refused():
    with pytest.raises(SystemExit):
        parse_args(["--save-video-points"])


class FakeWriter:
    def __init__(self) -> None:
        self.pictures: list[np.ndarray] = []
        self.released = 0

    def write(self, picture: np.ndarray) -> None:
        self.pictures.append(picture.copy())

    def release(self) -> None:
        self.released += 1


class Times:
    """A clock that answers the times it is given, one per call."""

    def __init__(self, *times: float) -> None:
        self._times = list(times)

    def __call__(self) -> float:
        return self._times.pop(0)


def recorder(tmp_path, clock, fps=10.0, draw_points=False, factory=None):
    writer = FakeWriter()
    opened = []

    def default_factory(path, fps_, width, height):
        opened.append((path, fps_, width, height))
        return writer

    made = VideoRecorder(tmp_path / "out.mp4", fps, draw_points=draw_points, clock=clock, writer_factory=factory or default_factory)
    return made, writer, opened


def picture(value: int, width: int = 8, height: int = 6) -> np.ndarray:
    frame = np.full((height, width, 3), value, dtype=np.uint8)
    frame[:, 0] = 255   # the left column is white, so mirroring shows
    return frame


def test_pictures_are_mirrored_and_written_once_each_at_the_frame_rate(tmp_path):
    made, writer, opened = recorder(tmp_path, Times(0.0, 0.1, 0.2))

    for value in (10, 20, 30):
        assert made(picture(value), ()) is False

    assert opened == [(tmp_path / "out.mp4", 10.0, 8, 6)]
    assert [int(p[0, 0, 0]) for p in writer.pictures] == [10, 20, 30]
    assert all((p[:, -1] == 255).all() and (p[:, 0] != 255).all() for p in writer.pictures), "the white column moved to the right: mirrored"
    assert made.frames_written == 3 and made.pictures_seen == 3


def test_a_slower_tracker_repeats_the_picture_that_was_there_so_the_video_runs_in_real_time(tmp_path):
    made, writer, _ = recorder(tmp_path, Times(0.0, 0.35))

    made(picture(10), ())
    made(picture(20), ())

    # slots at 0.0, 0.1, 0.2 show the first picture, the slot at 0.3 the new one
    assert [int(p[0, 1, 0]) for p in writer.pictures] == [10, 10, 10, 20]


def test_a_picture_that_comes_before_its_slot_waits_for_the_next_one(tmp_path):
    made, writer, _ = recorder(tmp_path, Times(0.0, 0.05, 0.1))

    made(picture(10), ())
    made(picture(20), ())   # 0.05: the next slot (0.1) is not due yet
    made(picture(30), ())   # 0.1: due

    assert [int(p[0, 1, 0]) for p in writer.pictures] == [10, 30]


def test_the_hand_points_are_drawn_where_the_mirrored_hand_is(tmp_path):
    made, writer, _ = recorder(tmp_path, Times(0.0), draw_points=True)
    landmarks = tuple((0.25, 0.5, 0.0) for _ in range(LANDMARKS_PER_HAND))

    made(np.zeros((60, 80, 3), dtype=np.uint8), (Hand(0, 0.9, landmarks),))

    drawn = writer.pictures[0].any(axis=2)
    columns = np.nonzero(drawn.any(axis=0))[0]
    assert columns.size > 0
    assert abs(columns.mean() - 0.75 * 79) < 6, "x 0.25 of the camera is x 0.75 of the mirrored picture"
    assert not drawn[:, :40].any(), "nothing on the other half"


def test_without_points_nothing_is_drawn(tmp_path):
    made, writer, _ = recorder(tmp_path, Times(0.0))
    landmarks = tuple((0.25, 0.5, 0.0) for _ in range(LANDMARKS_PER_HAND))

    made(np.zeros((60, 80, 3), dtype=np.uint8), (Hand(0, 0.9, landmarks),))

    assert not writer.pictures[0].any()


def test_close_releases_the_file_once(tmp_path):
    made, writer, _ = recorder(tmp_path, Times(0.0))
    made(picture(10), ())

    made.close()
    made.close()

    assert writer.released == 1


def test_close_before_any_picture_does_nothing(tmp_path):
    made, writer, opened = recorder(tmp_path, Times())

    made.close()

    assert opened == [] and writer.released == 0


def test_a_file_that_cannot_be_written_stops_the_saving_but_not_the_tracker(tmp_path, caplog):
    def refusing(path, fps, width, height):
        raise OSError("no room")

    made, _, _ = recorder(tmp_path, Times(0.0, 0.1), factory=refusing)
    with caplog.at_level(logging.ERROR):
        assert made(picture(10), ()) is False
        assert made(picture(20), ()) is False

    assert "not saving the camera video" in caplog.text
    assert made.frames_written == 0


def test_a_picture_of_another_size_is_left_out(tmp_path):
    made, writer, _ = recorder(tmp_path, Times(0.0, 0.1))

    made(picture(10), ())
    made(picture(20, width=16, height=12), ())

    assert len(writer.pictures) == 1 and made.pictures_seen == 1


def test_chain_calls_every_callback_and_stops_when_any_of_them_asks():
    calls = []

    def keep_going(bgr, hands):
        calls.append("a")
        return False

    def stop(bgr, hands):
        calls.append("b")
        return True

    assert chain([]) is None
    assert chain([keep_going]) is keep_going
    assert chain([stop, keep_going])(None, ()) is True
    assert calls == ["b", "a"], "the one after the stop is still called"


def test_a_real_file_can_be_read_back(tmp_path):
    cv2 = pytest.importorskip("cv2")
    path = tmp_path / "camera.mp4"
    made = VideoRecorder(path, 10.0, clock=Times(*(i / 10 for i in range(10))), writer_factory=open_cv_writer)

    for i in range(10):
        made(np.full((48, 64, 3), i * 20, dtype=np.uint8), ())
    made.close()

    capture = cv2.VideoCapture(str(path))
    frames = 0
    while capture.read()[0]:
        frames += 1
    capture.release()
    assert frames == 10
