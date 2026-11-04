using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Where one hand is and how it is moving, every frame. This is the continuous data views can use (a cursor, ripples in water,
    /// bending plants); strikes and notes are only one possible reading of it. Coordinates are video-normalized: x 0..1 across the mirrored
    /// picture (0 = left), y 0..1 down.
    /// </summary>
    public readonly struct HandMotion
    {
        public int Id { get; }
        /// <summary>The hand was detected in the latest frame (false while it is briefly missing).</summary>
        public bool IsTracked { get; }
        public float Score { get; }
        public float X { get; }
        public float Y { get; }
        /// <summary>Palm length in video heights: a scale for speeds (a hand near the camera is bigger and moves more pixels).</summary>
        public float Size { get; }
        /// <summary>False for the first frame of a hand (there is no earlier position to measure against).</summary>
        public bool HasVelocity { get; }
        /// <summary>Normalized video units per second: x as a fraction of the width, y as a fraction of the height (down is positive).</summary>
        public float VelocityX { get; }
        public float VelocityY { get; }
        /// <summary>Speed in palm lengths per second (aspect corrected, so a circle in the air reads the same in every direction).</summary>
        public float SpeedPalms { get; }
        /// <summary>Downward speed in palm lengths per second (negative when moving up).</summary>
        public float DownSpeedPalms { get; }
        public double CaptureMs { get; }
        /// <summary>Changes whenever the hand's history restarts (it came back after being lost): treat the hand as a new one.</summary>
        public int Epoch { get; }
        /// <summary>Open hand or fist. <see cref="HandPose.Unknown"/> for a hand that was just seen (a new pose is taken over only after <see cref="PoseSettings.ConfirmFrames"/> pictures in a row) and for one that is half open.</summary>
        public HandPose Pose { get; }
        /// <summary>
        /// The hand plays (<see cref="HandPlayRule"/>): it has been tracked for <see cref="PoseSettings.ConfirmFrames"/> pictures and is not a fist, so an open hand and a hand whose pose stays unsure both play. The motion tracker
        /// decides; a hand that is made by hand (a test, a capture) plays unless its pose is a fist.
        /// </summary>
        public bool CanPlay { get; }

        /// <param name="canPlay">Whether the hand plays; null reads it from the pose (everything but a fist plays), for hands that do not come from the motion tracker.</param>
        public HandMotion(int id, bool isTracked, float score, float x, float y, float size, bool hasVelocity, float velocityX, float velocityY,
            float speedPalms, float downSpeedPalms, double captureMs, int epoch, HandPose pose, bool? canPlay = null)
        {
            Id = id; IsTracked = isTracked; Score = score; X = x; Y = y; Size = size; HasVelocity = hasVelocity;
            VelocityX = velocityX; VelocityY = velocityY; SpeedPalms = speedPalms; DownSpeedPalms = downSpeedPalms; CaptureMs = captureMs; Epoch = epoch; Pose = pose;
            CanPlay = canPlay ?? pose != HandPose.Fist;
        }

        public HandMotion WithTracked(bool isTracked) =>
            new HandMotion(Id, isTracked, Score, X, Y, Size, HasVelocity, VelocityX, VelocityY, SpeedPalms, DownSpeedPalms, CaptureMs, Epoch, Pose, CanPlay);
    }

    /// <summary>What views read each frame: the hands currently in the picture, with position and velocity.</summary>
    public interface IHandMotionView
    {
        /// <summary>Sequence number of the latest hand frame (0 before the first).</summary>
        long FrameSeq { get; }
        /// <summary>Number of hands detected in the latest frame (0..2).</summary>
        int Count { get; }
        /// <summary>The i-th detected hand of the latest frame, in id order.</summary>
        HandMotion Hand(int index);
        /// <summary>A hand by id, also while it is briefly missing (IsTracked tells). False once it has been lost for good.</summary>
        bool TryGetById(int id, out HandMotion motion);
    }

    /// <summary>
    /// Keeps a short history per hand id and turns frames into <see cref="HandMotion"/>. Velocity is measured over the last
    /// <see cref="HandInterpreterSettings.VelocityWindowMs"/>. Driven by frame timestamps only, so a replay gives the same values as the camera did.
    /// </summary>
    public sealed class HandMotionTracker : IHandMotionView
    {
        const int HistoryCapacity = 16;
        const float MsPerSecond = 1000f;

        sealed class Track
        {
            public readonly double[] Time = new double[HistoryCapacity];
            public readonly float[] X = new float[HistoryCapacity];
            public readonly float[] Y = new float[HistoryCapacity];
            public readonly float[] Size = new float[HistoryCapacity];
            public int Count;
            public int Head;
            public bool Present;
            public double LastSeenMs;
            public int Epoch;
            public HandMotion Current;
            /// <summary>The pose the hand is taken to have (changed only after enough pictures in a row), the one the pictures show now, and how many pictures in a row showed it.</summary>
            public HandPose Pose;
            public HandPose Pending;
            public int PendingCount;
            /// <summary>What the latest picture said by itself: its pose and the finger ratios it was read from (<see cref="TryGetPicture"/>).</summary>
            public HandPose PicturePose;
            public FingerRatios PictureFingers;
            /// <summary>How many pictures this hand has been tracked for since its history began (a new hand, or one that came back after a long gap, starts at 1).</summary>
            public int Pictures;

            public void Push(double time, float x, float y, float size)
            {
                Head = (Head + 1) % HistoryCapacity;
                Time[Head] = time;
                X[Head] = x;
                Y[Head] = y;
                Size[Head] = size;
                if (Count < HistoryCapacity) Count++;
            }
        }

        readonly HandSettingsHolder _settings;
        readonly Track[] _tracks = new Track[HandBufferLayout.MaxHands];
        readonly bool[] _seen = new bool[HandBufferLayout.MaxHands];
        readonly int[] _tracked = new int[HandBufferLayout.MaxHands];
        int _trackedCount;

        public HandMotionTracker(HandSettingsHolder settings)
        {
            _settings = settings;
            for (var i = 0; i < _tracks.Length; i++) _tracks[i] = new Track();
        }

        public long FrameSeq { get; private set; }
        public double CaptureMs { get; private set; }
        /// <summary>Width / height of the picture of the latest frame: x and y are fractions of different lengths, a distance needs this.</summary>
        public float Aspect { get; private set; } = 4f / 3f;
        public int Count => _trackedCount;

        public HandMotion Hand(int index)
        {
            if (index < 0 || index >= _trackedCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _tracks[_tracked[index]].Current;
        }

        public bool TryGetById(int id, out HandMotion motion)
        {
            if (id < 0 || id >= _tracks.Length || !_tracks[id].Present)
            {
                motion = default;
                return false;
            }
            motion = _tracks[id].Current;
            return true;
        }

        /// <summary>
        /// What the latest picture of the hand of that id said by itself, before the tracker's confirming (<c>--log-pose</c>): the pose that this one picture gives and the finger ratios it was read from.
        /// False for a hand that is not there.
        /// </summary>
        public bool TryGetPicture(int id, out FingerRatios fingers, out HandPose pose)
        {
            var known = id >= 0 && id < _tracks.Length && _tracks[id].Present;
            fingers = known ? _tracks[id].PictureFingers : default;
            pose = known ? _tracks[id].PicturePose : HandPose.Unknown;
            return known;
        }

        /// <summary>Forget every hand (a recording restarted, or the tracker was restarted).</summary>
        public void Reset()
        {
            for (var i = 0; i < _tracks.Length; i++)
            {
                var epoch = _tracks[i].Epoch + 1;
                _tracks[i] = new Track { Epoch = epoch };
            }
            _trackedCount = 0;
            FrameSeq = 0;
        }

        public void Update(in HandFrame frame)
        {
            var settings = _settings.Settings;
            var now = frame.CaptureMs;
            FrameSeq = frame.Seq;
            CaptureMs = now;
            Aspect = frame.Aspect;
            for (var i = 0; i < _seen.Length; i++) _seen[i] = false;

            for (var i = 0; i < frame.HandCount; i++)
            {
                var hand = frame.Hand(i);
                if (hand.Score < settings.MinScore || _seen[hand.Id]) continue;
                _seen[hand.Id] = true;
                Observe(_tracks[hand.Id], hand, frame.Aspect, now, settings, _settings.Pose.ConfirmFrames);
            }

            _trackedCount = 0;
            for (var id = 0; id < _tracks.Length; id++)
            {
                var track = _tracks[id];
                if (_seen[id]) { _tracked[_trackedCount++] = id; continue; }
                if (!track.Present) continue;
                track.Current = track.Current.WithTracked(false);
                if (now - track.LastSeenMs > settings.MaxGapMs || now < track.LastSeenMs) track.Present = false;
            }
        }

        static void Observe(Track track, in HandSample hand, float aspect, double now, in HandInterpreterSettings settings, int confirmFrames)
        {
            var gap = now - track.LastSeenMs;
            if (!track.Present || gap > settings.MaxGapMs || gap < 0)
            {
                track.Count = 0;
                track.Head = 0;
                track.Epoch++;
                track.Pose = HandPose.Unknown;   // a new hand: what its fingers do is not known until enough pictures agree
                track.Pending = HandPose.Unknown;
                track.PendingCount = 0;
                track.Pictures = 0;
            }
            track.Present = true;
            track.LastSeenMs = now;
            if (track.Pictures < int.MaxValue) track.Pictures++;
            track.Push(now, hand.X, hand.Y, hand.Size);
            track.PicturePose = hand.Pose;
            track.PictureFingers = hand.Fingers;
            TakePose(track, hand.Pose, confirmFrames);
            track.Current = Measure(track, hand, aspect, now, settings.VelocityWindowMs, HandPlayRule.IsEligible(track.Pose, track.Pictures, confirmFrames));
        }

        /// <summary>
        /// A hand takes a new pose over only when <paramref name="confirmFrames"/> pictures in a row show it: the picture of one hand is now and then wrong (a fist for one picture in the middle of a wave),
        /// and a wrong picture must neither stop the music nor end the piece. A picture of the pose the hand already has cancels a change that was on its way.
        /// </summary>
        static void TakePose(Track track, HandPose seen, int confirmFrames)
        {
            if (seen == track.Pose)
            {
                track.Pending = seen;
                track.PendingCount = 0;
                return;
            }
            track.PendingCount = seen == track.Pending ? track.PendingCount + 1 : 1;
            track.Pending = seen;
            if (track.PendingCount < confirmFrames) return;
            track.Pose = seen;
            track.PendingCount = 0;
        }

        static HandMotion Measure(Track track, in HandSample hand, float aspect, double now, double windowMs, bool canPlay)
        {
            var oldest = -1;
            for (var back = 1; back < track.Count; back++)
            {
                var index = (track.Head - back + HistoryCapacity) % HistoryCapacity;
                if (now - track.Time[index] > windowMs) break;
                oldest = index;
            }
            var dt = oldest < 0 ? 0.0 : (now - track.Time[oldest]) / MsPerSecond;
            if (oldest < 0 || dt <= 0)
            {
                return new HandMotion(hand.Id, true, hand.Score, hand.X, hand.Y, hand.Size, false, 0f, 0f, 0f, 0f, now, track.Epoch, track.Pose, canPlay);
            }

            var size = 0f;
            var samples = 0;
            for (var i = oldest; ; i = (i + 1) % HistoryCapacity)
            {
                size += track.Size[i];
                samples++;
                if (i == track.Head) break;
            }
            size /= samples;

            var vx = (float)((track.X[track.Head] - track.X[oldest]) / dt);
            var vy = (float)((track.Y[track.Head] - track.Y[oldest]) / dt);
            var speed = (float)Math.Sqrt((vx * aspect) * (vx * aspect) + vy * vy) / size;
            return new HandMotion(hand.Id, true, hand.Score, hand.X, hand.Y, hand.Size, true, vx, vy, speed, vy / size, now, track.Epoch, track.Pose, canPlay);
        }
    }
}
