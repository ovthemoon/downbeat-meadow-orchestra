using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Collections;
using System.Threading;
using Downbeat.Audio;
using Downbeat.Content;
using Downbeat.Core;
using UnityEngine;
using VContainer.Unity;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--measure-clock --out DIR [--scenario calm|stress] [--duration SEC]</c>: the native version of the Day 0 S-1 beat-clock test.
    /// Instead of the game it schedules one click per 1/16 slot through the same <see cref="IAudioEngine"/> the game uses (0.1 s ahead, the BPM of content.json),
    /// records Unity's mixer output with <see cref="AudioOutputTap"/> (silenced) and writes a raw float32 capture plus a JSON in the format
    /// tools/analyze_s1.mjs reads. The "stress" scenario blocks the main thread for 50 ms every 2 s. The app quits when the run is written.
    /// </summary>
    public sealed class ClockMeasurement : IStartable, ITickable
    {
        const string Path = "native-scheduled";
        const int ClickSampleIndex = 0;
        const double LookaheadSec = 0.1;
        const double StartDelaySec = 0.6;
        const double DefaultDurationSec = 60.0;
        const double BootTimeoutSec = 30.0;
        const double TailSec = 0.5;
        const double CaptureMarginSec = 5.0;
        const double StressPeriodSec = 2.0;
        const int StressBlockMs = 50;
        const double MsPerSecond = 1000.0;
        const int MeasuredTargetFps = 60;
        const string RawSuffix = ".ctx0-native.f32";
        const string MeasurementsFolder = "measurements";
        const int Failure = 3;
        const double LongFrameMs = 100.0; // a frame this long is listed in the run's metadata: it is a stall of the main thread

        enum Phase
        {
            Loading,
            Running,
            Done,
        }

        readonly IAudioEngine _audio;
        readonly LaunchArgs _args;
        readonly IContentRepository _content;
        readonly CoroutineRunner _runner;
        readonly FrameStats _frames = new FrameStats(8192);
        readonly List<ClockScheduleRecord> _records = new List<ClockScheduleRecord>(512);
        readonly List<ClockBlockRecord> _blocks = new List<ClockBlockRecord>();
        readonly List<string> _longFrames = new List<string>();

        Phase _phase = Phase.Loading;
        bool _booted;
        double _bpm;
        int _slotsPerBeat;
        string _outDir;
        string _scenario;
        double _durationSec;
        AudioOutputTap _tap;
        BeatGrid _grid;
        long _totalSlots;
        long _nextSlot;
        long _issued;
        long _skippedLate;
        double _bootStarted;
        double _startedPerfMs;
        double _originPerfMs;
        double _startMarkPerfMs;
        double _startMarkDsp;
        double _lastDsp;
        double _maxDspStall;
        double _minFrameMs = double.MaxValue;
        double _nextBlockAt;
        long _frameCount;

        public ClockMeasurement(IAudioEngine audio, LaunchArgs args, IContentRepository content, CoroutineRunner runner)
        {
            _audio = audio;
            _args = args;
            _content = content;
            _runner = runner;
        }

        public void Start()
        {
            _outDir = System.IO.Path.GetFullPath(_args.Get("out") ?? throw new ArgumentException("--measure-clock needs --out DIR"));
            _scenario = _args.Get("scenario") ?? "calm";
            _durationSec = _args.TryGetDouble("duration", out var duration) ? duration : DefaultDurationSec;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = MeasuredTargetFps; // the game runs at about this rate; the clock must not depend on it
            _bootStarted = Time.realtimeSinceStartupAsDouble;
            _runner.StartCoroutine(Boot());
        }

        /// <summary>Song and voice count come from content.json, so the run measures the configuration the game plays.</summary>
        IEnumerator Boot()
        {
            ContentBundle bundle = null;
            string failure = null;
            yield return _content.Load(b => bundle = b, e => failure = e);
            if (bundle == null) { Fail(failure); yield break; }

            _bpm = bundle.Dto.song.bpm;
            _slotsPerBeat = bundle.Dto.song.slotsPerBeat;
            _audio.Configure(1f, bundle.Dto.audio.maxVoices); // full scale: the click is what is measured
            var clickPath = WriteClickFile();
            _audio.LoadSamples(new[] { new SampleLoadRequest(ClickSampleIndex, new Uri(clickPath).AbsoluteUri) });

            var listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            if (listener == null) { Fail("no AudioListener to tap"); yield break; }
            _tap = listener.gameObject.AddComponent<AudioOutputTap>();
            _tap.MuteOutput = true;
            _booted = true;
            Debug.Log($"[ClockMeasurement] scenario={_scenario} duration={_durationSec}s bpm={_bpm} slotsPerBeat={_slotsPerBeat} out={_outDir}");
        }

        public void Tick()
        {
            if (_phase == Phase.Done) return;
            _audio.Tick();
            if (_phase == Phase.Loading) TickLoading();
            else TickRunning();
        }

        void TickLoading()
        {
            if (!_booted)
            {
                if (Time.realtimeSinceStartupAsDouble - _bootStarted > BootTimeoutSec) Fail("content did not load");
                return;
            }
            if (_audio.SamplesFailed > 0) { Fail("the click sample failed to load"); return; }
            if (Time.realtimeSinceStartupAsDouble - _bootStarted > BootTimeoutSec) { Fail("the audio clock did not start"); return; }
            if (_audio.IsRunning) BeginRun();
        }

        void BeginRun()
        {
            var now = _audio.Now;
            _grid = new BeatGrid(now + StartDelaySec, _bpm, _slotsPerBeat);
            _totalSlots = (long)Math.Floor(_durationSec / _grid.SlotDuration);
            _tap.Begin((int)((_durationSec + StartDelaySec + CaptureMarginSec) * AudioSettings.outputSampleRate));

            _startedPerfMs = PerfMs();
            _originPerfMs = _startedPerfMs;
            _startMarkPerfMs = PerfMs();
            _startMarkDsp = _audio.Now;
            _lastDsp = _startMarkDsp;
            _nextBlockAt = Time.realtimeSinceStartupAsDouble + StressPeriodSec;
            _phase = Phase.Running;
        }

        void TickRunning()
        {
            RecordFrame();
            if (_scenario == "stress") MaybeBlock();
            ScheduleDueSlots();

            var lastTarget = _grid.SlotTime(_totalSlots - 1);
            if (_nextSlot >= _totalSlots && _audio.Now > lastTarget + TailSec) Finish();
        }

        void RecordFrame()
        {
            var deltaMs = Time.unscaledDeltaTime * 1000.0;
            if (_frameCount > 0)
            {
                _frames.Add(deltaMs);
                _minFrameMs = Math.Min(_minFrameMs, deltaMs);
            }
            _frameCount++;
            if (deltaMs > LongFrameMs) _longFrames.Add($"t={PerfMs() / MsPerSecond - _startedPerfMs / MsPerSecond:0.0}s:{deltaMs:0}ms");
            var dsp = _audio.Now;
            _maxDspStall = Math.Max(_maxDspStall, dsp - _lastDsp);
            _lastDsp = dsp;
        }

        void MaybeBlock()
        {
            if (Time.realtimeSinceStartupAsDouble < _nextBlockAt) return;
            _nextBlockAt += StressPeriodSec;
            var start = PerfMs();
            Thread.Sleep(StressBlockMs);
            _blocks.Add(new ClockBlockRecord(start, PerfMs() - start));
        }

        void ScheduleDueSlots()
        {
            var now = _audio.Now;
            var lastSlot = Math.Min(_totalSlots - 1, _grid.LastSlotAtOrBefore(now + LookaheadSec));
            while (_nextSlot <= lastSlot)
            {
                var target = _grid.SlotTime(_nextSlot);
                if (target < now)
                {
                    _skippedLate++; // the frame loop was too slow: this click can no longer be scheduled
                }
                else
                {
                    _audio.Schedule(ClickSampleIndex, target, 1.0, 1.0);
                    _issued++;
                    _records.Add(new ClockScheduleRecord(_nextSlot, target, now, PerfMs()));
                }
                _nextSlot++;
            }
        }

        void Finish()
        {
            _phase = Phase.Done;
            var endPerfMs = PerfMs();
            var endDsp = _audio.Now;
            var recorded = _tap.End();
            try
            {
                WriteRun(recorded, endPerfMs, endDsp);
                Debug.Log($"[ClockMeasurement] done: {recorded} frames, {_issued}/{_totalSlots} clicks scheduled, {_skippedLate} skipped late, stolen {_audio.Stats.Stolen}, late {_audio.Stats.Late}");
                Application.Quit();
            }
            catch (Exception e)
            {
                Fail($"could not write the run: {e}");
            }
        }

        void WriteRun(int recorded, double endPerfMs, double endDsp)
        {
            var label = $"{Path}__{_scenario}";
            var rawDir = System.IO.Path.Combine(_outDir, "raw-audio");
            Directory.CreateDirectory(rawDir);
            var rawPath = System.IO.Path.Combine(rawDir, label + RawSuffix);
            WriteRaw(rawPath, _tap.Samples, recorded);

            var root = FindMeasurementsRoot(_outDir);
            var data = BuildData(label, recorded, endPerfMs, endDsp, ToRelative(root, rawPath), ToRelative(root, _outDir));
            File.WriteAllText(System.IO.Path.Combine(_outDir, label + ".meta.json"), ClockRunMeta.ToJson(data));
        }

        ClockRunData BuildData(string label, int recorded, double endPerfMs, double endDsp, string rawFile, string runDir)
        {
            AudioSettings.GetDSPBufferSize(out var bufferLength, out var bufferCount);
            var sampleRate = AudioSettings.outputSampleRate;
            return new ClockRunData
            {
                Label = label, Path = Path, RunDir = runDir, RawFile = rawFile,
                Bpm = (int)Math.Round(_grid.Bpm), SlotsPerBeat = _grid.SlotsPerBeat, DurationSec = _durationSec, LookaheadSec = LookaheadSec, StartDelaySec = StartDelaySec,
                StartedPerfMs = _startedPerfMs, EndedPerfMs = endPerfMs,
                SampleRate = sampleRate, FirstFrame = _tap.FirstFrame, Frames = recorded, Peak = _tap.Peak, Gaps = _tap.Gaps,
                OutputLatencySec = _audio.Now - _audio.HeardNow, BaseLatencySec = (double)bufferLength / sampleRate,
                StartMarkPerfMs = _startMarkPerfMs, StartMarkDsp = _startMarkDsp, EndMarkPerfMs = endPerfMs, EndMarkDsp = endDsp,
                TotalSlots = _totalSlots, Issued = _issued, SkippedLate = _skippedLate, OriginDsp = _grid.Origin, OriginPerfMs = _originPerfMs,
                FrameCount = _frameCount, MaxDspStallSec = _maxDspStall, FrameTimes = _frames.Summarize(), MinFrameMs = _frames.Count > 0 ? _minFrameMs : 0.0,
                Records = _records, Blocks = _blocks, Runtime = BuildRuntime(bufferLength, bufferCount, sampleRate),
            };
        }

        Dictionary<string, string> BuildRuntime(int bufferLength, int bufferCount, int sampleRate)
        {
            var stats = _audio.Stats;
            return new Dictionary<string, string>
            {
                { "unity", Application.unityVersion },
                { "platform", Application.platform.ToString() },
                { "os", SystemInfo.operatingSystem },
                { "cpu", SystemInfo.processorType },
                { "processorCount", SystemInfo.processorCount.ToString(CultureInfo.InvariantCulture) },
                { "batchMode", Application.isBatchMode.ToString() },
                { "targetFrameRate", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture) },
                { "vSyncCount", QualitySettings.vSyncCount.ToString(CultureInfo.InvariantCulture) },
                { "outputSampleRate", sampleRate.ToString(CultureInfo.InvariantCulture) },
                { "dspBufferLength", bufferLength.ToString(CultureInfo.InvariantCulture) },
                { "dspBufferCount", bufferCount.ToString(CultureInfo.InvariantCulture) },
                { "scenario", _scenario },
                { "engineLate", stats.Late.ToString(CultureInfo.InvariantCulture) },
                { "engineStolen", stats.Stolen.ToString(CultureInfo.InvariantCulture) },
                { "framesOver100ms", _longFrames.Count == 0 ? "none" : string.Join(" ", _longFrames) },
            };
        }

        static string WriteClickFile()
        {
            var path = System.IO.Path.Combine(Application.temporaryCachePath, "clock-click.wav");
            File.WriteAllBytes(path, WavEncoder.EncodePcm16Mono(ClickSignal.Render(AudioSettings.outputSampleRate), AudioSettings.outputSampleRate));
            return path;
        }

        static void WriteRaw(string path, float[] samples, int frames)
        {
            var bytes = new byte[frames * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(path, bytes);
        }

        static string FindMeasurementsRoot(string dir)
        {
            for (var current = new DirectoryInfo(dir); current != null; current = current.Parent)
            {
                if (current.Name == MeasurementsFolder) return current.FullName;
            }
            throw new ArgumentException($"--out must be inside a '{MeasurementsFolder}' folder (analyze_s1.mjs reads the capture from there): {dir}");
        }

        static string ToRelative(string root, string path) => System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

        static double PerfMs() => Time.realtimeSinceStartupAsDouble * 1000.0;

        void Fail(string message)
        {
            _phase = Phase.Done;
            Debug.LogError("[ClockMeasurement] " + message);
            Application.Quit(Failure);
        }
    }
}
