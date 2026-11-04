"""The log file of the tracker: the folder it lives in is made when it is missing (a freshly unzipped bundle has no tracker/logs)."""

from __future__ import annotations

import logging

import pytest

from downbeat_tracker.__main__ import configure_logging
from downbeat_tracker.config import parse_args


@pytest.fixture(autouse=True)
def keep_root_logger_untouched():
    """configure_logging replaces the root handlers; give the test runner its own back afterwards."""
    root = logging.getLogger()
    saved_handlers, saved_level = list(root.handlers), root.level
    yield
    for handler in list(root.handlers):
        root.removeHandler(handler)
        handler.close()
    for handler in saved_handlers:
        root.addHandler(handler)
    root.setLevel(saved_level)


def test_a_log_path_in_a_missing_folder_is_created(tmp_path):
    log_path = tmp_path / "logs" / "nested" / "tracker-supervised.log"
    config = parse_args(["--log", str(log_path)])

    configure_logging(config)
    logging.getLogger("downbeat_tracker").info("hello from the test")
    for handler in logging.getLogger().handlers:
        handler.flush()

    assert log_path.parent.is_dir()
    assert "hello from the test" in log_path.read_text(encoding="utf-8")


def test_a_log_path_in_an_existing_folder_still_works(tmp_path):
    log_path = tmp_path / "tracker.log"
    config = parse_args(["--log", str(log_path)])

    configure_logging(config)
    logging.getLogger("downbeat_tracker").info("second line")
    for handler in logging.getLogger().handlers:
        handler.flush()

    assert "second line" in log_path.read_text(encoding="utf-8")


def test_without_a_log_path_the_log_goes_to_the_console():
    config = parse_args([])

    configure_logging(config)

    handlers = logging.getLogger().handlers
    assert len(handlers) == 1 and not isinstance(handlers[0], logging.FileHandler)


def test_the_log_file_is_rotated_so_a_night_cannot_fill_the_disk(tmp_path):
    from logging.handlers import RotatingFileHandler

    from downbeat_tracker.__main__ import LOG_BACKUPS, LOG_MAX_BYTES, make_log_handler

    handler = make_log_handler(tmp_path / "tracker.log")
    try:
        assert isinstance(handler, RotatingFileHandler)
        assert handler.maxBytes == LOG_MAX_BYTES and handler.backupCount == LOG_BACKUPS
        assert LOG_MAX_BYTES * (LOG_BACKUPS + 1) <= 40 * 1024 * 1024, "all generations together stay small"
    finally:
        handler.close()


def test_rotation_moves_the_old_lines_to_a_numbered_file_and_keeps_writing(tmp_path):
    from downbeat_tracker.__main__ import make_log_handler

    handler = make_log_handler(tmp_path / "tracker.log", max_bytes=2_000, backups=2)
    logger = logging.getLogger("rotation-test")
    logger.setLevel(logging.INFO)
    logger.addHandler(handler)
    try:
        for i in range(200):
            logger.info("line %03d %s", i, "x" * 50)
    finally:
        logger.removeHandler(handler)
        handler.close()

    assert (tmp_path / "tracker.log.1").exists()
    assert not (tmp_path / "tracker.log.3").exists(), "two generations are kept"
    assert "line 199" in (tmp_path / "tracker.log").read_text(encoding="utf-8")


def test_a_rotation_that_cannot_happen_does_not_lose_the_log(tmp_path, monkeypatch):
    """On Windows a log that another program has open cannot be renamed; the tracker then goes on writing to the same file."""
    import os

    from downbeat_tracker.__main__ import make_log_handler

    handler = make_log_handler(tmp_path / "tracker.log", max_bytes=500, backups=1)
    real_rename = os.rename

    def refuse(source, destination):
        raise PermissionError("in use")

    monkeypatch.setattr(os, "rename", refuse)
    monkeypatch.setattr(os, "replace", refuse)
    logger = logging.getLogger("rotation-refused-test")
    logger.setLevel(logging.INFO)
    logger.addHandler(handler)
    try:
        for i in range(60):
            logger.info("line %03d %s", i, "y" * 40)
    finally:
        logger.removeHandler(handler)
        handler.close()
        monkeypatch.setattr(os, "rename", real_rename)

    text = (tmp_path / "tracker.log").read_text(encoding="utf-8")
    assert "line 000" in text and "line 059" in text, "nothing was lost"


def test_a_refused_rotation_is_tried_again_at_most_once_a_minute(tmp_path, monkeypatch):
    import os

    from downbeat_tracker.__main__ import ROLLOVER_RETRY_SEC, make_log_handler

    now = {"t": 1000.0}
    handler = make_log_handler(tmp_path / "tracker.log", max_bytes=500, backups=1, clock=lambda: now["t"])
    attempts = {"n": 0}

    def refuse(source, destination):
        attempts["n"] += 1
        raise PermissionError("in use")

    monkeypatch.setattr(os, "rename", refuse)
    monkeypatch.setattr(os, "replace", refuse)
    logger = logging.getLogger("rotation-backoff-test")
    logger.setLevel(logging.INFO)
    logger.addHandler(handler)
    try:
        for i in range(200):
            logger.info("line %03d %s", i, "z" * 40)
        assert attempts["n"] == 1, "one try, then it leaves the file alone instead of trying on every line"

        now["t"] += ROLLOVER_RETRY_SEC + 1
        for i in range(10):
            logger.info("later %03d %s", i, "z" * 40)
        assert attempts["n"] == 2, "after the wait it tries once more"
    finally:
        logger.removeHandler(handler)
        handler.close()

    text = (tmp_path / "tracker.log").read_text(encoding="utf-8")
    assert "line 000" in text and "later 009" in text, "and no line was lost"
