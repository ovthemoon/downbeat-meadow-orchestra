from conftest import TRACKER, UNITY, run_for


def test_starts_every_process_at_once(world, make):
    supervisor = make()

    supervisor.step()

    assert [p.spec.name for p in world.spawned] == ["unity", "tracker"]


def test_restarts_after_exit(world, make):
    supervisor = make()
    supervisor.step()
    world.beat()
    world.of("tracker")[0].die(1)

    run_for(world, supervisor, 2)
    assert len(world.of("tracker")) == 1, "not yet: it waits restartDelaySec (3 s) first"
    run_for(world, supervisor, 3)

    assert len(world.of("tracker")) == 2
    assert world.of("unity")[0].stopped is False, "the other process is left alone"
    assert len(world.of("unity")) == 1


def test_a_process_that_keeps_running_is_never_restarted(world, make):
    supervisor = make()
    supervisor.step()
    for _ in range(40):
        world.beat()
        run_for(world, supervisor, 1)

    assert len(world.spawned) == 2


def test_backs_off_after_5_restarts_in_10_min(world, make):
    supervisor = make(TRACKER, backoff_unstable_runs=99)   # this test is about the window rule; the rests that grow have their own tests (test_escalation.py)
    supervisor.step()
    for _ in range(5):
        world.of("tracker")[-1].die(1)
        run_for(world, supervisor, 4)           # 3 s delay, then the start
    assert len(world.of("tracker")) == 6, "five restarts happen one after the other"

    world.of("tracker")[-1].die(1)
    run_for(world, supervisor, 30)
    assert len(world.of("tracker")) == 6, "the sixth is held back: a rest of 60 s"
    run_for(world, supervisor, 40)

    assert len(world.of("tracker")) == 7, "after the rest it goes on"


def test_restarts_that_are_far_apart_do_not_add_up(world, make):
    supervisor = make(TRACKER)
    supervisor.step()
    for _ in range(8):
        world.of("tracker")[-1].die(1)
        run_for(world, supervisor, 4)
        run_for(world, supervisor, 200)         # each exit is minutes after the last (and each run was a long one): never 5 in 10 minutes

    assert len(world.of("tracker")) == 9
    assert not any(e[2] == "backoff" for e in supervisor.events.rows)


def test_stop_ends_every_child_and_says_so(world, make):
    supervisor = make()
    supervisor.step()

    supervisor.shutdown()

    assert all(p.stopped for p in world.spawned)
    assert [e[2] for e in supervisor.events.rows][-1] == "supervisor_stop"


def test_a_process_that_cannot_start_is_retried_not_fatal(world, make):
    supervisor = make(TRACKER)
    original = world.spawn
    attempts = {"n": 0}

    def flaky(spec):
        attempts["n"] += 1
        if attempts["n"] == 1:
            raise FileNotFoundError("python.exe")
        return original(spec)

    supervisor.spawn = flaky
    supervisor.step()
    run_for(world, supervisor, 5)

    assert len(world.of("tracker")) == 1
    assert any(e[2] == "start_failed" and "python.exe" in e[3] for e in supervisor.events.rows)
