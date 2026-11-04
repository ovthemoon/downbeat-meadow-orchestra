import csv
import json
from datetime import datetime, timedelta, timezone

import pytest

from conftest import TRACKER, UNITY, run_for
from downbeat_supervisor.config import ConfigError, load_config
from downbeat_supervisor.events import EventLog


def read_rows(path):
    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.reader(handle))


def test_events_csv_rows(world, make, tmp_path):
    supervisor = make()
    supervisor.step()
    world.of("tracker")[0].die(3)
    run_for(world, supervisor, 5)

    files = sorted((tmp_path / "logs").glob("supervisor-*.csv"))
    assert len(files) == 1
    rows = read_rows(files[0])
    assert rows[0] == ["timestamp_utc", "process", "event", "detail"]
    by_event = [(r[1], r[2]) for r in rows[1:]]
    assert ("unity", "start") in by_event
    assert ("tracker", "start") in by_event
    assert ("tracker", "exit") in by_event
    exit_row = next(r for r in rows[1:] if r[2] == "exit")
    assert "code=3" in exit_row[3]
    assert exit_row[0].endswith("Z") and exit_row[0].startswith("2026-10-30T01:00:")


def test_the_file_name_is_the_local_date_and_rows_are_appended_with_one_header(tmp_path):
    now = {"t": datetime(2026, 10, 30, 1, 0, 0, tzinfo=timezone.utc)}
    log = EventLog(str(tmp_path), lambda: now["t"])
    log.log("unity", "start", "pid=1")
    again = EventLog(str(tmp_path), lambda: now["t"])    # the supervisor was started again the same day
    again.log("unity", "exit", "code=0")

    files = list(tmp_path.glob("supervisor-*.csv"))
    assert len(files) == 1
    local = now["t"].astimezone().strftime("%Y%m%d")
    assert files[0].name == f"supervisor-{local}.csv"
    rows = read_rows(files[0])
    assert [r[2] for r in rows] == ["event", "start", "exit"]


def test_a_new_local_date_starts_a_new_file(tmp_path):
    midnight = datetime(2026, 10, 30, 0, 0, 0).astimezone()            # a local midnight
    now = {"t": (midnight - timedelta(seconds=30)).astimezone(timezone.utc)}
    log = EventLog(str(tmp_path), lambda: now["t"])
    log.log("unity", "start", "a")
    now["t"] = (midnight + timedelta(seconds=30)).astimezone(timezone.utc)
    log.log("unity", "start", "b")

    assert len(list(tmp_path.glob("supervisor-*.csv"))) == 2


def test_commas_quotes_and_newlines_in_a_detail_stay_in_their_cell(tmp_path):
    log = EventLog(str(tmp_path), lambda: datetime(2026, 10, 30, 1, 0, 0, tzinfo=timezone.utc))
    log.log("tracker", "start_failed", 'a, "b"\nc')

    rows = read_rows(next(tmp_path.glob("supervisor-*.csv")))
    assert rows[1][3] == 'a, "b"\nc'
    assert len(rows[1]) == 4


def test_an_unwritable_log_folder_does_not_stop_the_supervisor(tmp_path):
    blocker = tmp_path / "a-file"
    blocker.write_text("x")
    log = EventLog(str(blocker / "logs"), lambda: datetime(2026, 10, 30, 1, 0, 0, tzinfo=timezone.utc))

    log.log("unity", "start", "pid=1")      # must not raise

    assert log.rows[0][2] == "start", "the rows are still kept in memory"
    assert log.write_failures == 1


def write_config(tmp_path, **changes):
    config = {
        "restartDelaySec": 3, "heartbeatTimeoutSec": 30, "heartbeatGraceSec": 60, "audioStallRestartSec": 20,
        "backoff": {"maxRestarts": 5, "windowSec": 600, "restSec": 60}, "pollSec": 1, "logsDir": "{root}/logs",
        "vars": {"camera": "0"},
        "processes": [
            {"name": "unity", "command": ["{root}/Downbeat.exe", "--input", "hand"], "cwd": "{root}", "heartbeatFile": "{root}/logs/heartbeat.txt"},
            {"name": "tracker", "command": ["{root}/py.exe", "-m", "downbeat_tracker", "--camera", "{camera}"], "cwd": "{root}/tracker"},
        ],
    }
    config.update(changes)
    path = tmp_path / "config.json"
    path.write_text(json.dumps(config), encoding="utf-8")
    return str(path)


def test_the_config_fills_in_the_root_and_the_variables(tmp_path):
    settings = load_config(write_config(tmp_path), root="C:/play", variables={"camera": "1"})

    unity, tracker = settings.processes
    assert unity.command == ("C:/play/Downbeat.exe", "--input", "hand")
    assert unity.heartbeat_file == "C:/play/logs/heartbeat.txt"
    assert tracker.command[-1] == "1"
    assert tracker.cwd == "C:/play/tracker"
    assert settings.logs_dir == "C:/play/logs"
    assert settings.restart_delay_sec == 3.0 and settings.backoff_max_restarts == 5


