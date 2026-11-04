"""A player that cannot get a sound device (an HDMI monitor that is the only speaker is switched off) must not be restarted every minute all night:
after a few starts in a row that never stayed healthy the rests grow up to a cap, and one healthy run puts everything back."""
from conftest import TRACKER, TRACKER_WITH_BEATS, UNITY, run_for

RESTART_DELAY = 3.0
REST = 60.0
CAP = 600.0
STABLE_AFTER = 120.0
FAIL_AFTER_SEC = 30          # the player gives up on the sound this long after its start (BootTimeoutSec of the app)


def events_named(supervisor, name: str) -> list:
    return [row for row in supervisor.events.rows if row[2] == name]


def one_failed_run(world, supervisor, starts_before: int) -> float:
    """The player at this start has no sound: it says starting, then failed. Returns how many seconds passed between the kill and the next start."""
    process = world.of("unity")[starts_before - 1]
    for _ in range(FAIL_AFTER_SEC):
        world.beat(audio="starting")
        run_for(world, supervisor, 1)
    killed_at = None
    for _ in range(3000):
        world.beat(audio="failed")
        run_for(world, supervisor, 1)
        if process.stopped and killed_at is None:
            killed_at = world.clock.seconds
        if len(world.of("unity")) > starts_before:
            return world.clock.seconds - killed_at
    raise AssertionError("the player was not started again within 3000 s")


