"""The values of the configuration file: what is read, what the defaults are and what is refused."""
import json

import pytest

from downbeat_supervisor.config import ConfigError, load_config


def write(tmp_path, **overrides) -> str:
    data = {
        "backoff": {"maxRestarts": 5, "windowSec": 600, "restSec": 60},
        "processes": [{"name": "unity", "command": ["Downbeat.exe"], "heartbeatFile": "{root}/hb.txt"}],
    }
    data.update(overrides)
    path = tmp_path / "config.json"
    path.write_text(json.dumps(data), encoding="utf-8")
    return str(path)


def test_the_escalation_has_defaults_that_are_written_down(tmp_path):
    settings = load_config(write(tmp_path), "/r", {})

    assert settings.backoff_unstable_runs == 3
    assert settings.backoff_max_rest_sec == 600.0
    assert settings.stable_after_sec == 120.0


def test_the_escalation_values_are_read(tmp_path):
    backoff = {"maxRestarts": 5, "windowSec": 600, "restSec": 30, "unstableRuns": 4, "maxRestSec": 300, "stableAfterSec": 90}
    settings = load_config(write(tmp_path, backoff=backoff), "/r", {})

    assert (settings.backoff_unstable_runs, settings.backoff_max_rest_sec, settings.stable_after_sec) == (4, 300.0, 90.0)


@pytest.mark.parametrize(
    ("key", "value", "message"),
    [("unstableRuns", 0, "unstableRuns"), ("unstableRuns", 2.5, "unstableRuns"), ("maxRestSec", 10, "maxRestSec"), ("stableAfterSec", 0, "stableAfterSec")],
)
def test_nonsense_escalation_values_are_refused_with_their_name(tmp_path, key, value, message):
    backoff = {"maxRestarts": 5, "windowSec": 600, "restSec": 60, key: value}

    with pytest.raises(ConfigError, match=message):
        load_config(write(tmp_path, backoff=backoff), "/r", {})


def test_a_process_can_have_a_heartbeat_timeout_of_its_own(tmp_path):
    processes = [
        {"name": "tracker", "command": ["python"], "heartbeatFile": "{root}/t.txt", "heartbeatTimeoutSec": 45},
        {"name": "unity", "command": ["Downbeat.exe"], "heartbeatFile": "{root}/hb.txt"},
    ]
    settings = load_config(write(tmp_path, processes=processes), "/r", {})

    assert [p.heartbeat_timeout_sec for p in settings.processes] == [45.0, None]


def test_a_heartbeat_timeout_that_is_not_a_positive_number_is_refused(tmp_path):
    processes = [{"name": "tracker", "command": ["python"], "heartbeatFile": "{root}/t.txt", "heartbeatTimeoutSec": 0}]

    with pytest.raises(ConfigError, match="heartbeatTimeoutSec"):
        load_config(write(tmp_path, processes=processes), "/r", {})


def test_a_priority_other_than_above_normal_is_refused(tmp_path):
    processes = [{"name": "tracker", "command": ["python"], "priority": "realtime"}]

    with pytest.raises(ConfigError, match="priority"):
        load_config(write(tmp_path, processes=processes), "/r", {})


def test_the_priority_is_read(tmp_path):
    processes = [{"name": "tracker", "command": ["python"], "priority": "aboveNormal"}, {"name": "unity", "command": ["Downbeat.exe"]}]
    settings = load_config(write(tmp_path, processes=processes), "/r", {})

    assert [p.priority for p in settings.processes] == ["aboveNormal", None]


def _words(tmp_path, when, **variables):
    command = ["Downbeat.exe", {"word": "--flag", "when": when}]
    processes = [{"name": "unity", "command": command}]
    return load_config(write(tmp_path, processes=processes, vars=variables), "/r", {}).processes[0].command


def test_a_word_can_be_left_out_unless_a_variable_has_a_value(tmp_path):
    assert _words(tmp_path, {"var": "cap", "notEquals": ""}, cap="") == ("Downbeat.exe",)
    assert _words(tmp_path, {"var": "cap", "notEquals": ""}, cap="30") == ("Downbeat.exe", "--flag")
    assert _words(tmp_path, {"var": "cap", "notEquals": ""}) == ("Downbeat.exe", "--flag"), "a variable that was never given is not the empty string either"


def test_a_when_needs_exactly_one_of_equals_and_not_equals(tmp_path):
    with pytest.raises(ConfigError, match="equals"):
        _words(tmp_path, {"var": "cap", "equals": "1", "notEquals": "2"})
    with pytest.raises(ConfigError, match="equals"):
        _words(tmp_path, {"var": "cap"})
