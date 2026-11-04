"""Hand id continuity (web hand-ids.js rules with the plan's numbers: jump > 0.25 is a new hand, a gap > 300 ms frees the id)."""

from __future__ import annotations

from downbeat_tracker.hand_ids import HOLD_MS, MAX_JUMP, Hand2D, HandIdAssigner


def at(x: float, y: float) -> Hand2D:
    return Hand2D(x, y)


def test_constants_are_the_plans_numbers():
    assert MAX_JUMP == 0.25
    assert HOLD_MS == 300.0


def test_ids_follow_moving_hand():
    assigner = HandIdAssigner()

    assert assigner.assign([at(0.5, 0.5)], 0) == [0]
    assert assigner.assign([at(0.52, 0.55)], 33) == [0]
    assert assigner.assign([at(0.6, 0.7)], 66) == [0]


def test_ids_follow_two_hands_when_the_detector_swaps_their_order():
    assigner = HandIdAssigner()

    assert assigner.assign([at(0.2, 0.5), at(0.8, 0.5)], 0) == [0, 1]
    assert assigner.assign([at(0.8, 0.5), at(0.2, 0.5)], 33) == [1, 0]
    assert assigner.assign([at(0.21, 0.52), at(0.79, 0.5)], 66) == [0, 1]


def test_ids_follow_hands_that_move_towards_each_other():
    assigner = HandIdAssigner()
    assert assigner.assign([at(0.3, 0.5), at(0.7, 0.5)], 0) == [0, 1]

    for step in range(1, 11):
        ids = assigner.assign([at(0.3 + step * 0.015, 0.5), at(0.7 - step * 0.015, 0.5)], step * 33)
        assert ids == [0, 1]


def test_best_overall_match_wins_not_the_greedy_one():
    assigner = HandIdAssigner()
    assigner.assign([at(0.30, 0.5), at(0.60, 0.5)], 0)  # ids 0 and 1

    # detection A is slightly nearer to hand 0 than B is, but B can only match hand 1
    assert assigner.assign([at(0.44, 0.5), at(0.30, 0.5)], 33) == [1, 0]


def test_new_id_after_jump():
    assigner = HandIdAssigner()
    assert assigner.assign([at(0.2, 0.5)], 0) == [0]

    # 0.6 away is more than MAX_JUMP: not the same hand, so it must not inherit id 0 (which is still reserved for the unseen hand)
    assert assigner.assign([at(0.8, 0.5)], 33) == [1]


def test_a_jump_just_under_the_limit_is_still_the_same_hand():
    assigner = HandIdAssigner()
    assigner.assign([at(0.2, 0.5)], 0)

    assert assigner.assign([at(0.2 + MAX_JUMP - 0.01, 0.5)], 33) == [0]


def test_new_id_after_gap_300ms():
    # hand 0 vanishes. Within 300 ms its id stays reserved, so a newcomer takes id 1; after 300 ms the id is free again.
    quick = HandIdAssigner()
    quick.assign([at(0.2, 0.5)], 0)
    assert quick.assign([at(0.9, 0.2)], 300) == [1]

    slow = HandIdAssigner()
    slow.assign([at(0.2, 0.5)], 0)
    assert slow.assign([at(0.9, 0.2)], 301) == [0]


def test_a_hand_that_disappears_for_a_moment_gets_its_id_back():
    assigner = HandIdAssigner()
    assert assigner.assign([at(0.2, 0.5), at(0.8, 0.5)], 0) == [0, 1]
    assert assigner.assign([at(0.8, 0.5)], 33) == [1]  # hand 0 is lost for a frame
    assert assigner.assign([at(0.2, 0.52), at(0.8, 0.5)], 66) == [0, 1]


def test_third_detection_is_ignored():
    assigner = HandIdAssigner()

    assert assigner.assign([at(0.1, 0.5), at(0.5, 0.5), at(0.9, 0.5)], 0) == [0, 1]


def test_reset_forgets_every_hand():
    assigner = HandIdAssigner()
    assigner.assign([at(0.2, 0.5)], 0)
    assigner.reset()

    assert assigner.assign([at(0.9, 0.2)], 10) == [0]


def test_from_landmarks_uses_the_wrist():
    landmarks = [(0.25, 0.75, 0.0)] + [(0.9, 0.9, 0.0)] * 20

    assert Hand2D.from_landmarks(landmarks) == Hand2D(0.25, 0.75)