def test_rests_grow_after_three_starts_in_a_row_that_never_stayed_healthy_and_stop_at_the_cap(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    gaps = [one_failed_run(world, supervisor, starts_before=n) for n in range(1, 9)]

    assert gaps[0] <= RESTART_DELAY + 2 and gaps[1] <= RESTART_DELAY + 2, f"the first two are restarted at once: {gaps}"
    assert REST <= gaps[2] <= REST + 2, f"the third failed run rests a minute: {gaps}"
    assert 2 * REST <= gaps[3] <= 2 * REST + 2
    assert 4 * REST <= gaps[4] <= 4 * REST + 2
    assert 8 * REST <= gaps[5] <= 8 * REST + 2
    assert CAP <= gaps[6] <= CAP + 2, f"capped at {CAP:.0f} s: {gaps}"
    assert CAP <= gaps[7] <= CAP + 2, "and it stays there"
    escalations = events_named(supervisor, "backoff_escalated")
    assert escalations, "the supervisor says so in its record"
    assert "3 starts in a row" in escalations[0][3] and "audio_failed" in escalations[0][3]


def test_a_run_that_stays_healthy_for_the_stable_time_puts_the_rests_back(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for n in range(1, 5):
        one_failed_run(world, supervisor, starts_before=n)
    assert events_named(supervisor, "backoff_escalated"), "it had escalated"
    starts = len(world.of("unity"))

    for _ in range(int(STABLE_AFTER) + 5):         # the device is back: this start is healthy for longer than the stable time
        world.beat(audio="ok")
        run_for(world, supervisor, 1)
    assert len(events_named(supervisor, "recovered")) == 1

    world.of("unity")[-1].die(1)                   # an unrelated crash much later is an ordinary restart again
    run_for(world, supervisor, int(RESTART_DELAY) + 2)

    assert len(world.of("unity")) == starts + 1, "restarted after the plain 3 s, not after a rest"


def test_a_run_that_is_healthy_only_for_a_while_does_not_reset_the_count(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for n in range(1, 3):
        one_failed_run(world, supervisor, starts_before=n)

    process = world.of("unity")[-1]
    for _ in range(int(STABLE_AFTER) - 20):        # the sound works for a while, but not for the stable time ...
        world.beat(audio="ok")
        run_for(world, supervisor, 1)
    for _ in range(40):                            # ... then it stalls again
        world.beat(audio="stalled")
        run_for(world, supervisor, 1)
    assert process.stopped is True
    assert events_named(supervisor, "recovered") == []

    run_for(world, supervisor, int(REST) + 5)
    assert len(world.of("unity")) == 4, "three unstable runs in a row: the rest after the third is a minute"


def test_exits_before_the_stable_time_count_as_unstable_runs_too(world, make):
    supervisor = make(TRACKER)
    supervisor.step()
    for _ in range(3):
        world.of("tracker")[-1].die(1)
        run_for(world, supervisor, int(RESTART_DELAY) + 1)
    assert len(world.of("tracker")) == 3, "the first two exits are followed by a start after 3 s"

    run_for(world, supervisor, 30)
    assert len(world.of("tracker")) == 3, "the third exit in a row rests a minute"
    run_for(world, supervisor, int(REST))

    assert len(world.of("tracker")) == 4
    assert events_named(supervisor, "backoff_escalated")


def test_a_process_without_a_heartbeat_is_stable_once_it_has_run_for_the_stable_time(world, make):
    supervisor = make(TRACKER)
    supervisor.step()
    for _ in range(2):
        world.of("tracker")[-1].die(1)
        run_for(world, supervisor, int(RESTART_DELAY) + 1)

    run_for(world, supervisor, int(STABLE_AFTER) + 5)
    world.of("tracker")[-1].die(1)
    run_for(world, supervisor, int(RESTART_DELAY) + 2)

    assert len(world.of("tracker")) == 4, "a long run in between: the next crash is an ordinary one"
    assert events_named(supervisor, "backoff_escalated") == []


def test_a_program_that_cannot_be_started_rests_longer_and_longer_as_well(world, make):
    supervisor = make(TRACKER)
    attempts = []

    def missing(spec):
        attempts.append(world.clock.seconds)
        raise FileNotFoundError("python.exe")

    supervisor.spawn = missing
    run_for(world, supervisor, 600)

    gaps = [b - a for a, b in zip(attempts, attempts[1:])]
    assert gaps[0] <= RESTART_DELAY + 1
    assert max(gaps) >= REST, f"the tries slow down: {gaps}"
    assert len(attempts) < 600 / RESTART_DELAY / 3, "far fewer than one try every 3 s"


def test_the_sound_still_starting_after_the_grace_period_is_a_stuck_start(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for _ in range(55):
        world.beat(audio="starting")
        run_for(world, supervisor, 1)
    assert world.of("unity")[0].stopped is False, "still inside the 60 s of grace"
    for _ in range(10):
        world.beat(audio="starting")
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is True
    assert events_named(supervisor, "audio_not_started")


def test_a_sound_that_was_given_up_restarts_the_player_after_the_stall_time(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for _ in range(15):
        world.beat(audio="starting")
        run_for(world, supervisor, 1)
    for _ in range(25):
        world.beat(audio="failed")
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is True
    assert events_named(supervisor, "audio_failed")
    assert events_named(supervisor, "audio_stalled") == []


def test_an_app_that_says_it_failed_is_restarted_whatever_the_reason(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for _ in range(10):
        world.beat(audio="ok")
        run_for(world, supervisor, 1)
    for _ in range(25):
        world.beat(audio="ok", app="failed")      # the content could not be loaded: the sound is fine, the app is not
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is True
    assert events_named(supervisor, "app_failed")


def test_a_player_that_does_not_say_anything_about_the_sound_is_healthy(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for _ in range(int(STABLE_AFTER) + 10):
        world.beat(audio=None, app=None)              # an older player: one line
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is False


# ---- the tracker has a heartbeat of its own (a read() of the camera that never returns stops it) ------------------------------------------

def tracker_beats(world, seconds: int, supervisor, audio=None) -> None:
    for _ in range(seconds):
        world.beat("tracker-hb.txt", audio=audio, app=None)
        run_for(world, supervisor, 1)


def test_a_tracker_whose_beats_stop_is_restarted_after_its_own_timeout(world, make):
    supervisor = make(TRACKER_WITH_BEATS)
    supervisor.step()
    tracker_beats(world, 30, supervisor)

    run_for(world, supervisor, 15)
    assert world.of("tracker")[0].stopped is False, "15 s without news is inside the 20 s of this tracker"
    run_for(world, supervisor, 10)

    assert world.of("tracker")[0].stopped is True
    stale = events_named(supervisor, "heartbeat_stale")
    assert len(stale) == 1 and stale[0][1] == "tracker"
    run_for(world, supervisor, 5)
    assert len(world.of("tracker")) == 2


def test_a_tracker_that_keeps_beating_is_left_alone_even_without_a_camera(world, make):
    supervisor = make(TRACKER_WITH_BEATS)
    supervisor.step()

    tracker_beats(world, 300, supervisor)

    assert len(world.of("tracker")) == 1


def test_a_tracker_that_never_beats_is_restarted_when_the_grace_period_is_over(world, make):
    supervisor = make(TRACKER_WITH_BEATS)
    supervisor.step()

    run_for(world, supervisor, 70)

    assert world.of("tracker")[0].stopped is True
    assert events_named(supervisor, "heartbeat_missing")[0][1] == "tracker"


def test_the_player_and_the_tracker_are_judged_one_by_one(world, make):
    supervisor = make(UNITY, TRACKER_WITH_BEATS)
    supervisor.step()
    for _ in range(40):
        world.beat("hb.txt", audio="ok")
        world.beat("tracker-hb.txt", audio=None, app=None)
        run_for(world, supervisor, 1)
    for _ in range(30):                               # only the tracker's beats stop
        world.beat("hb.txt", audio="ok")
        run_for(world, supervisor, 1)

    assert world.of("tracker")[0].stopped is True
    assert world.of("unity")[0].stopped is False


# ---- the app has to have reached Running: a clock that moves is not enough (a Boot that threw leaves the app loading for ever, with the sound clock running) ------------

def test_an_app_that_is_loading_inside_the_grace_period_is_left_alone(world, make):
    supervisor = make(UNITY)
    supervisor.step()

    for _ in range(50):
        world.beat(audio="ok", app="loading")
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is False, "50 s of the 60 s of grace"


def test_an_app_that_is_still_loading_after_the_grace_period_is_restarted_as_not_ready(world, make):
    supervisor = make(UNITY)
    supervisor.step()

    for _ in range(70):
        world.beat(audio="ok", app="loading")      # the sound clock runs, the beat is fresh, and the app never gets to Running
        run_for(world, supervisor, 1)

    assert world.of("unity")[0].stopped is True
    not_ready = events_named(supervisor, "app_not_ready")
    assert len(not_ready) == 1 and "loading" in not_ready[0][3]
    assert events_named(supervisor, "audio_not_started") == [], "it is the app that is not ready, not only the sound"


def test_a_start_that_never_got_past_loading_counts_as_unstable(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for n in range(1, 4):
        process = world.of("unity")[n - 1]
        for _ in range(70):
            world.beat(audio="ok", app="loading")
            run_for(world, supervisor, 1)
        assert process.stopped is True
        for _ in range(70):                          # wait for the next start (3 s, then 60 s after the third)
            if len(world.of("unity")) > n:
                break
            run_for(world, supervisor, 1)

    escalations = events_named(supervisor, "backoff_escalated")
    assert escalations and "app_not_ready" in escalations[0][3]


def test_a_loading_app_is_never_counted_as_healthy_for_the_stability_reset(world, make):
    supervisor = make(UNITY, heartbeat_grace_sec=1000.0)
    supervisor.step()
    for n in range(1, 4):
        one_failed_run(world, supervisor, starts_before=n)
    assert events_named(supervisor, "backoff_escalated"), "it had escalated"

    for _ in range(int(STABLE_AFTER) + 30):          # loading for longer than the stable time, with the sound clock running
        world.beat(audio="ok", app="loading")
        run_for(world, supervisor, 1)

    assert events_named(supervisor, "recovered") == [], "a loading app is not a healthy one"


def test_loading_and_then_running_is_healthy_and_resets_the_escalation(world, make):
    supervisor = make(UNITY)
    supervisor.step()
    for n in range(1, 4):
        one_failed_run(world, supervisor, starts_before=n)
    for _ in range(10):
        world.beat(audio="ok", app="loading")
        run_for(world, supervisor, 1)
    assert world.of("unity")[-1].stopped is False

    for _ in range(int(STABLE_AFTER) + 5):
        world.beat(audio="ok", app="running")
        run_for(world, supervisor, 1)

    assert len(events_named(supervisor, "recovered")) == 1
    assert world.of("unity")[-1].stopped is False
