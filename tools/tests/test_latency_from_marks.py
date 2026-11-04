"""Tests of tools/latency_from_marks.py (run from tools/: ../tracker/.venv/bin/python -m pytest tests -q)."""
import json
import math

import pytest

import latency_from_marks as lfm

HEADER = "trial,hand_down_frame,sound_frame,flash_frame\n"


def write_csv(tmp_path, rows, header=HEADER):
    path = tmp_path / "marks.csv"
    path.write_text(header + "".join(f"{row}\n" for row in rows), encoding="utf-8")
    return path


def test_computes_ms_from_frames(tmp_path):
    # 240 pictures a second: one picture is 4.1667 ms. Hand down at 100, sound heard at 160 (60 pictures), flash seen at 150 (50 pictures).
    result = lfm.analyze(write_csv(tmp_path, ["1,100,160,150"]), fps=240)

    trial = result["trials"][0]
    assert trial["trial"] == "1"
    assert trial["hand_to_sound_ms"] == pytest.approx(250.0)
    assert trial["hand_to_screen_ms"] == pytest.approx(50 / 240 * 1000)
    assert trial["screen_minus_sound_ms"] == pytest.approx(-(10 / 240 * 1000))
    assert result["frame_ms"] == pytest.approx(1000 / 240)


def test_skips_incomplete_rows_and_counts_them(tmp_path):
    rows = ["1,100,160,150", "2,200,,250", "3,,300,310", "4,400,460,", "5,500,562,551", "6,abc,600,610"]

    result = lfm.analyze(write_csv(tmp_path, rows), fps=240)

    assert result["trials_used"] == 2
    assert result["rows_skipped"] == 4
    assert [t["trial"] for t in result["trials"]] == ["1", "5"]


def test_reports_median_p90_max(tmp_path):
    # hand to sound: 10 trials of 10, 20, ... 100 pictures at 100 pictures a second = 100, 200, ... 1000 ms
    rows = [f"{i},0,{10 * i},{10 * i}" for i in range(1, 11)]

    result = lfm.analyze(write_csv(tmp_path, rows), fps=100)

    stats = result["hand_to_sound_ms"]
    assert stats["median"] == pytest.approx(550.0)
    assert stats["p90"] == pytest.approx(910.0)   # linear between the 9th (900) and the 10th (1000) value
    assert stats["max"] == pytest.approx(1000.0)
    assert stats["min"] == pytest.approx(100.0)
    assert result["hand_to_screen_ms"]["median"] == pytest.approx(550.0)


def test_percentile_of_one_value_is_that_value():
    assert lfm.percentile([42.0], 90) == 42.0


def test_a_sound_before_the_hand_is_a_misread_and_is_left_out(tmp_path):
    result = lfm.analyze(write_csv(tmp_path, ["1,100,160,150", "2,300,290,310"]), fps=240)

    assert result["trials_used"] == 1
    assert result["rows_negative"] == 1
    assert any("before the hand" in w for w in result["warnings"])


def test_warns_when_there_are_fewer_than_thirty_trials(tmp_path):
    result = lfm.analyze(write_csv(tmp_path, ["1,100,160,150"]), fps=240)

    assert any("30" in w for w in result["warnings"])


def test_no_warning_about_the_count_from_thirty_trials(tmp_path):
    rows = [f"{i},{i * 1000},{i * 1000 + 60},{i * 1000 + 50}" for i in range(30)]

    result = lfm.analyze(write_csv(tmp_path, rows), fps=240)

    assert not any("30 or more" in w for w in result["warnings"])


def test_a_file_without_a_usable_row_is_an_error(tmp_path):
    with pytest.raises(lfm.MarksError, match="no complete row"):
        lfm.analyze(write_csv(tmp_path, ["1,,,", "2,100,,"]), fps=240)


def test_a_missing_column_is_named(tmp_path):
    path = write_csv(tmp_path, ["1,100,160"], header="trial,hand_down_frame,sound_frame\n")

    with pytest.raises(lfm.MarksError, match="flash_frame"):
        lfm.analyze(path, fps=240)


def test_fps_must_be_a_positive_number(tmp_path):
    path = write_csv(tmp_path, ["1,100,160,150"])
    for bad in (0, -240, float("nan"), float("inf")):
        with pytest.raises(lfm.MarksError, match="fps"):
            lfm.analyze(path, fps=bad)


def test_decimal_frame_numbers_are_accepted(tmp_path):
    # a frame counted between two pictures (a half) is allowed
    result = lfm.analyze(write_csv(tmp_path, ["1,100,160.5,150"]), fps=240)

    assert result["trials"][0]["hand_to_sound_ms"] == pytest.approx(60.5 / 240 * 1000)


def test_command_line_writes_json(tmp_path, capsys):
    path = write_csv(tmp_path, ["1,100,160,150", "2,200,262,249"])
    out = tmp_path / "result.json"

    code = lfm.main(["--csv", str(path), "--fps", "240", "--out", str(out)])

    assert code == 0
    data = json.loads(out.read_text(encoding="utf-8"))
    assert data["trials_used"] == 2
    assert math.isclose(data["hand_to_sound_ms"]["max"], 62 / 240 * 1000)
    assert json.loads(capsys.readouterr().out)["trials_used"] == 2


def test_command_line_reports_a_missing_file_with_exit_code_2(tmp_path, capsys):
    code = lfm.main(["--csv", str(tmp_path / "nope.csv"), "--fps", "240"])

    assert code == 2
    assert "nope.csv" in capsys.readouterr().err
