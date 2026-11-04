using System.Collections;
using System.Globalization;
using System.IO;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--frame-dump DIR [--frame-dump-fps 20] [--frame-dump-seconds 10] [--frame-dump-skip 4] [--capture-size 1920x1080]</c>: saves the pictures of a run at an exact, even rate as PNG files
    /// (measuring how the grass moves, not how fast the machine is). Unity's clock is set to advance 1 / fps per frame (<c>Time.captureFramerate</c>), so the wind, which runs on that clock, is the
    /// same on every run and every machine however long a picture takes; the sound (use <c>--mute</c>) and anything on the audio clock are not part of it. Each picture is the meadow camera drawn
    /// off-screen at the window's own steps (see <see cref="MeadowCapture"/>). The run ends by itself. Use with <c>--no-record</c>.
    /// <c>--frame-dump-beats BPM</c> stands in for a played piece: on every beat the beat's sweep and a ring (cycling over three places on the field) start, on the same Unity clock.
    /// <c>--frame-dump-hand</c> stands in for a conducting hand: one hand sweeps to and fro over the field (from the first frame, so the wind has settled), on the Unity clock counted from the first frame, so the hand is at the same place in the same frame on every run (the wind itself runs on the clock the app started with, which differs by a fraction of a second between runs).
    /// </summary>
    public sealed class FrameDump : MonoBehaviour
    {
        const float DefaultFps = 20f;
        const double DefaultSeconds = 10.0;
        const double DefaultSkipSec = 4.0;
        const int DefaultWidth = 1920;
        const int DefaultHeight = 1080;

        string _dir;
        float _fps;
        double _seconds;
        double _skipSec;
        int _width;
        int _height;
        const int BeatsPerBar = 4;
        const float RingVelocity = 0.8f;
        const float RingDepth = 5f;
        const float RingSpreadX = 3f;
        const float BeatEnergy = 0.5f;
        const float HandCentreX = 0.5f;          // video-normalized: the hand sweeps 0.22..0.78 across, a third of the way into the field
        const float HandSweepX = 0.28f;
        const float HandY = 0.62f;
        const float HandSweepHz = 0.7f;
        const float HandPalm = 0.2f;

        Camera _camera;
        MeadowStage _stage;
        double _beatSec;
        ScriptedHand _hand;

        public static void Begin(LaunchArgs args, MeadowStage stage)
        {
            var dir = args.Get("frame-dump");
            if (string.IsNullOrEmpty(dir)) return;
            var host = new GameObject("FrameDump").AddComponent<FrameDump>();
            host._dir = Path.GetFullPath(dir);
            host._fps = args.TryGetDouble("frame-dump-fps", out var fps) && fps >= 1.0 ? (float)fps : DefaultFps;
            host._seconds = args.TryGetDouble("frame-dump-seconds", out var seconds) && seconds > 0.0 ? seconds : DefaultSeconds;
            host._skipSec = args.TryGetDouble("frame-dump-skip", out var skip) && skip >= 0.0 ? skip : DefaultSkipSec;
            if (!MeadowCapture.TryParseSize(args.Get("capture-size"), out host._width, out host._height))
            {
                host._width = DefaultWidth;
                host._height = DefaultHeight;
            }
            host._camera = stage.Camera;
            host._stage = stage;
            host._beatSec = args.TryGetDouble("frame-dump-beats", out var bpm) && bpm >= 20.0 ? 60.0 / bpm : 0.0;
            if (args.Has("frame-dump-hand")) host._hand = new ScriptedHand();
            host.StartCoroutine(host.Run());
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory(_dir);
            Time.captureFramerate = Mathf.RoundToInt(_fps);
            var runStart = Time.timeAsDouble;   // the hand's clock starts here: from now on Unity's time steps exactly 1 / fps per frame, so frame k has the same hand whatever time the app started at
            var skipFrames = Mathf.RoundToInt((float)_skipSec * _fps);
            var frames = Mathf.RoundToInt((float)_seconds * _fps);
            for (var i = 0; i < skipFrames; i++)
            {
                _hand?.SetTime(Time.timeAsDouble - runStart);
                if (_hand != null) _stage.HandsOverride = _hand;
                yield return null;
            }

            var startedAt = Time.timeAsDouble;
            var nextBeat = startedAt;
            var beat = 0;
            for (var i = 0; i < frames; i++)
            {
                if (_beatSec > 0.0 && Time.timeAsDouble >= nextBeat)
                {
                    _stage.AddBeatSweep(BeatEnergy, beat % BeatsPerBar == 0);
                    _stage.AddNoteRadial(new FieldPoint((beat % 3 - 1) * RingSpreadX, RingDepth), RingVelocity);
                    nextBeat += _beatSec;
                    beat++;
                }
                if (_hand != null)
                {
                    _hand.SetTime(Time.timeAsDouble - runStart);
                    _stage.HandsOverride = _hand;
                }
                yield return new WaitForEndOfFrame();
                var picture = MeadowCapture.Render(_camera, _width, _height);
                File.WriteAllBytes(Path.Combine(_dir, $"f{i:D5}.png"), picture.EncodeToPNG());
                Destroy(picture);
            }
            File.WriteAllText(Path.Combine(_dir, "frames.txt"), string.Format(CultureInfo.InvariantCulture, "fps={0}\nframes={1}\nwidth={2}\nheight={3}\nsceneTimeFirstFrame={4:R}\nquality={5}\n",
                _fps, frames, _width, _height, startedAt, QualitySettings.names[QualitySettings.GetQualityLevel()]));
            Debug.Log($"[FrameDump] {frames} pictures of {_width}x{_height} at {_fps} per second in {_dir}");
            Time.captureFramerate = 0;
            Application.Quit();
        }

        /// <summary>One hand that sweeps to and fro (a sine in x, still in y) at a given time: the position and the velocity are exact, not measured.</summary>
        sealed class ScriptedHand : IHandMotionView
        {
            HandMotion _hand;

            public long FrameSeq { get; private set; } = 1;
            public int Count => 1;
            public HandMotion Hand(int index) => _hand;

            public bool TryGetById(int id, out HandMotion motion)
            {
                motion = _hand;
                return id == 0;
            }

            public void SetTime(double seconds)
            {
                var phase = 2.0 * System.Math.PI * HandSweepHz * seconds;
                var x = HandCentreX + HandSweepX * (float)System.Math.Sin(phase);
                var velocityX = HandSweepX * 2f * Mathf.PI * HandSweepHz * (float)System.Math.Cos(phase);
                _hand = new HandMotion(0, true, 1f, x, HandY, HandPalm, true, velocityX, 0f, System.Math.Abs(velocityX) / HandPalm, 0f, seconds * 1000.0, 0, HandPose.Open);
                FrameSeq++;
            }
        }
    }
}
