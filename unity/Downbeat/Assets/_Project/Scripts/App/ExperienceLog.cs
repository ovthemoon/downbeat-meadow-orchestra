using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    [Serializable]
    public sealed class ExperienceSampleDto
    {
        public float t;
        public float energy;
        public int level;
        public string state;
        public float skyT;
        public float fireflyLevel;
        public bool hands;
        public int handNotes;
        public int natureScheduled;
        public int natureDropped;
    }

    [Serializable]
    public sealed class ExperienceEventDto
    {
        public float t;
        public string kind;
        public string from;
        public string to;
        public float energy;
    }

    [Serializable]
    public sealed class ExperienceSummaryDto
    {
        public int maxLevel;
        public float peakEnergy;
        public int sessions;
        public int handNotes;
        public int natureScheduled;
        public int natureDropped;
        /// <summary>Seconds (run time) at which level 1, 2 and 3 were first reached; -1 when never.</summary>
        public float[] firstReachedLevel = { -1f, -1f, -1f, -1f };
        /// <summary>Run time at which the last visit ended (Idle again); -1 when it did not.</summary>
        public float returnedToIdleAt = -1f;
    }

    [Serializable]
    public sealed class ExperienceLogDto
    {
        public string note;
        public float sampleIntervalSec;
        public ExperienceSummaryDto summary = new ExperienceSummaryDto();
        public List<ExperienceEventDto> events = new List<ExperienceEventDto>();
        public List<ExperienceSampleDto> samples = new List<ExperienceSampleDto>();
    }

    /// <summary>
    /// A record of one run of the energy and the flow of a visit (<c>--log-experience PATH</c>): a sample every <c>sampleIntervalSec</c> and an event whenever the state or the
    /// level changes or a visit starts or ends. The time is run time in seconds since the first observation.
    /// </summary>
    public sealed class ExperienceLog
    {
        readonly ExperienceLogDto _dto = new ExperienceLogDto();
        readonly float _interval;
        double _start = double.NaN;
        double _lastSampleAt = double.NegativeInfinity;
        int _lastLevel;
        string _lastState;

        public ExperienceLog(float sampleIntervalSec, string note)
        {
            if (!(sampleIntervalSec > 0f)) throw new ArgumentOutOfRangeException(nameof(sampleIntervalSec), sampleIntervalSec, "must be positive");
            _interval = sampleIntervalSec;
            _dto.sampleIntervalSec = sampleIntervalSec;
            _dto.note = note;
        }

        public ExperienceLogDto Data => _dto;

        public void Observe(double now, float energy, int level, ExperienceState state, float skyT, float fireflyLevel, bool hands, int handNotes, int natureScheduled, int natureDropped)
        {
            if (double.IsNaN(_start)) { _start = now; _lastState = state.ToString(); }
            var t = (float)(now - _start);
            var stateName = state.ToString();

            if (stateName != _lastState) { AddEvent(t, "state", _lastState, stateName, energy); _lastState = stateName; }
            if (level != _lastLevel) { AddEvent(t, "level", _lastLevel.ToString(), level.ToString(), energy); NoteLevel(t, level); _lastLevel = level; }

            var summary = _dto.summary;
            summary.peakEnergy = Math.Max(summary.peakEnergy, energy);
            summary.maxLevel = Math.Max(summary.maxLevel, level);
            summary.handNotes = handNotes;
            summary.natureScheduled = natureScheduled;
            summary.natureDropped = natureDropped;

            if (now - _lastSampleAt < _interval) return;
            _lastSampleAt = now;
            _dto.samples.Add(new ExperienceSampleDto
            {
                t = t, energy = energy, level = level, state = stateName, skyT = skyT, fireflyLevel = fireflyLevel, hands = hands,
                handNotes = handNotes, natureScheduled = natureScheduled, natureDropped = natureDropped,
            });
        }

        public void SessionStarted(double now, float energy)
        {
            _dto.summary.sessions++;
            AddEvent(RunTime(now), "session-started", null, null, energy);
        }

        public void SessionEnded(double now, float energy)
        {
            _dto.summary.returnedToIdleAt = RunTime(now);
            AddEvent(RunTime(now), "session-ended", null, null, energy);
        }

        public string ToJson() => JsonUtility.ToJson(_dto, true);

        float RunTime(double now) => double.IsNaN(_start) ? 0f : (float)(now - _start);

        void NoteLevel(float t, int level)
        {
            if (level >= 1 && level <= 3 && _dto.summary.firstReachedLevel[level] < 0f) _dto.summary.firstReachedLevel[level] = t;
        }

        void AddEvent(float t, string kind, string from, string to, float energy) =>
            _dto.events.Add(new ExperienceEventDto { t = t, kind = kind, from = from, to = to, energy = energy });
    }
}
