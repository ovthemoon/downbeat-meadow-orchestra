"""The configuration file of the supervisor: which processes to run and how patiently to watch them. Nothing is guessed: a value that makes no sense is refused with its name."""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from typing import Any, Mapping


class ConfigError(ValueError):
    """The configuration cannot be used; the message says which value and why."""


@dataclass(frozen=True)
class ProcessSpec:
    name: str
    command: tuple[str, ...]
    cwd: str | None
    heartbeat_file: str | None
    env: Mapping[str, str] = field(default_factory=dict)
    heartbeat_timeout_sec: float | None = None   # how old this process's beat may get; None: the global heartbeatTimeoutSec
    priority: str | None = None                  # "aboveNormal": on Windows the process starts with a higher priority class (no administrator rights are needed); None: the normal one


@dataclass(frozen=True)
class Settings:
    restart_delay_sec: float
    heartbeat_timeout_sec: float
    heartbeat_grace_sec: float
    audio_stall_restart_sec: float
    backoff_max_restarts: int
    backoff_window_sec: float
    backoff_rest_sec: float
    poll_sec: float
    logs_dir: str
    processes: tuple[ProcessSpec, ...]
    backoff_unstable_runs: int = 3        # from this many starts in a row that never stayed healthy the rests grow (see backoff.* in the configuration files)
    backoff_max_rest_sec: float = 600.0   # the largest rest
    stable_after_sec: float = 120.0       # a start that was healthy for this long is stable: it ends the count


_PLACEHOLDER = re.compile(r"\{(\w+)\}")


def _fill(text: str, values: Mapping[str, str], where: str) -> str:
    def replace(match: re.Match[str]) -> str:
        name = match.group(1)
        if name not in values:
            raise ConfigError(f"{where}: the variable {{{name}}} is not defined (give it in \"vars\" or with --var {name}=...)")
        return values[name]

    return _PLACEHOLDER.sub(replace, text)


def _conditional_words(item: Any, values: Mapping[str, str], where: str) -> list[str]:
    """One conditional entry of a command: {"word": "--soak", "when": ...} or a group {"words": ["--quality", "{quality}"], "when": ...}.
    Its words are only there when the variable of `when` has exactly that value."""
    shape = f"{where}: a word is a string, {{\"word\": ..., \"when\": ...}} or {{\"words\": [...], \"when\": ...}}, where when is {{\"var\": ..., \"equals\": ...}} (or \"notEquals\")"
    if not isinstance(item, dict) or not isinstance(item.get("when"), dict) or ("word" in item) == ("words" in item):
        raise ConfigError(shape)
    if "word" in item:
        texts = [item["word"]]
    else:
        texts = item["words"]
        if not isinstance(texts, list) or not texts:
            raise ConfigError(f"{where}: words must be a list of strings, not empty")
    if not all(isinstance(text, str) for text in texts):
        raise ConfigError(shape)
    when = item["when"]
    if not isinstance(when.get("var"), str) or ("equals" in when) == ("notEquals" in when) or not isinstance(when.get("equals", when.get("notEquals")), str):
        raise ConfigError(f"{where}: a word's when needs var and either equals or notEquals")
    actual = values.get(when["var"])
    if "equals" in when and actual != when["equals"]:
        return []
    if "notEquals" in when and actual == when["notEquals"]:
        return []
    return [_fill(text, values, where) for text in texts]


def _words(command: Any, values: Mapping[str, str], where: str) -> list[str]:
    """The words of a command. A word is a string, or a conditional word / group of words (see _conditional_words)."""
    if not isinstance(command, list) or not command:
        raise ConfigError(f"{where} must be a list of words, not empty")
    words: list[str] = []
    for item in command:
        if isinstance(item, str):
            words.append(_fill(item, values, where))
        else:
            words.extend(_conditional_words(item, values, where))
    if not words:
        raise ConfigError(f"{where} has no words left")
    return words


