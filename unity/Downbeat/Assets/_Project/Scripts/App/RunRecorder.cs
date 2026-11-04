using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Downbeat.Audio;
using Downbeat.Core;
using Downbeat.Meadow;
using Downbeat.Record;
using UnityEngine;

namespace Downbeat.App
{
    [Serializable]
    public sealed class RunRecordingDto
    {
        public string note;
        public int audioSampleRate;
        /// <summary>Audio-clock time (the time notes are scheduled in) of the first recorded sample: sample i of audio.wav was mixed at audioFirstSampleTime + i / audioSampleRate.</summary>
        public double audioFirstSampleTime;
        public int audioFrames;
        public float audioPeak;
        public int audioGaps;
        public int audioClippedSamples;
        public int frames;
        public int frameWidth;
        public int frameHeight;
        public double firstFrameVisualNow;
        public double lastFrameVisualNow;
        public float averageFps;
        public float slowestFrameMs;
        /// <summary>The app was closed (or the recorder torn down) before the requested time was up: the files hold what was recorded until that moment.</summary>
        public bool endedEarly;
        /// <summary>Real seconds from the start of the recording to its end (the requested time, a little over, when it was not cut short).</summary>
        public float recordedSeconds;
        /// <summary>
        /// Wall clock, UTC, ISO 8601 with milliseconds (<c>2026-11-02T01:23:45.678Z</c>), at which the audio thread mixed the first block of audio.wav, that is sample 0. It is when the sound was made, not when it was heard:
        /// to line this recording up with another one by its wall-clock stamp use <see cref="videoZeroUtc"/>, not this.
        /// </summary>
        public string startedUtc;
        /// <summary>
        /// How long after it is mixed a sample leaves the speakers, as the audio engine estimates it (the buffer length times the buffer count over the sample rate; the engine's Now minus its HeardNow). It is an estimate of the output buffers,
        /// not a measurement of the sound card or the speakers. The pictures are stamped on the heard timeline (VisualNow is Now minus this), so the video is shifted by it against <see cref="startedUtc"/>.
        /// </summary>
        public double outputLatencySec;
        /// <summary>
        /// <see cref="startedUtc"/> plus <see cref="outputLatencySec"/>, in the same format: the wall clock at which second 0 of the video that tools/assemble_run_video.py makes from this folder (with <c>--start 0</c>) is on the screen and in the speakers;
        /// second S of the video is at this plus S seconds. What is left over is not corrected and is random: the first block is stamped when the audio thread starts mixing it, which is only known to one audio block (about 20 ms with Unity's default buffer);
        /// the system clock steps in about 1 ms on macOS and up to about 16 ms on Windows when nothing raised the timer resolution; the latency is the engine's estimate. Altogether a few tens of milliseconds: good to line up with a webcam video by eye, not frame exact.
        /// </summary>
        public string videoZeroUtc;
    }

