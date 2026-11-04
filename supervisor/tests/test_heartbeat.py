from datetime import datetime, timezone

import pytest

from conftest import UNITY, run_for
from downbeat_supervisor.heartbeat import Heartbeat, age_sec, parse_heartbeat, read_heartbeat


def test_restarts_unity_on_stale_heartbeat(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    first = world.of("unity")[0]
    for _ in range(20):                          # healthy for a while: a beat every second
        world.beat()
        run_for(world, supervisor, 1)

    run_for(world, supervisor, 31)               # then the beat stops (a frozen main thread)

    assert first.stopped is True
    assert any(e[2] == "heartbeat_stale" for e in supervisor.events.rows)
    run_for(world, supervisor, 5)
    assert len(world.of("unity")) == 2


def test_a_heartbeat_that_is_late_but_not_30_seconds_late_is_fine(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    world.beat()
    run_for(world, supervisor, 25)

    assert world.of("unity")[0].stopped is False


def test_no_heartbeat_at_all_gets_a_grace_period_after_the_start(world, make):
    supervisor = make(UNITY)
    supervisor.step()

    run_for(world, supervisor, 50)
    assert world.of("unity")[0].stopped is False, "the player is loading: 60 s of grace"
    run_for(world, supervisor, 15)

    assert world.of("unity")[0].stopped is True
    assert any(e[2] == "heartbeat_missing" for e in supervisor.events.rows)


def test_a_heartbeat_left_by_the_previous_run_is_not_a_sign_of_life(world, make):
    supervisor = make(UNITY)
    world.beat(age=3600)                          # an hour old, written before this start
    supervisor.step()

    run_for(world, supervisor, 40)
    assert world.of("unity")[0].stopped is False, "still inside the grace period: it is not judged as stale"
    run_for(world, supervisor, 25)

    assert world.of("unity")[0].stopped is True


def healthy_for(world, supervisor, seconds: int) -> None:
    for _ in range(seconds):
        world.beat()
        run_for(world, supervisor, 1)


def events_named(supervisor, name: str) -> list:
    return [row for row in supervisor.events.rows if row[2] == name]


def test_one_unreadable_read_between_good_beats_is_forgiven(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    healthy_for(world, supervisor, 100)           # well past the 60 s of grace

    world.heartbeats["hb.txt"] = None             # the player truncates the file to write the next beat: the reader sees nothing once
    run_for(world, supervisor, 1)
    healthy_for(world, supervisor, 10)

    assert world.of("unity")[0].stopped is False
    assert events_named(supervisor, "heartbeat_missing") == []
    assert events_named(supervisor, "heartbeat_stale") == []


def test_a_long_run_of_unreadable_reads_after_good_beats_restarts_once_the_timeout_has_passed(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    healthy_for(world, supervisor, 100)

    world.heartbeats["hb.txt"] = None             # nothing readable any more: the last good beat just grows older
    run_for(world, supervisor, 20)
    assert world.of("unity")[0].stopped is False, "20 s without news is inside the 30 s timeout"
    run_for(world, supervisor, 15)

    assert world.of("unity")[0].stopped is True
    assert len(events_named(supervisor, "heartbeat_stale")) == 1
    assert events_named(supervisor, "heartbeat_missing") == []


def test_a_beat_from_before_the_start_is_still_ignored_after_a_restart(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    healthy_for(world, supervisor, 100)
    world.of("unity")[0].die(1)                   # the first player ends; its last beat stays in the file
    run_for(world, supervisor, 5)
    assert len(world.of("unity")) == 2

    run_for(world, supervisor, 40)                # the new player writes nothing yet: the old beat is 45 s old but is not judged
    assert world.of("unity")[1].stopped is False, "the old beat must not count as stale news of the new player"
    run_for(world, supervisor, 40)

    assert world.of("unity")[1].stopped is True
    assert len(events_named(supervisor, "heartbeat_missing")) == 1, "no beat of this start came: grace ran out"


def test_the_new_player_is_judged_by_its_own_beats_not_by_the_ones_remembered_from_the_old_player(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    healthy_for(world, supervisor, 100)
    world.of("unity")[0].die(1)
    run_for(world, supervisor, 5)
    world.heartbeats.clear()

    healthy_for(world, supervisor, 40)            # the new player beats; the remembered beat of the old one would be long stale

    assert world.of("unity")[1].stopped is False


def test_audio_stalled_for_20_seconds_restarts_the_player(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for _ in range(10):
        world.beat()
        run_for(world, supervisor, 1)

    for _ in range(25):                           # the beats go on, but the sound device is gone
        world.beat(audio="stalled")
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is True
    assert any(e[2] == "audio_stalled" for e in supervisor.events.rows)


def test_a_short_audio_hiccup_is_forgiven(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for _ in range(10):
        world.beat(audio="stalled")
        run_for(world, supervisor, 1)
    for _ in range(30):
        world.beat(audio="ok")
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is False


def test_a_fresh_start_forgets_the_old_heartbeat_state(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    world.of("unity")[0].die(1)
    run_for(world, supervisor, 5)
    assert len(world.of("unity")) == 2
    world.heartbeats.clear()

    run_for(world, supervisor, 30)

    assert world.of("unity")[1].stopped is False, "the new player gets its own grace period"


def test_parse_heartbeat_reads_the_two_lines_the_player_writes():
    hb = parse_heartbeat("2026-10-30T01:02:03.456Z\naudio=ok\n")
    assert hb == Heartbeat(timestamp=datetime(2026, 10, 30, 1, 2, 3, 456000, tzinfo=timezone.utc), audio="ok")
    assert parse_heartbeat("2026-10-30T01:02:03.456Z\naudio=stalled\n").audio == "stalled"


@pytest.mark.parametrize("word", ["starting", "ok", "stalled", "failed"])
def test_parse_heartbeat_knows_the_four_states_of_the_sound(word):
    assert parse_heartbeat(f"2026-10-30T01:02:03.456Z\naudio={word}\n").audio == word


def test_parse_heartbeat_ignores_a_state_it_does_not_know_and_reads_the_app_line():
    hb = parse_heartbeat("2026-10-30T01:02:03.456Z\naudio=sparkling\napp=failed\n")
    assert hb.audio is None, "an unknown word is no news about the sound"
    assert hb.app_failed is True
    assert parse_heartbeat("2026-10-30T01:02:03.456Z\naudio=ok\napp=running\n").app_failed is False


@pytest.mark.parametrize("word", ["loading", "running", "failed"])
def test_parse_heartbeat_knows_the_three_states_of_the_app(word):
    assert parse_heartbeat(f"2026-10-30T01:02:03.456Z\naudio=ok\napp={word}\n").app == word


def test_an_older_player_has_no_app_line_and_an_unknown_word_is_no_news():
    assert parse_heartbeat("2026-10-30T01:02:03.456Z\naudio=ok\n").app is None
    assert parse_heartbeat("2026-10-30T01:02:03.456Z\naudio=ok\napp=dancing\n").app is None


def test_parse_heartbeat_accepts_the_old_one_line_form_and_refuses_garbage():
    assert parse_heartbeat("2026-10-30T01:02:03.456Z\n").audio is None
    assert parse_heartbeat("") is None
    assert parse_heartbeat("not a time\n") is None
    assert parse_heartbeat("2026-13-45T99:99:99Z\n") is None


def test_read_heartbeat_missing_or_unreadable_file_is_none(tmp_path):
    assert read_heartbeat(str(tmp_path / "nope.txt")) is None
    (tmp_path / "hb.txt").write_text("2026-10-30T01:00:00.000Z\naudio=ok\n", encoding="utf-8")
    assert read_heartbeat(str(tmp_path / "hb.txt")).audio == "ok"


def test_age_is_never_negative():
    hb = Heartbeat(timestamp=datetime(2026, 10, 30, 1, 0, 10, tzinfo=timezone.utc), audio="ok")
    assert age_sec(hb, datetime(2026, 10, 30, 1, 0, 0, tzinfo=timezone.utc)) == 0.0
    assert age_sec(hb, datetime(2026, 10, 30, 1, 0, 40, tzinfo=timezone.utc)) == 30.0


def test_the_tracker_heartbeat_is_read_with_the_same_parser():
    hb = parse_heartbeat("2026-10-30T01:02:03.456Z\ncamera=lost\n")

    assert hb == Heartbeat(timestamp=datetime(2026, 10, 30, 1, 2, 3, 456000, tzinfo=timezone.utc)), "the camera line is for people; it says nothing about sound or failure"
    assert hb.audio_ok is None and hb.app_failed is False