def _number(data: Mapping[str, Any], key: str, default: float, minimum: float, *, allow_equal: bool) -> float:
    value = data.get(key, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ConfigError(f"{key} must be a number")
    if value < minimum or (value == minimum and not allow_equal):
        raise ConfigError(f"{key} must be {'at least' if allow_equal else 'more than'} {minimum}, not {value}")
    return float(value)


def load_config(path: str, root: str, variables: Mapping[str, str]) -> Settings:
    """Reads the JSON file. `{root}` and the variables (from the file's "vars" and from `variables`, which win) are filled in everywhere in paths and commands."""
    try:
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, json.JSONDecodeError) as error:
        raise ConfigError(f"{path}: {error}") from error
    if not isinstance(data, dict):
        raise ConfigError("the configuration must be a JSON object")

    values = {str(k): str(v) for k, v in (data.get("vars") or {}).items()}
    values.update({str(k): str(v) for k, v in variables.items()})
    values["root"] = root
    for name in list(values):                       # a variable may build on {root} (the Mac configuration finds the tracker next to the build folder)
        if name != "root":
            values[name] = _fill(values[name], {"root": root}, f"vars.{name}")

    backoff = data.get("backoff") or {}
    max_restarts = backoff.get("maxRestarts", 5)
    if isinstance(max_restarts, bool) or not isinstance(max_restarts, int) or max_restarts < 1:
        raise ConfigError("backoff.maxRestarts must be a whole number of at least 1")

    unstable_runs = backoff.get("unstableRuns", 3)
    if isinstance(unstable_runs, bool) or not isinstance(unstable_runs, int) or unstable_runs < 1:
        raise ConfigError("backoff.unstableRuns must be a whole number of at least 1")

    raw_processes = data.get("processes")  # each may have "when": {"var": "input", "equals": "hand"}
    if not isinstance(raw_processes, list) or not raw_processes:
        raise ConfigError("processes must list at least one process")
    processes: list[ProcessSpec] = []
    names: set[str] = set()
    for index, raw in enumerate(raw_processes):
        where = f"processes[{index}]"
        if not isinstance(raw, dict) or not isinstance(raw.get("name"), str) or not raw["name"]:
            raise ConfigError(f"{where}.name is missing")
        name = raw["name"]
        when = raw.get("when")
        if when is not None:
            if not isinstance(when, dict) or not isinstance(when.get("var"), str) or not isinstance(when.get("equals"), str):
                raise ConfigError(f"{where} ({name}).when must be {{\"var\": name, \"equals\": value}}")
            if values.get(when["var"]) != when["equals"]:
                continue   # this process is only wanted for another value of the variable (the hand tracker is not wanted with --var input=mouse)
        if name in names:
            raise ConfigError(f"{where}: the name '{name}' is used twice")
        names.add(name)
        command = _words(raw.get("command"), values, f"{where} ({name}).command")
        cwd = raw.get("cwd")
        heartbeat = raw.get("heartbeatFile")
        process_timeout = _number(raw, "heartbeatTimeoutSec", 0.0, 0.0, allow_equal=False) if "heartbeatTimeoutSec" in raw else None
        priority = raw.get("priority")
        if priority is not None and priority != "aboveNormal":
            raise ConfigError(f"{where} ({name}).priority must be \"aboveNormal\" or left out, not {priority!r}")
        env = raw.get("env") or {}
        if not isinstance(env, dict):
            raise ConfigError(f"{where} ({name}).env must be an object")
        processes.append(
            ProcessSpec(
                name=name,
                command=tuple(command),
                cwd=_fill(cwd, values, f"{where} ({name}).cwd") if isinstance(cwd, str) else None,
                heartbeat_file=_fill(heartbeat, values, f"{where} ({name}).heartbeatFile") if isinstance(heartbeat, str) else None,
                env={str(k): _fill(str(v), values, f"{where} ({name}).env") for k, v in env.items()},
                heartbeat_timeout_sec=process_timeout,
                priority=priority,
            )
        )

    if not processes:
        raise ConfigError("no process is left for these variables")

    rest_sec = _number(backoff, "restSec", 60.0, 0.0, allow_equal=True)
    max_rest_sec = _number(backoff, "maxRestSec", 600.0, 0.0, allow_equal=True)
    if max_rest_sec < rest_sec:
        raise ConfigError(f"backoff.maxRestSec ({max_rest_sec:g}) must be at least backoff.restSec ({rest_sec:g})")

    return Settings(
        restart_delay_sec=_number(data, "restartDelaySec", 3.0, 0.0, allow_equal=True),
        heartbeat_timeout_sec=_number(data, "heartbeatTimeoutSec", 30.0, 0.0, allow_equal=False),
        heartbeat_grace_sec=_number(data, "heartbeatGraceSec", 60.0, 0.0, allow_equal=True),
        audio_stall_restart_sec=_number(data, "audioStallRestartSec", 20.0, 0.0, allow_equal=False),
        backoff_max_restarts=max_restarts,
        backoff_window_sec=_number(backoff, "windowSec", 600.0, 0.0, allow_equal=False),
        backoff_rest_sec=rest_sec,
        poll_sec=_number(data, "pollSec", 1.0, 0.0, allow_equal=False),
        logs_dir=_fill(str(data.get("logsDir", "{root}/logs")), values, "logsDir"),
        processes=tuple(processes),
        backoff_unstable_runs=unstable_runs,
        backoff_max_rest_sec=max_rest_sec,
        stable_after_sec=_number(backoff, "stableAfterSec", 120.0, 0.0, allow_equal=False),
    )
