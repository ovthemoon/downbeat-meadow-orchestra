"""python -m downbeat_supervisor --config PATH [--root PATH] [--var name=value ...] [--dry-run]"""
from __future__ import annotations

import argparse
import json
import os
import signal
import sys
from datetime import datetime, timezone

from .config import ConfigError, load_config
from .events import EventLog
from .heartbeat import read_heartbeat
from .process import popen_factory
from .supervisor import Supervisor

import time


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="downbeat_supervisor", description=__doc__)
    parser.add_argument("--config", required=True, help="the JSON configuration (supervisor/configs/windows.json or mac.json)")
    parser.add_argument("--root", default=os.getcwd(), help="the folder that holds Downbeat.exe (or the Mac build folder); {root} in the configuration (default: this folder)")
    parser.add_argument("--var", action="append", default=[], metavar="NAME=VALUE", help="sets a {variable} of the configuration, for example camera=1")
    parser.add_argument("--dry-run", action="store_true", help="print what would be started and exit")
    args = parser.parse_args(argv)

    variables = {}
    for item in args.var:
        name, separator, value = item.partition("=")
        if not separator or not name:
            parser.error(f"--var wants NAME=VALUE, not '{item}'")
        variables[name] = value
    try:
        settings = load_config(args.config, os.path.abspath(args.root), variables)
    except ConfigError as error:
        print(f"downbeat_supervisor: {error}", file=sys.stderr)
        return 2

    if args.dry_run:
        print(json.dumps({"logsDir": settings.logs_dir, "processes": [{"name": p.name, "command": list(p.command), "cwd": p.cwd, "heartbeatFile": p.heartbeat_file} for p in settings.processes]}, indent=2, ensure_ascii=False))
        return 0

    stop = {"now": False}

    def request_stop(*_: object) -> None:
        stop["now"] = True

    for name in ("SIGINT", "SIGTERM", "SIGBREAK"):
        number = getattr(signal, name, None)
        if number is not None:
            signal.signal(number, request_stop)

    events = EventLog(settings.logs_dir, lambda: datetime.now(timezone.utc))
    supervisor = Supervisor(settings, events, popen_factory(settings.logs_dir), time.monotonic, lambda: datetime.now(timezone.utc), read_heartbeat)
    print(f"downbeat_supervisor: watching {', '.join(p.name for p in settings.processes)}; the record is in {settings.logs_dir}. Ctrl+C ends it and the processes it started.")
    try:
        supervisor.run(lambda: stop["now"])
    finally:
        supervisor.shutdown()
    return 0


if __name__ == "__main__":
    sys.exit(main())
