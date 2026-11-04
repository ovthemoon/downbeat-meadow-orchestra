using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;

namespace Downbeat.Tests
{
    /// <summary>
    /// Builds hand frames the way the page tracker would: a hand is a palm center and a palm length, laid out as all 21 landmarks (the five palm points, the thumb and the four fingers, whose tips are
    /// <see cref="OpenRatio"/>, <see cref="HalfRatio"/> or <see cref="FistRatio"/> palm lengths from the wrist, which is what the hand pose reading measures).
    /// </summary>
    public static class HandTestData
    {
        public const double FrameMs = 1000.0 / 30.0;
        public const float PalmSize = 0.2f;
        /// <summary>The distance of a fingertip from the wrist, in wrist-to-middle-knuckle lengths: stretched out (the recorded demo hand measures 1.55 to 1.9), half curled (between the two thresholds), curled into a fist.</summary>
        public const float OpenRatio = 1.8f;
        public const float HalfRatio = 1.3f;
        public const float FistRatio = 0.9f;

        public readonly struct Pose
        {
            public int Id { get; }
            public float X { get; }
            public float Y { get; }
            public float Score { get; }
            public float Size { get; }
            /// <summary>How the fingers are held (an open hand unless a test says otherwise).</summary>
            public HandPose Shape { get; }

            public Pose(int id, float x, float y, float score = 0.95f, float size = PalmSize, HandPose shape = HandPose.Open)
            {
                Id = id; X = x; Y = y; Score = score; Size = size; Shape = shape;
            }
        }

        /// <summary>A hand with its fingers in a given pose, for tests of the pose: <see cref="PoseHand"/> makes one, <see cref="ReplayOf"/> and <see cref="PosedFrame"/> turn it into a recording or a tracker picture.</summary>
        public readonly struct PosedHand
        {
            public int Id { get; }
            public float X { get; }
            public float Y { get; }
            public float Size { get; }
            public HandPose Pose { get; }
            public float Score { get; }

            public PosedHand(int id, float x, float y, float size, HandPose pose, float score = 0.95f)
            {
                Id = id; X = x; Y = y; Size = size; Pose = pose; Score = score;
            }

            /// <summary>The hand as a line of a replay file: 21 x/y pairs, every one of them written.</summary>
            public ReplayHandDto ToReplayHand() => new ReplayHandDto { id = Id, s = Score, p = Landmarks(X, Y, Size, Pose) };
        }

        /// <summary>A hand at (x, y) of a given palm length with all 21 points written: <see cref="HandPose.Open"/> fingers 1.8 palm lengths from the wrist, <see cref="HandPose.Fist"/> 0.9, <see cref="HandPose.Unknown"/> 1.3 (half curled).</summary>
        public static PosedHand PoseHand(int id, float x, float y, float size, HandPose pose) => new PosedHand(id, x, y, size, pose);

        public static float RatioOf(HandPose pose) => pose == HandPose.Open ? OpenRatio : (pose == HandPose.Fist ? FistRatio : HalfRatio);

        /// <summary>
        /// The 21 landmarks of an upright hand as x0, y0, x1, y1 ... (the palm points where <see cref="Frame"/> has always put them, so the palm centre is exactly (x, y) and the palm length exactly <paramref name="size"/>
        /// in video heights). <paramref name="fingerRatios"/> are the distances of the tips of the index, middle, ring and little finger from the wrist in palm lengths; the knuckles of each finger lie on the line to its tip.
        /// <paramref name="aspect"/> is width / height: lengths are in video heights, so x is divided by it.
        /// </summary>
        public static float[] Landmarks(float x, float y, float size, float[] fingerRatios, float aspect = 1f)
        {
            var p = new float[ReplayFormat.CoordinatesPerHand];
            void Put(int landmark, float dx, float dy)
            {
                p[landmark * 2] = x + dx * size / aspect;
                p[landmark * 2 + 1] = y + dy * size;
            }

            const float WristDy = 0.8f;
            var knuckles = new[] { (HandBufferLayout.IndexBase, -0.32f), (HandBufferLayout.MiddleBase, 0f), (HandBufferLayout.RingBase, 0.16f), (HandBufferLayout.PinkyBase, 0.16f) };
            Put(HandBufferLayout.Wrist, 0f, WristDy);
            for (var finger = 0; finger < knuckles.Length; finger++)
            {
                var (knuckle, dx) = knuckles[finger];
                var length = (float)Math.Sqrt(dx * dx + 1f);               // the wrist is 1.0 below the knuckle line
                var directionX = dx / length;
                var directionY = -1f / length;
                for (var step = 0; step <= 3; step++)                       // the knuckle, two joints, the tip: evenly from the knuckle to the tip
                {
                    var along = length + (fingerRatios[finger] - length) * step / 3f;
                    Put(knuckle + step, directionX * along, WristDy + directionY * along);
                }
            }
            Put(HandBufferLayout.Wrist + 1, -0.15f, WristDy - 0.1f);        // the thumb: not read by the pose, kept out of the way of the fingers
            Put(HandBufferLayout.Wrist + 2, -0.35f, WristDy - 0.35f);
            Put(HandBufferLayout.Wrist + 3, -0.5f, WristDy - 0.55f);
            Put(HandBufferLayout.Wrist + 4, -0.6f, WristDy - 0.75f);
            return p;
        }

