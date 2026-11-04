using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// The conducting engine on top of the motion tracker, driven by hand: a fake audio clock that runs at 60 frames a second and tracker pictures that arrive on their own schedule
    /// (a steady 30 pictures a second, 6 a second, or unevenly), exactly as <c>HandPlayInput</c> wires them.
    /// </summary>
    public sealed class ConductRig
    {
        public const double GridOrigin = 10.0;
        public const double Bpm = 96.0;
        public const int SlotsPerBeat = 4;
        public const int SlotsPerBar = 16;
        public const double LeadSec = 0.1;
        public const double AudioFrameSec = 1.0 / 60.0;
        public const float Jitter = 0.003f;

        public static readonly int[] DefaultGaps = { 1, 2, 2, 2, 1, 1 };   // harp, recorder, bell, low string, marimba, hand drum (song.partMinGapSlots)

        public BeatGrid Grid { get; } = new BeatGrid(GridOrigin, Bpm, SlotsPerBeat);
        public HandSettingsHolder Hand { get; } = new HandSettingsHolder();
        public HandMotionTracker Motion { get; }
        public ConductEngine Engine { get; } = new ConductEngine();
        public ConductSettings Settings { get; }
        public List<NoteIntent> Notes { get; } = new List<NoteIntent>();
        /// <summary>The audio time at which each note of <see cref="Notes"/> was made.</summary>
        public List<double> MadeAt { get; } = new List<double>();

        double _audioNow = GridOrigin - 1.0;
        double _nextPictureAt = GridOrigin - 1.0;
        long _seq = 1;
        double _captureMs;

        /// <summary>The rig with the default pose and cells and <see cref="DefaultGaps"/>, unless the blocks of a content file (<paramref name="pose"/>, <paramref name="cells"/>, <paramref name="gaps"/>) are given.</summary>
        public ConductRig(ConductDto content = null, HandSettingsDto hand = null, PoseDto pose = null, CellsDto cells = null, int[] gaps = null)
        {
            Hand.Set(hand ?? new HandSettingsDto(), pose, cells);
            Motion = new HandMotionTracker(Hand);
            Settings = ConductSettings.From(content, null);
            Engine.Configure(Settings, SlotsPerBeat, SlotsPerBar, gaps ?? DefaultGaps, LeadSec, Hand.Settings.PartHysteresis, Hand.Settings.MaxGapMs);
            Engine.OnGridReady(Grid);
        }

        public double AudioNow => _audioNow;

        static SongPitch _shippedPitch;

        /// <summary>Prepares <paramref name="score"/> and starts it on the bar line <paramref name="startSlot"/>. The notes of the song are shown with the steps of the shipped instruments unless <paramref name="pitch"/> is given.</summary>
        public void PrepareAndStart(SongScore score, long startSlot, SongPitch pitch = null)
        {
            Engine.PrepareSong(score, pitch ?? (_shippedPitch ?? (_shippedPitch = SongPitch.From(TestContent.LoadShipped()))));
            Engine.StartSong(startSlot);
        }

        /// <summary>Runs for this long. <paramref name="hands"/> says where the hands are at a time (seconds since the rig was made); <paramref name="pictureGaps"/> says how long each picture of the tracker comes after the last.</summary>
        public void Run(double seconds, Func<double, Pose[]> hands, Func<double> pictureGaps)
        {
            var end = _audioNow + seconds;
            while (_audioNow < end)
            {
                _audioNow += AudioFrameSec;
                while (_nextPictureAt <= _audioNow)
                {
                    _captureMs = (_nextPictureAt - (GridOrigin - 1.0)) * 1000.0;
                    Motion.Update(Frame(_seq++, _captureMs, hands(_nextPictureAt - (GridOrigin - 1.0))));
                    _nextPictureAt += pictureGaps();
                }
                Update();
            }
        }

        void Update()
        {
            var before = Notes.Count;
            Engine.Update(_audioNow, Motion, 1f, Notes);
            for (var i = before; i < Notes.Count; i++) MadeAt.Add(_audioNow);
        }

        public void Run(double seconds, Func<double, Pose[]> hands, double pictureEverySec) => Run(seconds, hands, () => pictureEverySec);

        /// <summary>Runs a recorded hand file (<c>content/replay/demo-hands.json</c>) as the camera: its pictures arrive at the times they were recorded.</summary>
        public void RunReplay(ReplayPlayer player, double seconds)
        {
            var end = _audioNow + seconds;
            var start = _audioNow;
            var frames = new List<HandFrame>();
            while (_audioNow < end)
            {
                _audioNow += AudioFrameSec;
                frames.Clear();
                player.Advance((_audioNow - start) * 1000.0, frames);
                foreach (var frame in frames) Motion.Update(frame);
                Update();
            }
        }

        /// <summary>The slot a note asked for: the first slot at least <see cref="LeadSec"/> after the time it was requested, which is what the scheduler does with it.</summary>
        public long SlotOf(NoteIntent note) => Grid.SlotForTrigger(note.RequestedAt, LeadSec);

        public IEnumerable<NoteIntent> Of(Part part) => Notes.Where(n => n.Part == part);

        /// <summary>The distance in slots between neighbouring notes of a part, after the first <paramref name="skipSec"/> seconds of the run (the movement is measured over a window).</summary>
        public List<long> Gaps(Part part, double skipSec)
        {
            var from = Grid.FirstSlotAtOrAfter(GridOrigin - 1.0 + skipSec);
            var slots = Of(part).Select(SlotOf).Where(s => s >= from).ToList();
            var gaps = new List<long>();
            for (var i = 1; i < slots.Count; i++) gaps.Add(slots[i] - slots[i - 1]);
            return gaps;
        }

        // ---- the hands people make ---------------------------------------------------------------------------------------------------------

        /// <summary>The middle of a cell of the picture: a third of the width across (0.17, 0.5, 0.83 are the middles of the flowers' zones), the sky row high (0.25), the ground row low (0.75).</summary>
        public static (float x, float y) CentreOf(Part part) => (new[] { 0.17f, 0.5f, 0.83f }[Parts.Column(part)], Parts.IsSky(part) ? 0.25f : 0.75f);

        /// <summary>A hand held still in the middle of a cell. An open hand unless <paramref name="shape"/> says otherwise.</summary>
        public static Func<double, Pose[]> StillIn(Part part, int id = 0, HandPose shape = HandPose.Open)
        {
            var (x, y) = CentreOf(part);
            return Still(x, y, id, shape);
        }

        /// <summary>A hand that goes round a circle of radius <paramref name="radius"/> (a tenth of the picture: it stays inside its cell) around the middle of a cell.</summary>
        public static Func<double, Pose[]> CircleIn(Part part, float palmsPerSecond, float radius = 0.1f, int id = 0)
        {
            var (x, y) = CentreOf(part);
            return Circle(x, y, palmsPerSecond, radius, id);
        }

        /// <summary>A hand held still at (x, y): the picture of it jitters by a few thousandths from picture to picture, as a real one does. An open hand unless <paramref name="shape"/> says otherwise.</summary>
        public static Func<double, Pose[]> Still(float x, float y, int id = 0, HandPose shape = HandPose.Open) =>
            t =>
            {
                var k = (long)Math.Round(t * 1000.0);
                return new[] { new Pose(id, x + Jitter * Wobble(k, 1), y + Jitter * Wobble(k, 2), shape: shape) };
            };

        /// <summary>A hand that goes round a circle of radius <paramref name="radius"/> around (x, y) at <paramref name="palmsPerSecond"/>.</summary>
        public static Func<double, Pose[]> Circle(float x, float y, float palmsPerSecond, float radius = 0.1f, int id = 0) =>
            t =>
            {
                var omega = palmsPerSecond * PalmSize / radius;
                return new[] { new Pose(id, x + radius * (float)Math.Cos(omega * t), y + radius * (float)Math.Sin(omega * t)) };
            };

        /// <summary>The same hand until <paramref name="untilSec"/>, no hand from then on until <paramref name="backSec"/> (pictures still arrive, without a hand), then the hand again.</summary>
        public static Func<double, Pose[]> Gone(Func<double, Pose[]> hand, double untilSec, double backSec = double.PositiveInfinity) =>
            t => t >= untilSec && t < backSec ? new Pose[0] : hand(t);

        public static Func<double, Pose[]> Both(Func<double, Pose[]> first, Func<double, Pose[]> second) => t => first(t).Concat(second(t)).ToArray();

        /// <summary>Pictures 33 to 200 ms apart in no pattern (the tracker of a busy laptop: 5 to 30 a second).</summary>
        public static Func<double> UnevenPictures(int seed = 7)
        {
            var state = (uint)seed;
            return () =>
            {
                state = state * 1664525u + 1013904223u;
                return 0.033 + (state >> 8) / (double)(1 << 24) * (0.200 - 0.033);
            };
        }

        /// <summary>A repeatable number between -1 and 1 for the k-th picture.</summary>
        static float Wobble(long k, int salt)
        {
            var h = (uint)(k * 2654435761L + salt * 40503L);
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }

        /// <summary>The notes of a part after the first <paramref name="skipSec"/> seconds, that are not on the first beat of a bar (those are accented): what the loudness of a hand is read from.</summary>
        public List<NoteIntent> Unaccented(Part part, double skipSec)
        {
            var from = Grid.FirstSlotAtOrAfter(GridOrigin - 1.0 + skipSec);
            return Of(part).Where(n => SlotOf(n) >= from && SlotOf(n) % SlotsPerBar != 0).ToList();
        }
    }
}
