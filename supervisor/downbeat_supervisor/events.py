"""The supervisor's own record: one CSV row for everything that happens to a process. A log that cannot be written never stops the supervisor."""
from __future__ import annotations

import csv
import os
from datetime import datetime, timezone
from typing import Callable

HEADER = ("timestamp_utc", "process", "event", "detail")


class EventLog:
    def __init__(self, logs_dir: str, utc_now: Callable[[], datetime]) -> None:
        self._dir = logs_dir
        self._utc_now = utc_now
        self.rows: list[tuple[str, str, str, str]] = []
        self.write_failures = 0

    def log(self, process: str, event: str, detail: str = "") -> None:
        now = self._utc_now()
        stamp = now.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.") + f"{now.microsecond // 1000:03d}Z"
        row = (stamp, process, event, detail)
        self.rows.append(row)
        path = os.path.join(self._dir, f"supervisor-{now.astimezone().strftime('%Y%m%d')}.csv")
        try:
            os.makedirs(self._dir, exist_ok=True)
            is_new = not os.path.exists(path) or os.path.getsize(path) == 0
            with open(path, "a", newline="", encoding="utf-8") as handle:
                writer = csv.writer(handle, lineterminator="\n")
                if is_new:
                    writer.writerow(HEADER)
                writer.writerow(row)
        except OSError:
            self.write_failures += 1
