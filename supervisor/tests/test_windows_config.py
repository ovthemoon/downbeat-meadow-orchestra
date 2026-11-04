"""The configuration that ships to Windows (configs/windows.json), loaded the way tools/windows/run-supervised.ps1 loads it."""
from pathlib import Path

import pytest

from downbeat_supervisor.config import ConfigError, load_config

WINDOWS_CONFIG = str(Path(__file__).resolve().parent.parent / "configs" / "windows.json")
# run-supervised.ps1 passes the python.exe of %LOCALAPPDATA%\Downbeat\venv: the environment is not inside the bundle (a Desktop that OneDrive syncs).
TRACKER_PYTHON = "C:/Users/me/AppData/Local/Downbeat/venv/Scripts/python.exe"


def unity_command(**variables: str) -> tuple[str, ...]:
    settings = load_config(WINDOWS_CONFIG, root="C:/app", variables={"trackerDir": "C:/bundle/tracker", "trackerPython": TRACKER_PYTHON, **variables})
    return next(process.command for process in settings.processes if process.name == "unity")


def tracker_command(**variables: str) -> tuple[str, ...]:
    settings = load_config(WINDOWS_CONFIG, root="C:/app", variables={"trackerDir": "C:/bundle/tracker", "trackerPython": TRACKER_PYTHON, **variables})
    return next(process.command for process in settings.processes if process.name == "tracker")


def test_without_a_quality_the_player_gets_none_and_uses_its_own_default():
    assert "--quality" not in unity_command()


@pytest.mark.parametrize("tier", ["Auto", "High", "Low"])
def test_the_quality_tier_reaches_the_player_in_exactly_the_case_the_app_matches(tier):
    command = unity_command(quality=tier)

    assert command[command.index("--quality") + 1] == tier
    assert command.count("--quality") == 1


def test_a_tier_in_the_wrong_case_passes_nothing():
    assert "--quality" not in unity_command(quality="low")


def test_the_quality_does_not_disturb_the_other_player_words():
    command = unity_command(quality="Low", soak="1", input="replay")

    assert command[0] == "C:/app/Downbeat.exe"
    assert command[command.index("--input") + 1] == "replay"
    assert "--soak" in command and "--health-dir" in command


def test_the_tracker_gets_the_camera_backend_and_a_log_folder_inside_the_tracker():
    command = tracker_command(backend="msmf", camera="1")

    assert command[command.index("--backend") + 1] == "msmf"
    assert command[command.index("--camera") + 1] == "1"
    assert command[command.index("--log") + 1] == "C:/bundle/tracker/logs/tracker-supervised.log"
    assert "--quality" not in command, "the tier is the player's, not the tracker's"


def test_the_tracker_runs_with_the_python_it_is_given_and_not_one_inside_the_bundle():
    command = tracker_command()

    assert command[:3] == (TRACKER_PYTHON, "-m", "downbeat_tracker")
    assert ".venv" not in " ".join(command)


def test_the_tracker_python_has_no_default_so_a_missing_one_is_an_error_and_not_the_old_place():
    with pytest.raises(ConfigError, match="trackerPython"):
        load_config(WINDOWS_CONFIG, root="C:/app", variables={"trackerDir": "C:/bundle/tracker"})


def test_a_run_without_the_tracker_needs_no_tracker_python():
    settings = load_config(WINDOWS_CONFIG, root="C:/app", variables={"trackerDir": "C:/bundle/tracker", "input": "mouse"})

    assert [p.name for p in settings.processes] == ["unity"]


def test_without_the_latency_variable_the_player_gets_no_latency_mode():
    assert "--latency-mode" not in unity_command()
    assert "--latency-mode" not in unity_command(latency="0")


def test_the_latency_variable_passes_latency_mode_to_the_player_only():
    command = unity_command(latency="1")

    assert command.count("--latency-mode") == 1
    assert "--latency-mode" not in tracker_command(latency="1"), "the marker is the player's"


def test_latency_mode_does_not_disturb_the_other_player_words():
    command = unity_command(latency="1", quality="Low", input="hand")

    assert command[command.index("--quality") + 1] == "Low"
    assert command[command.index("--input") + 1] == "hand"
    assert "--health-dir" in command


def test_without_the_log_pose_variable_the_player_gets_no_log_pose():
    assert "--log-pose" not in unity_command()
    assert "--log-pose" not in unity_command(logPose="0")


