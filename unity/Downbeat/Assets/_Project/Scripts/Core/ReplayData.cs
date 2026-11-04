using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    // A recording of hand tracking (what the camera tracker produced, not video): content/replay/*.json.
    // Palm and finger landmarks only as 21 x/y pairs per hand, x already mirrored. 4 decimals.

    [Serializable]
    public sealed class ReplayDto
    {
        public string format;
        public int version;
        public string note;
        /// <summary>Video width / height the coordinates were normalized to.</summary>
        public float aspect;
        public bool mirrored;
        public ReplayFrameDto[] frames;
    }

    [Serializable]
    public sealed class ReplayFrameDto
    {
        /// <summary>Capture time in ms since the first frame of the recording.</summary>
        public double t;
        public ReplayHandDto[] h;
    }

    [Serializable]
    public sealed class ReplayHandDto
    {
        public int id;
        public float s;
        /// <summary>x0, y0, x1, y1 ... x20, y20</summary>
        public float[] p;
    }

    public static class ReplayFormat
    {
        public const string Name = "downbeat-hands";
        public const int SupportedVersion = 1;
        public const int CoordinatesPerHand = HandBufferLayout.LandmarksPerHand * 2;
        public const int MaxFrames = 36000; // 20 minutes at 30 fps
    }

    /// <summary>Checks a replay file before it is played: it comes from outside, so nothing in it is trusted.</summary>
    public static class ReplayValidator
    {
        public static IReadOnlyList<string> Validate(ReplayDto replay)
        {
            var errors = new List<string>();
            if (replay == null) { errors.Add("replay is null"); return errors; }
            if (replay.format != ReplayFormat.Name) errors.Add($"format must be '{ReplayFormat.Name}'");
            if (replay.version != ReplayFormat.SupportedVersion) errors.Add($"version {replay.version} is not {ReplayFormat.SupportedVersion}");
            if (!(replay.aspect > 0.5f && replay.aspect < 3f)) errors.Add("aspect must be between 0.5 and 3");
            if (replay.frames == null || replay.frames.Length == 0) { errors.Add("frames is empty"); return errors; }
            if (replay.frames.Length > ReplayFormat.MaxFrames) { errors.Add($"more than {ReplayFormat.MaxFrames} frames"); return errors; }

            var previous = 0.0;
            for (var i = 0; i < replay.frames.Length; i++)
            {
                var frame = replay.frames[i];
                if (frame == null) { errors.Add($"frame {i} is null"); return errors; }
                if (double.IsNaN(frame.t) || double.IsInfinity(frame.t) || frame.t < previous) { errors.Add($"frame {i}: t must be finite and not decrease"); return errors; }
                previous = frame.t;
                if (!ValidateHands(frame.h, i, errors)) return errors;
            }
            return errors;
        }

        static bool ValidateHands(ReplayHandDto[] hands, int frameIndex, List<string> errors)
        {
            if (hands == null) return true;
            if (hands.Length > HandBufferLayout.MaxHands) { errors.Add($"frame {frameIndex}: more than {HandBufferLayout.MaxHands} hands"); return false; }
            var usedIds = 0;
            foreach (var hand in hands)
            {
                if (hand == null || hand.p == null || hand.p.Length != ReplayFormat.CoordinatesPerHand) { errors.Add($"frame {frameIndex}: a hand needs {ReplayFormat.CoordinatesPerHand} coordinates"); return false; }
                if (hand.id < 0 || hand.id >= HandBufferLayout.MaxHands || (usedIds & (1 << hand.id)) != 0) { errors.Add($"frame {frameIndex}: hand id must be 0 or 1 and unique"); return false; }
                usedIds |= 1 << hand.id;
                if (float.IsNaN(hand.s) || hand.s < 0f || hand.s > 1f) { errors.Add($"frame {frameIndex}: score must be 0..1"); return false; }
                foreach (var value in hand.p)
                {
                    if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 4f) { errors.Add($"frame {frameIndex}: coordinate out of range"); return false; }
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Hands recorded frames to the game on a clock. Each frame is written into the same buffer layout the live page uses and read back
    /// with <see cref="HandFrameReader"/>, so a replay and a live camera take exactly the same path from there on.
    /// </summary>
    public sealed class ReplayPlayer
    {
        readonly ReplayDto _replay;
        readonly HandSettingsHolder _settings;
        readonly double[] _buffer = new double[HandBufferLayout.Length];
        int _next;

        /// <param name="settings">Where the pose thresholds come from (<see cref="HandSettingsHolder.Pose"/>); null reads with the defaults.</param>
        public ReplayPlayer(ReplayDto replay, HandSettingsHolder settings = null)
        {
            var problems = ReplayValidator.Validate(replay);
            if (problems.Count > 0) throw new ArgumentException("invalid replay: " + string.Join("; ", problems), nameof(replay));
            _replay = replay;
            _settings = settings;
        }

        public int FrameCount => _replay.frames.Length;
        public double DurationMs => _replay.frames[_replay.frames.Length - 1].t;
        public bool IsFinished => _next >= _replay.frames.Length;

        public void Rewind() => _next = 0;

        /// <summary>Appends every frame with t &lt;= <paramref name="elapsedMs"/> that has not been handed out yet.</summary>
        public void Advance(double elapsedMs, List<HandFrame> output)
        {
            while (_next < _replay.frames.Length && _replay.frames[_next].t <= elapsedMs)
            {
                if (TryBuild(_next, out var frame)) output.Add(frame);
                _next++;
            }
        }

        bool TryBuild(int index, out HandFrame frame)
        {
            var source = _replay.frames[index];
            Array.Clear(_buffer, 0, _buffer.Length);
            _buffer[HandBufferLayout.SeqIndex] = index + 1;
            _buffer[HandBufferLayout.CaptureMsIndex] = source.t;
            _buffer[HandBufferLayout.InferEndMsIndex] = source.t;
            _buffer[HandBufferLayout.StateIndex] = (int)TrackerState.Running;
            _buffer[HandBufferLayout.AspectIndex] = _replay.aspect;
            var handCount = source.h?.Length ?? 0;
            _buffer[HandBufferLayout.HandCountIndex] = handCount;
            for (var slot = 0; slot < handCount; slot++)
            {
                var hand = source.h[slot];
                var baseIndex = HandBufferLayout.HandBase(slot);
                _buffer[baseIndex] = hand.id;
                _buffer[baseIndex + 1] = hand.s;
                for (var i = 0; i < HandBufferLayout.LandmarksPerHand; i++)
                {
                    _buffer[baseIndex + 2 + i * 3] = hand.p[i * 2];
                    _buffer[baseIndex + 3 + i * 3] = hand.p[i * 2 + 1];
                }
            }
            return HandFrameReader.TryRead(_buffer, double.NaN, out frame, _settings?.Pose);
        }
    }

    /// <summary>Which replay files may be requested by the page URL: a relative path inside the site, nothing that reaches elsewhere.</summary>
    public static class ReplayPath
    {
        public const string DefaultFile = "replay/demo-hands.json";

        public static bool IsAllowed(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 120) return false;
            if (!path.EndsWith(".json", StringComparison.Ordinal)) return false;
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains("..") || path.Contains(":") || path.Contains("\\") || path.Contains("//") || path.Contains("?") || path.Contains("#")) return false;
            foreach (var c in path)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '/' || c == '-' || c == '_' || c == '.';
                if (!ok) return false;
            }
            return true;
        }
    }
}
