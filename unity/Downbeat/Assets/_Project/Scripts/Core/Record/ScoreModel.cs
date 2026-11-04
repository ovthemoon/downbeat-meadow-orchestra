using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>One note on the live score: when it is heard (audio-clock seconds), which part played it, which step of the scale, how hard.</summary>
    public readonly struct ScoreMark
    {
        public ScoreMark(double time, Part part, int pitchStep, float velocity)
        {
            Time = time;
            Part = part;
            PitchStep = pitchStep;
            Velocity = velocity < 0f ? 0f : (velocity > 1f ? 1f : velocity);
        }

        public double Time { get; }
        public Part Part { get; }
        public int PitchStep { get; }
        public float Velocity { get; }
    }

    /// <summary>One note of a nature layer, for the glints on its line.</summary>
    public readonly struct NatureMark
    {
        public NatureMark(NatureLayer layer, double time, float gain)
        {
            Layer = layer;
            Time = time;
            Gain = gain;
        }

        public NatureLayer Layer { get; }
        public double Time { get; }
        public float Gain { get; }
    }

    /// <summary>
    /// What the live score shows: the last <c>capacity</c> hand notes (the oldest is dropped when it is full), the glints of the nature layers, and which nature layers have joined
    /// (and when, so a line can light up gently). Pure data: the view asks for the notes of a window of time and draws them.
    /// </summary>
    public sealed class ScoreModel
    {
        readonly ScoreMark[] _marks;
        readonly NatureMark[] _glints;
        readonly bool[] _active = new bool[NatureLayers.Count];
        readonly double[] _changedAt = new double[NatureLayers.Count];
        int _markStart;
        int _markCount;
        int _glintStart;
        int _glintCount;

        public ScoreModel(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "must be at least 1");
            _marks = new ScoreMark[capacity];
            _glints = new NatureMark[capacity];
            for (var i = 0; i < _changedAt.Length; i++) _changedAt[i] = double.NegativeInfinity;
        }

        public int Capacity => _marks.Length;
        public int Count => _markCount;

        public void Add(in ScoreMark mark) => Push(_marks, ref _markStart, ref _markCount, mark);

        /// <summary>Takes back the newest notes that are heard after <paramref name="time"/> (their sound was cancelled: the piece was ended before it): they never appear on the score.</summary>
        public void RemoveMarksAfter(double time)
        {
            while (_markCount > 0 && _marks[(_markStart + _markCount - 1) % _marks.Length].Time > time) _markCount--;
        }

        public void AddNature(NatureLayer layer, double time, float gain) => Push(_glints, ref _glintStart, ref _glintCount, new NatureMark(layer, time, gain));

        /// <summary>Marks a nature layer as joined or left at <paramref name="time"/>; saying the same thing again changes nothing.</summary>
        public void SetLayerActive(NatureLayer layer, bool active, double time)
        {
            var i = (int)layer;
            if (_active[i] == active) return;
            _active[i] = active;
            _changedAt[i] = time;
        }

        public bool IsLayerActive(NatureLayer layer) => _active[(int)layer];

        /// <summary>When the layer last joined or left (negative infinity if it never did).</summary>
        public double LayerChangedAt(NatureLayer layer) => _changedAt[(int)layer];

        public int ActiveLayerCount
        {
            get
            {
                var n = 0;
                foreach (var a in _active) if (a) n++;
                return n;
            }
        }

        /// <summary>Appends the notes heard in the last <paramref name="windowSec"/> seconds up to <paramref name="now"/> (a note that is heard later is not on the score yet), oldest first.</summary>
        public void CopyVisible(double now, double windowSec, List<ScoreMark> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            for (var i = 0; i < _markCount; i++)
            {
                var mark = _marks[(_markStart + i) % _marks.Length];
                if (mark.Time <= now && mark.Time > now - windowSec) output.Add(mark);
            }
        }

        public void CopyVisibleNature(double now, double windowSec, List<NatureMark> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            for (var i = 0; i < _glintCount; i++)
            {
                var glint = _glints[(_glintStart + i) % _glints.Length];
                if (glint.Time <= now && glint.Time > now - windowSec) output.Add(glint);
            }
        }

        static void Push<T>(T[] ring, ref int start, ref int count, T value)
        {
            if (count < ring.Length)
            {
                ring[(start + count) % ring.Length] = value;
                count++;
                return;
            }
            ring[start] = value;                    // full: the oldest is overwritten
            start = (start + 1) % ring.Length;
        }
    }

    public readonly struct ScorePoint
    {
        public ScorePoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        /// <summary>Counted upwards from the bottom of the score.</summary>
        public float Y { get; }
    }

    public static class ScoreLayout
    {
        /// <summary>The place of a note on a score of <paramref name="width"/> x <paramref name="height"/>: the right edge is now, the left edge is <paramref name="windowSec"/> ago, a higher step is higher up.</summary>
        public static ScorePoint Position(in ScoreMark mark, double now, double windowSec, float width, float height, int pitchSteps)
        {
            if (!(windowSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(windowSec), windowSec, "must be positive");
            if (pitchSteps < 1) throw new ArgumentOutOfRangeException(nameof(pitchSteps), pitchSteps, "must be at least 1");
            var x = width * (float)(1.0 - (now - mark.Time) / windowSec);
            return new ScorePoint(x, StepY(mark.PitchStep, height, pitchSteps));
        }

        /// <summary>Height of a step: the middle of its band, so that the steps sit alternately on a staff line and between two.</summary>
        public static float StepY(int pitchStep, float height, int pitchSteps) => height * (pitchStep + 0.5f) / pitchSteps;

        /// <summary>The five staff lines: the odd steps (1, 3, 5, 7, 9 of ten), so the other steps fall in the spaces.</summary>
        public static float StaffLineY(int line, float height, int pitchSteps)
        {
            if (line < 0 || line > 4) throw new ArgumentOutOfRangeException(nameof(line), line, "0..4");
            return StepY(Math.Min(pitchSteps - 1, 1 + 2 * line), height, pitchSteps);
        }
    }
}
