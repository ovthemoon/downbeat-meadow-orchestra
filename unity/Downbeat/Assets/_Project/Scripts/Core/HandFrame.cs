using System;

namespace Downbeat.Core
{
    /// <summary>State of the page-side hand tracker, as written into the hand buffer header. Numeric values are part of the buffer format.</summary>
    public enum TrackerState
    {
        Off = 0,
        Starting = 1,
        Running = 2,
        CameraDenied = 3,
        CameraMissing = 4,
        CameraError = 5,
        ModelFailed = 6,
    }

    /// <summary>
    /// Layout of the hand buffer that the page fills and Unity pulls every frame (double[136]).
    /// Header: [0] seq, [1] capture time ms, [2] inference end ms, [3] tracker state, [4] hand count.
    /// Then up to 2 hands of [id, score, x0,y0,z0 ... x20,y20,z20] (65 values each), then [135] video aspect (width / height).
    /// Coordinates are normalized to the video frame, x already mirrored (0 = left as seen in a mirror), y down.
    /// The same layout is in web/.../hand-buffer.js; a test in each language pins the numbers.
    /// </summary>
    public static class HandBufferLayout
    {
        public const int SeqIndex = 0;
        public const int CaptureMsIndex = 1;
        public const int InferEndMsIndex = 2;
        public const int StateIndex = 3;
        public const int HandCountIndex = 4;
        public const int HeaderLength = 5;
        public const int MaxHands = 2;
        public const int LandmarksPerHand = 21;
        public const int HandStride = 2 + LandmarksPerHand * 3;
        public const int AspectIndex = HeaderLength + MaxHands * HandStride;
        public const int Length = AspectIndex + 1;

        // MediaPipe hand landmark indices that make up the palm: wrist and the four finger base joints.
        public const int Wrist = 0;
        public const int IndexBase = 5;
        public const int MiddleBase = 9;
        public const int RingBase = 13;
        public const int PinkyBase = 17;

        public static int HandBase(int handSlot) => HeaderLength + handSlot * HandStride;
    }

    /// <summary>One hand reduced to what the game uses: palm center, palm size as the scale for speeds, and whether the hand is open or a fist.</summary>
    public readonly struct HandSample
    {
        public int Id { get; }
        public float Score { get; }
        /// <summary>Palm center, 0..1 across the (mirrored) video, 0 = left.</summary>
        public float X { get; }
        /// <summary>Palm center, 0..1 down the video, 0 = top.</summary>
        public float Y { get; }
        /// <summary>Palm length in video heights (so it is comparable in x and y). Speeds are divided by it: a closer hand moves more pixels for the same gesture.</summary>
        public float Size { get; }
        /// <summary>Open hand or fist as read from this one picture (<see cref="HandPoseClassifier"/>); the motion tracker only takes a pose over after several pictures in a row.</summary>
        public HandPose Pose { get; }
        /// <summary>The finger ratios that <see cref="Pose"/> was read from, for the diagnostic <c>--log-pose</c> (a hand made by hand, in a test, has none: <see cref="FingerRatios.IsKnown"/> false).</summary>
        public FingerRatios Fingers { get; }

        public HandSample(int id, float score, float x, float y, float size, HandPose pose, FingerRatios fingers = default)
        {
            Id = id; Score = score; X = x; Y = y; Size = size; Pose = pose; Fingers = fingers;
        }
    }

    /// <summary>One tracker result. A readonly struct holding both hands inline so that reading a frame allocates nothing.</summary>
    public readonly struct HandFrame
    {
        readonly HandSample _first;
        readonly HandSample _second;

        public long Seq { get; }
        /// <summary>Time the camera frame was captured, ms on the page clock (replay: ms since the start of the recording).</summary>
        public double CaptureMs { get; }
        public double InferEndMs { get; }
        /// <summary>When Unity read this frame from the page, ms on the page clock. NaN for replay.</summary>
        public double ReceivedMs { get; }
        public TrackerState State { get; }
        public float Aspect { get; }
        public int HandCount { get; }

        public HandFrame(long seq, double captureMs, double inferEndMs, double receivedMs, TrackerState state, float aspect, int handCount, HandSample first, HandSample second)
        {
            Seq = seq; CaptureMs = captureMs; InferEndMs = inferEndMs; ReceivedMs = receivedMs; State = state; Aspect = aspect;
            HandCount = handCount < 0 ? 0 : (handCount > HandBufferLayout.MaxHands ? HandBufferLayout.MaxHands : handCount);
            _first = first;
            _second = second;
        }

        public HandSample Hand(int index)
        {
            if (index < 0 || index >= HandCount) throw new ArgumentOutOfRangeException(nameof(index));
            return index == 0 ? _first : _second;
        }
    }

    /// <summary>Parses the hand buffer. The buffer comes from outside Unity (the page), so every value is checked.</summary>
    public static class HandFrameReader
    {
        const float MinSize = 0.02f;
        const float MaxAbsCoordinate = 4f;
        const float DefaultAspect = 4f / 3f;

        public static TrackerState ReadState(double[] buffer)
        {
            if (buffer == null || buffer.Length < HandBufferLayout.Length) return TrackerState.Off;
            var raw = buffer[HandBufferLayout.StateIndex];
            if (double.IsNaN(raw) || raw < 0 || raw > (int)TrackerState.ModelFailed) return TrackerState.CameraError;
            return (TrackerState)(int)raw;
        }