def test_the_log_pose_variable_passes_log_pose_to_the_player_only():
    command = unity_command(logPose="1")

    assert command.count("--log-pose") == 1
    assert "--log-pose" not in tracker_command(logPose="1"), "the flag is the player's"


def test_log_pose_does_not_disturb_the_other_player_words():
    command = unity_command(logPose="1", latency="1", quality="Low", input="hand")

    assert command[command.index("--quality") + 1] == "Low"
    assert command[command.index("--input") + 1] == "hand"
    assert "--latency-mode" in command and "--health-dir" in command


MAC_CONFIG = str(Path(__file__).resolve().parent.parent / "configs" / "mac.json")


def load_for(config: str, **variables: str):
    return load_config(config, root="C:/app" if config == WINDOWS_CONFIG else "/app/builds/mac", variables={"trackerDir": "C:/bundle/tracker", "trackerPython": TRACKER_PYTHON, "tracker": "/app/tracker", **variables})


@pytest.mark.parametrize("config", [WINDOWS_CONFIG, MAC_CONFIG])
def test_the_tracker_is_watched_by_a_heartbeat_the_tracker_itself_writes(config):
    tracker = next(p for p in load_for(config).processes if p.name == "tracker")

    assert tracker.heartbeat_file, "a read() of the camera that never returns must be seen"
    assert tracker.command[tracker.command.index("--heartbeat") + 1] == tracker.heartbeat_file, "the file the supervisor reads is the file the tracker is told to write"
    assert tracker.heartbeat_timeout_sec is not None and tracker.heartbeat_timeout_sec >= 15, "long enough for a slow camera open, short enough to matter"


@pytest.mark.parametrize("config", [WINDOWS_CONFIG, MAC_CONFIG])
def test_the_player_is_watched_by_its_heartbeat_in_the_folder_the_supervisor_reads(config):
    settings = load_for(config)
    unity = next(p for p in settings.processes if p.name == "unity")

    assert unity.heartbeat_file.endswith("/logs/heartbeat.txt")
    assert unity.command[unity.command.index("--health-dir") + 1] == unity.heartbeat_file.rsplit("/", 1)[0], "the player writes where the supervisor reads"


def test_the_windows_grace_period_leaves_room_for_a_slower_laptop():
    settings = load_for(WINDOWS_CONFIG)

    assert settings.heartbeat_grace_sec >= 180


def test_the_windows_tracker_gets_the_media_foundation_workaround_in_its_environment():
    tracker = next(p for p in load_for(WINDOWS_CONFIG).processes if p.name == "tracker")

    assert tracker.env["OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS"] == "0"
    assert tracker.env["PYTHONUTF8"] == "1"


def test_the_escalation_of_the_rests_is_set_for_the_exhibition_pc():
    settings = load_for(WINDOWS_CONFIG)

    assert (settings.backoff_unstable_runs, settings.backoff_rest_sec, settings.backoff_max_rest_sec, settings.stable_after_sec) == (3, 60.0, 600.0, 120.0)


@pytest.mark.parametrize("mode", ["conduct", "strike"])
def test_the_play_mode_reaches_the_player_in_exactly_the_case_the_app_matches(mode):
    command = unity_command(playMode=mode)

    assert command[command.index("--play-mode") + 1] == mode
    assert command.count("--play-mode") == 1


def test_without_a_play_mode_the_player_gets_none_and_uses_the_mode_of_its_content():
    assert "--play-mode" not in unity_command()
    assert "--play-mode" not in unity_command(playMode="dancing")


def test_the_play_mode_is_the_players_and_not_the_trackers():
    assert "--play-mode" not in tracker_command(playMode="strike")


def test_the_tracker_runs_with_a_higher_priority_and_the_player_with_the_normal_one():
    settings = load_for(WINDOWS_CONFIG)
    by_name = {p.name: p for p in settings.processes}

    assert by_name["tracker"].priority == "aboveNormal", "a busy app must not starve the hand tracker"
    assert by_name["unity"].priority is None


def test_a_frame_cap_reaches_the_player_only_when_one_is_given():
    assert "--max-fps" not in unity_command()
    command = unity_command(maxFps="30")

    assert command[command.index("--max-fps") + 1] == "30"
    assert "--max-fps" not in tracker_command(maxFps="30"), "the cap is the player's"