@pytest.mark.parametrize("change", [
    {"restartDelaySec": -1}, {"heartbeatTimeoutSec": 0}, {"backoff": {"maxRestarts": 0, "windowSec": 600, "restSec": 60}},
    {"processes": []}, {"processes": [{"name": "x"}]}, {"processes": [{"name": "x", "command": []}]},
    {"processes": [{"name": "a", "command": ["x"]}, {"name": "a", "command": ["y"]}]},
])
def test_a_nonsense_config_is_refused_with_a_reason(tmp_path, change):
    with pytest.raises(ConfigError):
        load_config(write_config(tmp_path, **change), root="C:/play", variables={})


def test_a_variable_that_is_not_defined_is_refused(tmp_path):
    with pytest.raises(ConfigError, match="camera"):
        load_config(write_config(tmp_path, vars={}), root="C:/play", variables={})


def test_a_config_that_is_not_json_is_refused(tmp_path):
    path = tmp_path / "bad.json"
    path.write_text("{ nope", encoding="utf-8")
    with pytest.raises(ConfigError):
        load_config(str(path), root="C:/play", variables={})


def test_a_process_with_a_when_that_does_not_match_is_left_out(tmp_path):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["processes"][1]["when"] = {"var": "input", "equals": "hand"}
    data["vars"]["input"] = "hand"
    open(config, "w", encoding="utf-8").write(json.dumps(data))

    with_hands = load_config(config, root="C:/play", variables={})
    with_mouse = load_config(config, root="C:/play", variables={"input": "mouse"})

    assert [p.name for p in with_hands.processes] == ["unity", "tracker"]
    assert [p.name for p in with_mouse.processes] == ["unity"], "no hand tracker when the mouse plays"


def test_a_malformed_when_is_refused(tmp_path):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["processes"][1]["when"] = {"var": "input"}
    open(config, "w", encoding="utf-8").write(json.dumps(data))

    with pytest.raises(ConfigError, match="when"):
        load_config(config, root="C:/play", variables={})


def test_a_variable_can_be_built_on_the_root(tmp_path):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["vars"]["tracker"] = "{root}/../../tracker"
    data["processes"][1]["command"] = ["{tracker}/py", "-m", "downbeat_tracker"]
    open(config, "w", encoding="utf-8").write(json.dumps(data))

    settings = load_config(config, root="/builds/mac", variables={})

    assert settings.processes[1].command[0] == "/builds/mac/../../tracker/py"


def test_a_word_of_a_command_can_depend_on_a_variable(tmp_path):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["vars"]["soak"] = "0"
    data["processes"][0]["command"] = ["{root}/Downbeat.exe", "--input", "hand", {"word": "--soak", "when": {"var": "soak", "equals": "1"}}]
    open(config, "w", encoding="utf-8").write(json.dumps(data))

    normal = load_config(config, root="C:/play", variables={})
    soak = load_config(config, root="C:/play", variables={"soak": "1"})

    assert "--soak" not in normal.processes[0].command
    assert soak.processes[0].command[-1] == "--soak"


def test_a_malformed_word_is_refused(tmp_path):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["processes"][0]["command"] = ["x", {"word": "--soak"}]
    open(config, "w", encoding="utf-8").write(json.dumps(data))

    with pytest.raises(ConfigError, match="word"):
        load_config(config, root="C:/play", variables={})


QUALITY_WORDS = [
    {"words": ["--quality", "{quality}"], "when": {"var": "quality", "equals": "High"}},
    {"words": ["--quality", "{quality}"], "when": {"var": "quality", "equals": "Low"}},
]


def config_with_quality_words(tmp_path):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["vars"]["quality"] = ""
    data["processes"][0]["command"] = ["{root}/Downbeat.exe", "--input", "hand", *QUALITY_WORDS]
    open(config, "w", encoding="utf-8").write(json.dumps(data))
    return config


@pytest.mark.parametrize("quality, expected_tail", [
    ("", ("--input", "hand")),
    ("High", ("--input", "hand", "--quality", "High")),
    ("Low", ("--input", "hand", "--quality", "Low")),
    ("low", ("--input", "hand")),       # the app matches tier names exactly: a wrong case passes nothing instead of a word the app would not know
])
def test_a_group_of_words_is_there_only_when_its_condition_holds(tmp_path, quality, expected_tail):
    config = config_with_quality_words(tmp_path)

    settings = load_config(config, root="C:/play", variables={"quality": quality})

    assert settings.processes[0].command[1:] == expected_tail


@pytest.mark.parametrize("item", [
    {"words": [], "when": {"var": "quality", "equals": "Low"}},
    {"words": "--quality", "when": {"var": "quality", "equals": "Low"}},
    {"words": ["--quality", 3], "when": {"var": "quality", "equals": "Low"}},
    {"words": ["--quality", "Low"]},
    {"word": "--quality", "words": ["Low"], "when": {"var": "quality", "equals": "Low"}},
])
def test_a_malformed_group_of_words_is_refused(tmp_path, item):
    config = write_config(tmp_path)
    data = json.loads(open(config, encoding="utf-8").read())
    data["processes"][0]["command"] = ["x", item]
    open(config, "w", encoding="utf-8").write(json.dumps(data))

    with pytest.raises(ConfigError, match="word"):
        load_config(config, root="C:/play", variables={})