        public static float[] Landmarks(float x, float y, float size, HandPose pose, float aspect = 1f)
        {
            var ratio = RatioOf(pose);
            return Landmarks(x, y, size, new[] { ratio, ratio, ratio, ratio }, aspect);
        }

        /// <summary>A one-hand buffer (slot 0) with given finger ratios, for reading a pose straight from the buffer.</summary>
        public static double[] HandBuffer(float x, float y, float size, float[] fingerRatios, float aspect = 1f) =>
            HandBuffer(Landmarks(x, y, size, fingerRatios, aspect), aspect);

        /// <summary>A one-hand buffer (slot 0) of given landmarks (x0, y0, x1, y1 ...).</summary>
        public static double[] HandBuffer(float[] landmarks, float aspect = 1f)
        {
            var buffer = new double[HandBufferLayout.Length];
            buffer[HandBufferLayout.AspectIndex] = aspect;
            buffer[HandBufferLayout.HandCountIndex] = 1;
            WriteLandmarks(buffer, 0, 0, 0.95f, landmarks);
            return buffer;
        }

        static void WriteLandmarks(double[] buffer, int slot, int id, float score, float[] landmarks)
        {
            var b = HandBufferLayout.HandBase(slot);
            buffer[b] = id;
            buffer[b + 1] = score;
            for (var i = 0; i < HandBufferLayout.LandmarksPerHand; i++)
            {
                buffer[b + 2 + i * 3] = landmarks[i * 2];
                buffer[b + 3 + i * 3] = landmarks[i * 2 + 1];
            }
        }

        /// <summary>One tracker picture of hands in given poses (aspect 1), read back like the game does.</summary>
        public static HandFrame PosedFrame(long seq, double captureMs, PoseSettings pose, params PosedHand[] hands)
        {
            var buffer = HeaderOnlyBuffer(seq, captureMs, hands.Length);
            for (var slot = 0; slot < hands.Length; slot++)
            {
                var hand = hands[slot];
                WriteLandmarks(buffer, slot, hand.Id, hand.Score, Landmarks(hand.X, hand.Y, hand.Size, hand.Pose));
            }
            HandFrameReader.TryRead(buffer, captureMs + 12, out var frame, pose);
            return frame;
        }

        public static HandFrame PosedFrame(long seq, double captureMs, params PosedHand[] hands) => PosedFrame(seq, captureMs, null, hands);

        static double[] HeaderOnlyBuffer(long seq, double captureMs, int handCount)
        {
            var buffer = new double[HandBufferLayout.Length];
            buffer[HandBufferLayout.SeqIndex] = seq;
            buffer[HandBufferLayout.CaptureMsIndex] = captureMs;
            buffer[HandBufferLayout.InferEndMsIndex] = captureMs + 10;
            buffer[HandBufferLayout.StateIndex] = (int)TrackerState.Running;
            buffer[HandBufferLayout.HandCountIndex] = handCount;
            buffer[HandBufferLayout.AspectIndex] = 1.0;
            return buffer;
        }

        /// <summary>A recording of hands that are posed over time: <paramref name="hands"/> says which hands are there at a time (seconds from the start), <paramref name="fps"/> pictures a second (aspect 1).</summary>
        public static ReplayDto ReplayOf(Func<double, PosedHand[]> hands, double seconds, double fps)
        {
            var frames = new List<ReplayFrameDto>();
            var count = (int)Math.Floor(seconds * fps) + 1;
            for (var i = 0; i < count; i++)
            {
                var t = i / fps;
                frames.Add(new ReplayFrameDto { t = t * 1000.0, h = hands(t).Select(h => h.ToReplayHand()).ToArray() });
            }
            return new ReplayDto { format = ReplayFormat.Name, version = ReplayFormat.SupportedVersion, aspect = 1f, mirrored = true, frames = frames.ToArray() };
        }

        /// <summary>
        /// The recording as a slower tracker would have made it: the first picture, and then each picture that comes at least 1 / <paramref name="fps"/> seconds after the last one kept (a 30 a second recording
        /// thinned to 6 keeps every fifth). The pictures keep their own times.
        /// </summary>
        public static ReplayDto ThinnedTo(this ReplayDto replay, double fps)
        {
            if (!(fps > 0.0)) throw new ArgumentOutOfRangeException(nameof(fps), fps, "must be positive");
            const double Tolerance = 1e-6;   // a picture 33.3 ms after 33.3 ms is a picture on time
            var gapMs = 1000.0 / fps;
            var kept = new List<ReplayFrameDto>();
            foreach (var frame in replay.frames)
            {
                if (kept.Count > 0 && frame.t - kept[kept.Count - 1].t < gapMs - 1000.0 * Tolerance) continue;
                kept.Add(frame);
            }
            return new ReplayDto { format = replay.format, version = replay.version, note = replay.note, aspect = replay.aspect, mirrored = replay.mirrored, frames = kept.ToArray() };
        }

