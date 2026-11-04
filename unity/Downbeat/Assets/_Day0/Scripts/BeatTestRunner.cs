using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>
    /// S-1 beat clock test, Unity side. Two modes:
    ///  - "scheduled": frame loop schedules clicks lookahead seconds ahead with AudioSource.PlayScheduled (dspTime clock)
    ///  - "play": baseline, calls AudioSource.Play() in the first frame at or after each slot time (realtime clock)
    /// Both follow the same integer-slot grid; only the clock and the call differ.
    /// </summary>
    public sealed class BeatTestRunner
    {
        const double DspLogIntervalSec = 1.0;

        readonly BeatArgs _args;
        readonly AudioSource[] _pool;
        readonly BeatGrid _grid;
        readonly bool _isScheduled;
        readonly long _totalSlots;
        readonly List<ScheduleRecord> _records = new List<ScheduleRecord>(512);

        long _nextSlot;
        long _issued;
        long _skippedLate;
        int _poolIndex;
        int _startFrame;
        double _originPerfMs;
        double _originCtx;
        double _nextDspLog;
        double _lastDsp;
        double _maxDspStall;

        public BeatTestRunner(GameObject host, AudioClip clip, BeatArgs args)
        {
            _args = args ?? throw new ArgumentNullException(nameof(args));
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            _isScheduled = args.path == "scheduled";
            if (!_isScheduled && args.path != "play") throw new ArgumentException($"unknown path '{args.path}'");

            _pool = new AudioSource[_isScheduled ? Math.Max(2, args.poolSize) : 1];
            for (var i = 0; i < _pool.Length; i++)
            {
                var child = new GameObject($"click-{i}");
                child.transform.SetParent(host.transform, false);
                var src = child.AddComponent<AudioSource>();
                src.clip = clip;
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = 1f;
                _pool[i] = src;
            }

            var clock = CurrentClock();
            _grid = new BeatGrid(clock + args.startDelaySec, args.bpm, args.slotsPerBeat);
            _totalSlots = (long)Math.Floor(args.durationSec / _grid.SlotDuration);
            _startFrame = Time.frameCount;
            _originPerfMs = Day0Bridge.NowMs();
            _originCtx = Day0Bridge.CtxTime();
            _nextDspLog = Time.realtimeSinceStartupAsDouble;
            _lastDsp = AudioSettings.dspTime;
            Day0Bridge.Mark($"beat-start:{args.label}:{args.path}");
        }

        public bool IsFinished => _nextSlot >= _totalSlots;

        double CurrentClock() => _isScheduled ? AudioSettings.dspTime : Time.realtimeSinceStartupAsDouble;

        public void Tick()
        {
            LogDsp();
            if (IsFinished) return;
            if (_isScheduled) TickScheduled();
            else TickPlay();
        }

        void TickScheduled()
        {
            var now = AudioSettings.dspTime;
            var lastSlot = Math.Min(_totalSlots - 1, _grid.LastSlotAtOrBefore(now + _args.lookaheadSec));
            while (_nextSlot <= lastSlot)
            {
                var target = _grid.SlotTime(_nextSlot);
                if (target < now)
                {
                    _skippedLate++; // the frame loop was too slow (stall or hidden tab): this slot can no longer be scheduled
                }
                else
                {
                    var src = _pool[_poolIndex];
                    _poolIndex = (_poolIndex + 1) % _pool.Length;
                    src.PlayScheduled(target);
                    _issued++;
                    _records.Add(new ScheduleRecord
                    {
                        slot = _nextSlot,
                        targetDsp = target,
                        dspAtCall = now,
                        perfMsAtCall = Day0Bridge.NowMs(),
                        ctxAtCall = Day0Bridge.CtxTime(),
                    });
                }
                _nextSlot++;
            }
        }

        void TickPlay()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (now < _grid.SlotTime(_nextSlot)) return;
            // Naive per-frame trigger: at most one Play() per frame, so a late frame causes a catch-up burst on later frames.
            _pool[0].Play();
            _issued++;
            _records.Add(new ScheduleRecord
            {
                slot = _nextSlot,
                targetDsp = _grid.SlotTime(_nextSlot),
                dspAtCall = now,
                perfMsAtCall = Day0Bridge.NowMs(),
                ctxAtCall = Day0Bridge.CtxTime(),
            });
            _nextSlot++;
        }

        void LogDsp()
        {
            var rt = Time.realtimeSinceStartupAsDouble;
            var dsp = AudioSettings.dspTime;
            _maxDspStall = Math.Max(_maxDspStall, dsp - _lastDsp);
            _lastDsp = dsp;
            if (rt < _nextDspLog) return;
            _nextDspLog += DspLogIntervalSec;
            var sample = new DspSample
            {
                label = _args.label,
                realtimeSec = rt,
                dspSec = dsp,
                ctxSec = Day0Bridge.CtxTime(),
                perfMs = Day0Bridge.NowMs(),
                frame = Time.frameCount,
            };
            Day0Bridge.Emit("dsp", JsonUtility.ToJson(sample));
        }

        public BeatSummary Finish()
        {
            foreach (var src in _pool) src.Stop();
            Day0Bridge.Mark($"beat-end:{_args.label}:{_args.path}");
            return new BeatSummary
            {
                label = _args.label,
                path = _args.path,
                bpm = _args.bpm,
                slotsPerBeat = _args.slotsPerBeat,
                totalSlots = _totalSlots,
                issued = _issued,
                skippedLate = _skippedLate,
                originClock = _grid.Origin,
                originPerfMs = _originPerfMs,
                originCtx = _originCtx,
                endPerfMs = Day0Bridge.NowMs(),
                frames = Time.frameCount - _startFrame,
                maxDspStallSec = _maxDspStall,
                records = _records.ToArray(),
            };
        }
    }
}