    /// <summary>
    /// <c>--record-dir DIR --record-seconds N [--record-fps F] [--record-size WxH] [--record-frames 0] [--record-silent]</c>: records the sound of the run (the final mix, first channel, as a 16-bit WAV) and, unless
    /// switched off, a picture of this app's own frame about F times a second as JPEG files (with <c>--record-size</c> the meadow camera is drawn into an off-screen picture of exactly that size, whatever the window is), each stamped with the audio time that was being heard when it was drawn
    /// (<see cref="IAudioEngine.VisualNow"/>), so that sound and picture can be put together afterwards exactly, whatever frame rate the capture reached.
    /// <c>--record-silent</c> zeroes the speakers after the copy (a recording run that makes no noise). The run ends by itself when the time is up.
    /// <c>--record-source record</c> takes the pictures from the record screen (drawn as the second display shows it, <c>--record-size</c> or 1280x720) instead of the meadow: a clip of the record screen at real speed.
    /// If the app is closed before the time is up (the window is closed, <c>--quit-after</c> comes first) the recording is finished at that moment with what it has: the same three files, <c>recording.json</c> saying <c>endedEarly</c>.
    /// A file that cannot be written (the folder is gone, the disk is full) is logged with its path and does not stop the others; when the time is up the app still quits.
    /// </summary>
    public sealed class RunRecorder
    {
        const int JpegQuality = 90;
        const float DefaultFps = 30f;
        const double AudioTailSec = 0.5;
        const int DefaultRecordScreenWidth = 1280;
        const int DefaultRecordScreenHeight = 720;
        const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        readonly IAudioEngine _audio;
        readonly CoroutineRunner _runner;
        readonly Func<object> _nextPicture;
        readonly Action _quit;
        readonly string _dir;
        readonly double _seconds;
        readonly float _frameInterval;
        readonly bool _withFrames;
        readonly bool _silent;
        readonly bool _hasSize;
        readonly int _sizeWidth;
        readonly int _sizeHeight;
        readonly StringBuilder _frameTimes = new StringBuilder(64 * 1024);
        readonly BlockingCollection<(string Path, byte[] Bytes)> _writeQueue = new BlockingCollection<(string, byte[])>(64);
        RecordScreenView _recordScreen;
        AudioOutputTap _tap;
        Thread _writer;
        double _startedAtReal;
        int _frameCount;
        int _width;
        int _height;
        double _firstVisual = double.NaN;
        double _lastVisual;
        float _slowestFrame;
        double _lastCaptureReal = double.NegativeInfinity;
        DateTime _startedUtcAtStart;
        DateTime _startedUtc;
        bool _isStarted;
        bool _isStartLogged;
        bool _isClosed;
        int _failedFiles;
        int _failedPictures;

        /// <param name="quit">What closes the app when the time is up (<c>Application.Quit</c> by default); a test counts the calls.</param>
        /// <param name="nextPicture">What the recording loop waits for between two pictures; the end of the frame by default (the screen can only be read then). A test in the editor's batch mode passes <c>() =&gt; null</c> (the next frame): that mode never reaches the end of a frame.</param>
        public RunRecorder(IAudioEngine audio, CoroutineRunner runner, LaunchArgs args, Func<object> nextPicture = null, Action quit = null)
        {
            _audio = audio;
            _runner = runner;
            _quit = quit ?? Application.Quit;
            _nextPicture = nextPicture ?? (() => new WaitForEndOfFrame());
            _dir = Path.GetFullPath(args.Get("record-dir"));
            _seconds = args.TryGetDouble("record-seconds", out var seconds) ? seconds : 30.0;
            var fps = args.TryGetDouble("record-fps", out var f) ? (float)f : DefaultFps;
            _frameInterval = 1f / Mathf.Max(1f, fps);
            _withFrames = args.Get("record-frames") != "0";
            _silent = args.Has("record-silent");
            _hasSize = MeadowCapture.TryParseSize(args.Get("record-size"), out _sizeWidth, out _sizeHeight);
        }

        /// <summary>The recording is over: it ended by itself or was finished early, and the files were written (or what stopped a file was logged).</summary>
        public bool IsFinished { get; private set; }

        /// <summary>ISO 8601 UTC with milliseconds, e.g. <c>2026-11-02T01:23:45.678Z</c>: how recording.json and the log write the start of the recording.</summary>
        public static string FormatUtc(DateTime utc) => utc.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);

        /// <summary>The wall clock at which second 0 of the video is heard: the time sample 0 was mixed plus the output latency (<see cref="RunRecordingDto.videoZeroUtc"/>).</summary>
        public static DateTime VideoZeroUtc(DateTime startedUtc, double outputLatencySec) => startedUtc.AddTicks((long)Math.Round(outputLatencySec * TimeSpan.TicksPerSecond));

        /// <summary>The pictures are the record screen's, not the meadow's (<c>--record-source record</c>).</summary>
        public static bool WantsRecordScreen(LaunchArgs args) => args.Get("record-source") == "record" && !string.IsNullOrEmpty(args.Get("record-dir"));

        /// <summary><c>--record-source record</c> was given but there is no recorder (<c>--record-dir</c> is missing): the option does nothing, and the app says so instead of switching the screen to a picture nobody takes.</summary>
        public static bool RecordSourceIgnored(LaunchArgs args) => args.Get("record-source") == "record" && string.IsNullOrEmpty(args.Get("record-dir"));

        public void UseRecordScreen(RecordScreenView view) => _recordScreen = view;

