using System;
using System.Collections.Generic;
using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>
    /// Checks whether PlayScheduled(dspTime + ahead) really waits. For each ahead value one click is scheduled;
    /// the page's audio tap later measures when it actually sounded relative to ctx0.
    /// </summary>
    public sealed class AheadTest
    {
        readonly AheadArgs _args;
        readonly AudioSource _source;
        readonly List<AheadRecord> _records = new List<AheadRecord>();
        int _index;
        double _nextAt;

        public AheadTest(GameObject host, AudioClip clip, AheadArgs args)
        {
            _args = args ?? throw new ArgumentNullException(nameof(args));
            if (args.aheadSec == null || args.aheadSec.Length == 0) throw new ArgumentException("aheadSec is empty");
            var child = new GameObject("ahead-click");
            child.transform.SetParent(host.transform, false);
            _source = child.AddComponent<AudioSource>();
            _source.clip = clip;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _nextAt = Time.realtimeSinceStartupAsDouble + 0.5;
            Day0Bridge.Mark("ahead-start");
        }

        public bool IsFinished => _index >= _args.aheadSec.Length && Time.realtimeSinceStartupAsDouble > _nextAt;

        public void Tick()
        {
            if (_index >= _args.aheadSec.Length || Time.realtimeSinceStartupAsDouble < _nextAt) return;
            var ahead = _args.aheadSec[_index++];
            var dsp0 = AudioSettings.dspTime;
            var record = new AheadRecord
            {
                aheadSec = ahead,
                dsp0 = dsp0,
                perf0 = Day0Bridge.NowMs(),
                ctx0 = Day0Bridge.CtxTime(),
                targetDsp = dsp0 + ahead,
            };
            _source.PlayScheduled(record.targetDsp);
            _records.Add(record);
            _nextAt = Time.realtimeSinceStartupAsDouble + _args.spacingSec;
        }

        public AheadSummary Finish()
        {
            _source.Stop();
            Day0Bridge.Mark("ahead-end");
            return new AheadSummary { records = _records.ToArray() };
        }
    }
}
