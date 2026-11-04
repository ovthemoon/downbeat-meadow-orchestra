using System.Collections.Generic;
using Downbeat.Controls;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class InputModeTests
    {
        [TestCase("mouse", InputMode.Mouse)]
        [TestCase("MOUSE", InputMode.Mouse)]
        [TestCase("replay", InputMode.Replay)]
        [TestCase("hand", InputMode.Hand)]
        [TestCase("camera", InputMode.Hand)]
        [TestCase("", InputMode.Hand)]
        [TestCase(null, InputMode.Hand)]
        [TestCase("nonsense", InputMode.Hand)]
        public void The_input_parameter_is_parsed_and_defaults_to_the_camera(string value, InputMode expected)
        {
            Assert.AreEqual(expected, InputModeParser.Parse(value));
        }

        [TestCase("--input mouse", InputMode.Mouse)]
        [TestCase("--input=replay", InputMode.Replay)]
        [TestCase("--input hand", InputMode.Hand)]
        [TestCase("--view stage", InputMode.Hand)]
        [TestCase("", InputMode.Hand)]
        public void The_input_option_of_the_command_line_picks_the_mode_and_defaults_to_the_hand_tracker(string commandLine, InputMode expected)
        {
            var args = LaunchArgs.Parse(commandLine.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));

            Assert.AreEqual(expected, InputModeParser.FromArgs(args));
        }

        [Test]
        public void The_camera_mode_is_used_only_while_the_tracker_is_running()
        {
            Assert.AreEqual(InputMode.Hand, InputModeResolver.Resolve(InputMode.Hand, TrackerState.Running, null).Active);
            Assert.AreEqual(InputMode.Mouse, InputModeResolver.Resolve(InputMode.Hand, TrackerState.Starting, null).Active);
            Assert.AreEqual(InputMode.Mouse, InputModeResolver.Resolve(InputMode.Hand, TrackerState.Off, null).Active);
        }

        [TestCase(TrackerState.CameraDenied, "denied")]
        [TestCase(TrackerState.CameraMissing, "no camera")]
        [TestCase(TrackerState.CameraError, "could not be opened")]
        [TestCase(TrackerState.ModelFailed, "model")]
        public void A_camera_that_cannot_be_used_falls_back_to_the_mouse_and_says_why(TrackerState state, string reasonPart)
        {
            var decision = InputModeResolver.Resolve(InputMode.Hand, state, null);

            Assert.AreEqual(InputMode.Mouse, decision.Active);
            StringAssert.Contains(reasonPart, decision.Label);
        }

        [Test]
        public void Mouse_and_replay_do_not_depend_on_the_camera()
        {
            Assert.AreEqual(InputMode.Mouse, InputModeResolver.Resolve(InputMode.Mouse, TrackerState.CameraDenied, null).Active);
            var replay = InputModeResolver.Resolve(InputMode.Replay, TrackerState.Off, "replay/demo-hands.json");
            Assert.AreEqual(InputMode.Replay, replay.Active);
            StringAssert.Contains("demo-hands.json", replay.Label);
        }
    }

    public class SwitchingPlayInputTests
    {
        sealed class Fixture
        {
            public readonly FakeHandSource Source = new FakeHandSource();
            public readonly List<string> Announced = new List<string>();
            public readonly DebugPointerInput Mouse;
            public readonly SwitchingPlayInput Input;

            public Fixture(InputMode requested)
            {
                var range = new PlayableRange();
                range.Set(10);
                Mouse = new DebugPointerInput(new FixedLayout(), range);
                var settings = new HandSettingsHolder();
                var hand = new HandPlayInput(Source, range, settings, new HandMotionTracker(settings), new HandInterpreter());
                Input = new SwitchingPlayInput(Mouse, hand, requested, "demo.json", Announced.Add);
            }

            public bool Take(out NoteIntent intent) => Input.TryDequeue(out intent);
        }

        sealed class FixedLayout : IViewLayout
        {
            public RectF StagePixels => new RectF(0, 0, 900, 450);
        }

        [Test]
        public void The_camera_starting_means_mouse_for_now_then_hands_once_the_tracker_runs()
        {
            var f = new Fixture(InputMode.Hand);
            f.Source.State = TrackerState.Starting;
            f.Input.Tick(0);
            Assert.AreEqual(InputMode.Mouse, f.Input.Active);

            f.Source.State = TrackerState.Running;
            f.Input.Tick(0.1);

            Assert.AreEqual(InputMode.Hand, f.Input.Active);
            Assert.AreEqual(2, f.Announced.Count);
            StringAssert.Contains("Camera", f.Announced[1]);
        }

        [Test]
        public void A_denied_camera_switches_to_the_mouse_and_announces_it_once()
        {
            var f = new Fixture(InputMode.Hand);
            f.Source.State = TrackerState.CameraDenied;

            f.Input.Tick(0);
            f.Input.Tick(0.1);
            f.Input.Tick(0.2);

            Assert.AreEqual(InputMode.Mouse, f.Input.Active);
            Assert.AreEqual(1, f.Announced.Count);
            StringAssert.Contains("denied", f.Announced[0]);
        }

        [Test]
        public void In_mouse_mode_injected_clicks_play_and_hand_strikes_do_not()
        {
            var f = new Fixture(InputMode.Hand);
            f.Source.State = TrackerState.CameraMissing;
            f.Input.Tick(0);
            f.Mouse.Inject(new NoteIntent(Part.Recorder, 4, 0.7f));
            foreach (var frame in HandTestData.StrikeFrames(0.5f, 0.3f, 8f)) f.Source.Pending.Add(frame);

            f.Input.Tick(0.1);

            Assert.IsTrue(f.Take(out var intent));
            Assert.AreEqual(Part.Recorder, intent.Part);
            Assert.IsFalse(f.Take(out _), "the hand strike was dropped because the mouse is the active input");
        }

        [Test]
        public void In_hand_mode_a_strike_plays_and_mouse_clicks_are_ignored()
        {
            var f = new Fixture(InputMode.Hand);
            f.Source.State = TrackerState.Running;
            f.Input.Tick(0);
            f.Mouse.Inject(new NoteIntent(Part.Recorder, 4, 0.7f));
            foreach (var frame in HandTestData.StrikeFrames(0.9f, 0.3f, 8f)) f.Source.Pending.Add(frame);

            f.Input.Tick(0.1);

            Assert.IsTrue(f.Take(out var intent));
            Assert.AreEqual(Part.Bell, intent.Part);
            Assert.IsTrue(intent.Timing.IsValid);
            Assert.IsFalse(f.Take(out _));
        }

        [Test]
        public void Notes_queued_for_the_old_mode_are_dropped_when_the_mode_changes()
        {
            var f = new Fixture(InputMode.Hand);
            f.Source.State = TrackerState.Starting;
            f.Input.Tick(0);
            f.Mouse.Inject(new NoteIntent(Part.Recorder, 4, 0.7f));

            f.Source.State = TrackerState.Running;
            f.Input.Tick(0.1);

            Assert.IsFalse(f.Take(out _));
        }
    }
}