        public void Start()
        {
            Directory.CreateDirectory(_dir);
            if (_withFrames) Directory.CreateDirectory(Path.Combine(_dir, "frames"));
            var listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            if (listener == null) { Debug.LogError("[RunRecorder] no AudioListener to tap"); IsFinished = true; return; }
            _tap = listener.gameObject.AddComponent<AudioOutputTap>();
            _tap.MuteOutput = _silent;
            _tap.Begin((int)Math.Ceiling((_seconds + AudioTailSec) * AudioSettings.outputSampleRate));
            _startedAtReal = Time.realtimeSinceStartupAsDouble;
            _startedUtcAtStart = DateTime.UtcNow;
            _isStarted = true;
            if (_withFrames)
            {
                _writer = new Thread(WriteLoop) { IsBackground = true, Name = "RunRecorder writer" };
                _writer.Start();
            }
            _runner.ApplicationQuitting += FinishEarly;
            _runner.Destroyed += FinishEarly;
            _runner.StartCoroutine(Record());
            Debug.Log($"[RunRecorder] recording {_seconds:0.#} s to {_dir} (frames: {_withFrames})");
        }

        IEnumerator Record()
        {
            while (!_isClosed && Time.realtimeSinceStartupAsDouble - _startedAtReal < _seconds)
            {
                yield return _nextPicture();
                if (_isClosed) yield break;
                LogStartOnce(useStartOfRecorder: false);
                if (_withFrames) CaptureFrame();
            }
            Finish(endedEarly: false);
        }

        /// <summary>
        /// Finishes the recording now, with what has been captured so far: the app is being closed (<see cref="CoroutineRunner.ApplicationQuitting"/>) or the recorder's runner is destroyed before the time is up.
        /// Writes <c>audio.wav</c>, <c>frames.csv</c> and <c>recording.json</c> exactly as the end of the time does, <c>endedEarly</c> set. Does nothing when the recording was already finished, so any number of calls is safe.
        /// </summary>
        public void FinishEarly() => Finish(endedEarly: true);