        /// <summary>Writes a hand buffer (aspect 1, so palm length is exactly <see cref="Pose.Size"/>) and reads it back like the game does.</summary>
        public static HandFrame Frame(long seq, double captureMs, params Pose[] hands)
        {
            var buffer = HeaderOnlyBuffer(seq, captureMs, hands.Length);
            for (var slot = 0; slot < hands.Length; slot++) WriteLandmarks(buffer, slot, hands[slot].Id, hands[slot].Score, Landmarks(hands[slot].X, hands[slot].Y, hands[slot].Size, hands[slot].Shape));
            HandFrameReader.TryRead(buffer, captureMs + 12, out var frame);
            return frame;
        }

        /// <summary>
        /// One hand at (x, y0): still for <paramref name="stillFrames"/> frames, then moving down at constant
        /// <paramref name="speed"/> (palm lengths per second) for <paramref name="moveFrames"/> frames, then still again.
        /// </summary>
        public static List<HandFrame> StrikeFrames(float x, float y0, float speed, int stillFrames = 6, int moveFrames = 6, int tailFrames = 12, int id = 0, long firstSeq = 1, double firstMs = 0, HandPose shape = HandPose.Open)
        {
            var frames = new List<HandFrame>();
            var perFrame = speed * PalmSize * (float)(FrameMs / 1000.0);
            var y = y0;
            var seq = firstSeq;
            var t = firstMs;
            for (var i = 0; i < stillFrames + moveFrames + tailFrames; i++)
            {
                if (i >= stillFrames && i < stillFrames + moveFrames) y += perFrame;
                frames.Add(Frame(seq++, t, new Pose(id, x, y, shape: shape)));
                t += FrameMs;
            }
            return frames;
        }

        /// <summary>The motion tracker and the interpreter on top of it, wired the way <see cref="Downbeat.Controls.HandPlayInput"/> wires them.</summary>
        public sealed class Rig
        {
            public HandSettingsHolder Settings { get; } = new HandSettingsHolder();
            public HandMotionTracker Motion { get; }
            public HandInterpreter Interpreter { get; } = new HandInterpreter();

            public Rig(int pitchSteps, HandSettingsDto dto)
            {
                Settings.Set(dto);
                Motion = new HandMotionTracker(Settings);
                Interpreter.Configure(Settings.Settings, pitchSteps);
            }

            public void Reset()
            {
                Motion.Reset();
                Interpreter.Reset();
            }
        }

        public static List<HandStrike> Run(Rig rig, IEnumerable<HandFrame> frames)
        {
            var strikes = new List<HandStrike>();
            foreach (var frame in frames)
            {
                rig.Motion.Update(frame);
                rig.Interpreter.Process(frame, rig.Motion, strikes);
            }
            return strikes;
        }

        public static Rig NewInterpreter(int pitchSteps = 10, HandSettingsDto settings = null) => new Rig(pitchSteps, settings);

        /// <summary>The same hand as <see cref="Frame"/> in replay-file form (21 x/y pairs, an open hand).</summary>
        public static ReplayHandDto ReplayHand(int id, float x, float y, float size = PalmSize, float score = 0.95f) => new PosedHand(id, x, y, size, HandPose.Open, score).ToReplayHand();

        /// <summary>A replay of strikes: each entry is (x, startY, speed in palm lengths per second); the hand leaves the picture between strikes.</summary>
        public static ReplayDto ReplayOfStrikes(params (float x, float y, float speed)[] strikes)
        {
            var frames = new List<ReplayFrameDto>();
            var t = 0.0;
            for (var i = 0; i < 6; i++, t += FrameMs) frames.Add(new ReplayFrameDto { t = t, h = new ReplayHandDto[0] });
            foreach (var strike in strikes)
            {
                var y = strike.y;
                var perFrame = strike.speed * PalmSize * (float)(FrameMs / 1000.0);
                for (var i = 0; i < 36; i++, t += FrameMs)
                {
                    if (i >= 6 && i < 12) y += perFrame;
                    frames.Add(new ReplayFrameDto { t = t, h = new[] { ReplayHand(0, strike.x, y) } });
                }
                for (var i = 0; i < 10; i++, t += FrameMs) frames.Add(new ReplayFrameDto { t = t, h = new ReplayHandDto[0] }); // the hand leaves between strikes
            }
            for (var i = 0; i < 12; i++, t += FrameMs) frames.Add(new ReplayFrameDto { t = t, h = new ReplayHandDto[0] });
            return new ReplayDto { format = ReplayFormat.Name, version = ReplayFormat.SupportedVersion, aspect = 1f, mirrored = true, frames = frames.ToArray() };
        }
    }
}