        /// <summary>False when the buffer is missing or too short. Hands with bad ids or non-finite coordinates are skipped.</summary>
        /// <param name="pose">The thresholds that tell an open hand from a fist; null reads with the defaults.</param>
        public static bool TryRead(double[] buffer, double receivedMs, out HandFrame frame, PoseSettings pose = null)
        {
            frame = default;
            if (buffer == null || buffer.Length < HandBufferLayout.Length) return false;

            var seqRaw = buffer[HandBufferLayout.SeqIndex];
            if (!IsUsable(seqRaw) || !IsUsable(buffer[HandBufferLayout.CaptureMsIndex]) || !IsUsable(buffer[HandBufferLayout.InferEndMsIndex])) return false;

            var aspect = ReadAspect(buffer);
            var declared = (int)buffer[HandBufferLayout.HandCountIndex];
            declared = declared < 0 ? 0 : (declared > HandBufferLayout.MaxHands ? HandBufferLayout.MaxHands : declared);

            HandSample first = default, second = default;
            var kept = 0;
            for (var slot = 0; slot < declared; slot++)
            {
                if (!TryReadHand(buffer, slot, aspect, pose, out var sample)) continue;
                if (kept == 0) first = sample; else second = sample;
                kept++;
            }

            frame = new HandFrame((long)seqRaw, buffer[HandBufferLayout.CaptureMsIndex], buffer[HandBufferLayout.InferEndMsIndex],
                receivedMs, ReadState(buffer), aspect, kept, first, second);
            return true;
        }

        /// <summary>Width / height of the picture the buffer says it has (a missing or absurd value reads as 4:3).</summary>
        public static float ReadAspect(double[] buffer)
        {
            if (buffer == null || buffer.Length < HandBufferLayout.Length) return DefaultAspect;
            var raw = buffer[HandBufferLayout.AspectIndex];
            return IsUsable(raw) && raw > 0.1 && raw < 10.0 ? (float)raw : DefaultAspect;
        }

        static bool TryReadHand(double[] buffer, int slot, float aspect, PoseSettings pose, out HandSample sample)
        {
            sample = default;
            var baseIndex = HandBufferLayout.HandBase(slot);
            var id = (int)buffer[baseIndex];
            if (id < 0 || id >= HandBufferLayout.MaxHands) return false;
            var score = buffer[baseIndex + 1];
            if (!IsUsable(score)) return false;

            double sumX = 0, sumY = 0;
            if (!TryAddPalm(buffer, baseIndex, HandBufferLayout.Wrist, ref sumX, ref sumY)) return false;
            if (!TryAddPalm(buffer, baseIndex, HandBufferLayout.IndexBase, ref sumX, ref sumY)) return false;
            if (!TryAddPalm(buffer, baseIndex, HandBufferLayout.MiddleBase, ref sumX, ref sumY)) return false;
            if (!TryAddPalm(buffer, baseIndex, HandBufferLayout.RingBase, ref sumX, ref sumY)) return false;
            if (!TryAddPalm(buffer, baseIndex, HandBufferLayout.PinkyBase, ref sumX, ref sumY)) return false;

            var length = Distance(buffer, baseIndex, HandBufferLayout.Wrist, HandBufferLayout.MiddleBase, aspect);
            var width = Distance(buffer, baseIndex, HandBufferLayout.IndexBase, HandBufferLayout.PinkyBase, aspect);
            var size = Math.Max(MinSize, Math.Max(length, width));
            var shape = HandPoseClassifier.Classify(buffer, baseIndex, aspect, pose);
            sample = new HandSample(id, (float)score, (float)(sumX / 5.0), (float)(sumY / 5.0), size, shape, HandPoseClassifier.Measure(buffer, baseIndex, aspect));
            return true;
        }

        static bool TryAddPalm(double[] buffer, int handBase, int landmark, ref double sumX, ref double sumY)
        {
            if (!IsUsableLandmark(buffer, handBase, landmark)) return false;
            sumX += buffer[handBase + 2 + landmark * 3];
            sumY += buffer[handBase + 3 + landmark * 3];
            return true;
        }

        /// <summary>Distance between two landmarks in video heights (x is scaled by the aspect ratio).</summary>
        internal static float Distance(double[] buffer, int handBase, int a, int b, float aspect)
        {
            var dx = (buffer[handBase + 2 + a * 3] - buffer[handBase + 2 + b * 3]) * aspect;
            var dy = buffer[handBase + 3 + a * 3] - buffer[handBase + 3 + b * 3];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        internal static bool IsUsable(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>True when both coordinates of the landmark are finite and within the range the reader accepts.</summary>
        internal static bool IsUsableLandmark(double[] buffer, int handBase, int landmark)
        {
            var x = buffer[handBase + 2 + landmark * 3];
            var y = buffer[handBase + 3 + landmark * 3];
            return IsUsable(x) && IsUsable(y) && Math.Abs(x) <= MaxAbsCoordinate && Math.Abs(y) <= MaxAbsCoordinate;
        }
    }
}