        void CaptureFrame()
        {
            if (_isClosed) return;
            var real = Time.realtimeSinceStartupAsDouble;
            if (real - _lastCaptureReal < _frameInterval) return;
            var started = real;
            _lastCaptureReal = real;

            var visual = _audio.VisualNow;
            var texture = _recordScreen != null ? _recordScreen.RenderPicture(_hasSize ? _sizeWidth : DefaultRecordScreenWidth, _hasSize ? _sizeHeight : DefaultRecordScreenHeight, visual)
                : _hasSize && Camera.main != null ? MeadowCapture.Render(Camera.main, _sizeWidth, _sizeHeight) : ScreenCapture.CaptureScreenshotAsTexture();
            _width = texture.width;
            _height = texture.height;
            var jpeg = ImageConversion.EncodeToJPG(texture, JpegQuality);
            UnityEngine.Object.Destroy(texture);

            _frameCount++;
            if (double.IsNaN(_firstVisual)) _firstVisual = visual;
            _lastVisual = visual;
            _frameTimes.Append(_frameCount.ToString(CultureInfo.InvariantCulture)).Append(',').Append(visual.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            _writeQueue.Add((Path.Combine(_dir, "frames", $"f{_frameCount:D5}.jpg"), jpeg));
            _slowestFrame = Mathf.Max(_slowestFrame, (float)((Time.realtimeSinceStartupAsDouble - started) * 1000.0));
        }

        /// <summary>Writes the pictures. A picture that cannot be written is counted (the first is logged) and the loop goes on: a thread that died would leave the queue full and the main thread waiting for it for ever.</summary>
        void WriteLoop()
        {
            foreach (var (path, bytes) in _writeQueue.GetConsumingEnumerable())
            {
                try { File.WriteAllBytes(path, bytes); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    if (Interlocked.Increment(ref _failedPictures) == 1) Debug.LogError($"[RunRecorder] could not write a picture, {path}: {e.Message} (the next ones are only counted)");
                }
            }
        }

        /// <summary>Writes one file of the recording. An error of the disk is logged with the path and the other files are still written.</summary>
        void TryWrite(string name, Action<string> write)
        {
            var path = Path.Combine(_dir, name);
            try { write(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _failedFiles++;
                Debug.LogError($"[RunRecorder] could not write {path}: {e.Message}");
            }
        }

        /// <summary>The time at which sample 0 of audio.wav was mixed (written to the log once). The recorder's own start stands in for it only when no audio block ever came.</summary>
        void LogStartOnce(bool useStartOfRecorder)
        {
            if (_isStartLogged) return;
            var ticks = _tap.FirstBlockUtcTicks;
            if (ticks < 0 && !useStartOfRecorder) return;
            _startedUtc = ticks >= 0 ? new DateTime(ticks, DateTimeKind.Utc) : _startedUtcAtStart;
            _isStartLogged = true;
            Debug.Log($"[RunRecorder] recording started at {FormatUtc(_startedUtc)} UTC");
        }

        void Finish(bool endedEarly)
        {
            if (_isClosed || !_isStarted) return;
            _isClosed = true;
            _runner.ApplicationQuitting -= FinishEarly;
            _runner.Destroyed -= FinishEarly;
            LogStartOnce(useStartOfRecorder: true);

            var frames = _tap.End();
            _writeQueue.CompleteAdding();
            _writer?.Join();

            var rate = _tap.SampleRate;
            var wav = Pcm16Wav.Encode(_tap.Samples, frames, rate, out var clipped);
            var elapsed = (float)(Time.realtimeSinceStartupAsDouble - _startedAtReal);
            var dto = BuildDto(frames, rate, clipped, elapsed, endedEarly);
            TryWrite("audio.wav", path => File.WriteAllBytes(path, wav));
            if (_withFrames) TryWrite("frames.csv", path => File.WriteAllText(path, "index,visualNow\n" + _frameTimes));
            TryWrite("recording.json", path => File.WriteAllText(path, JsonUtility.ToJson(dto, true)));
            if (_failedPictures > 0) Debug.LogError($"[RunRecorder] {_failedPictures} of {_frameCount} pictures could not be written (the first error is above)");
            IsFinished = true;

            var summary = $"{frames} audio frames at {rate} Hz (peak {_tap.Peak:0.000}, {clipped} clipped), {_frameCount} pictures{(_failedPictures > 0 ? $" ({_failedPictures} of them not written)" : "")} ({dto.averageFps:0.0}/s)";
            var outcome = _failedFiles == 0 ? "saved what was recorded" : $"{_failedFiles} file(s) could not be saved, see the errors above";
            if (endedEarly)
            {
                Debug.Log($"[RunRecorder] closed early at {elapsed.ToString("0.0", CultureInfo.InvariantCulture)} s: {outcome} ({summary})");
                return; // the app is already closing: nothing to quit
            }
            Debug.Log(_failedFiles == 0 ? $"[RunRecorder] done: {summary}" : $"[RunRecorder] done, but {outcome}: {summary}");
            _quit(); // also after a failed file: the app must not stay open past --record-seconds
        }

        RunRecordingDto BuildDto(int frames, int rate, int clipped, float elapsed, bool endedEarly)
        {
            var latency = _audio.Now - _audio.HeardNow; // what ClockMeasurement writes as outputLatency, and what VisualNow (the pictures' stamp) is shifted by
            return new RunRecordingDto
            {
                note = "audio.wav is the final mix (first channel), 16-bit mono; frames.csv gives, for every JPEG, the audio-clock time that was being heard when it was drawn.",
                audioSampleRate = rate,
                audioFirstSampleTime = (double)_tap.FirstFrame / rate,
                audioFrames = frames,
                audioPeak = _tap.Peak,
                audioGaps = _tap.Gaps,
                audioClippedSamples = clipped,
                frames = _frameCount,
                frameWidth = _width,
                frameHeight = _height,
                firstFrameVisualNow = double.IsNaN(_firstVisual) ? 0.0 : _firstVisual,
                lastFrameVisualNow = _lastVisual,
                averageFps = elapsed > 0f ? _frameCount / elapsed : 0f,
                slowestFrameMs = _slowestFrame,
                endedEarly = endedEarly,
                recordedSeconds = elapsed,
                startedUtc = FormatUtc(_startedUtc),
                outputLatencySec = latency,
                videoZeroUtc = FormatUtc(VideoZeroUtc(_startedUtc, latency)),
            };
        }
    }
}
