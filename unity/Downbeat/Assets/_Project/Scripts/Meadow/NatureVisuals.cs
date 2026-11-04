using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.Meadow
{
    /// <summary>
    /// What the nature layers do to the picture: the sky glides along its arc with the performance energy, the fireflies come with level 1, the far hills' haze flashes
    /// when the drum is heard, and a sweep of wind crosses the field on every beat while somebody is there. The numbers come from <see cref="NatureVisualModel"/> and
    /// <see cref="BeatTicker"/> (tested without a scene); this class only hands them to the meadow.
    /// </summary>
    public sealed class NatureVisuals
    {
        const double FlashLeadSec = 0.007; // a picture event is released up to half a frame early so it is drawn with the sound
        const double MaxLateBeatSec = 0.1;

        readonly MeadowStage _stage;
        readonly NatureDto _settings;
        readonly NatureVisualModel _model;
        readonly BeatTicker _beats;
        readonly List<double> _pendingHillBeats = new List<double>(16);
        readonly List<NatureEvent> _pendingBells = new List<NatureEvent>(16);
        readonly List<BeatTick> _dueBeats = new List<BeatTick>(4);

        /// <param name="controlSky">False when the sky was set by hand (<c>--sky</c>, captures and tests): the energy then leaves it alone.</param>
        /// <param name="controlFireflies">False when the fireflies were set by hand (<c>--fireflies</c>).</param>
        public NatureVisuals(MeadowStage stage, NatureDto settings, BeatGrid grid, int beatsPerBar, bool controlSky, bool controlFireflies)
        {
            _stage = stage;
            _settings = settings;
            ControlsSky = controlSky;
            ControlsFireflies = controlFireflies;
            _model = new NatureVisualModel(settings.visualSmoothSec, settings.hillPulse, settings.hillPulseSec, settings.skyEnergyLow, settings.skyEnergyHigh);
            _beats = new BeatTicker(grid, beatsPerBar, MaxLateBeatSec);
        }

        public NatureVisualModel Model => _model;
        /// <summary>Whether the energy moves the sky. Off when the sky was set by hand (tests and captures put it where they want it).</summary>
        public bool ControlsSky { get; set; }
        /// <summary>Whether the energy sets the number of fireflies. Off when it was set by hand.</summary>
        public bool ControlsFireflies { get; set; }
        /// <summary>Sweeps of wind sent on beats so far.</summary>
        public int BeatSweeps { get; private set; }
        /// <summary>Drum beats that lit the haze so far.</summary>
        public int HillFlashes { get; private set; }
        /// <summary>Bells of the fireflies' instrument that were answered by fireflies so far.</summary>
        public int BellsAnswered { get; private set; }

        /// <summary>Once per frame. <paramref name="heardNow"/> is the audio time of what is being heard; <paramref name="newEvents"/> are the nature notes scheduled this frame.</summary>
        public void Tick(double heardNow, float dt, float energy, int level, bool awake, IReadOnlyList<NatureEvent> newEvents)
        {
            for (var i = 0; i < newEvents.Count; i++)
            {
                if (newEvents[i].Note.Layer == NatureLayer.Hills) _pendingHillBeats.Add(newEvents[i].AudioTime);
                else if (newEvents[i].Note.Layer == NatureLayer.Fireflies) _pendingBells.Add(newEvents[i]);
            }
            ReleaseHillBeats(heardNow);
            ReleaseBells(heardNow);
            if (awake) SendBeatSweeps(heardNow, energy);

            _model.Update(dt, energy, level, awake);
            if (ControlsSky) _stage.Backdrop.SetSunset(_model.SkyT);
            if (ControlsFireflies) _stage.SetFireflyLevel(_model.FireflyLevel);
            _stage.Backdrop.SetHillPulse(_model.HillPulse, _settings.hillPulseRow, _settings.hillPulseHalfHeight, _settings.hillPulseFollow);
        }

        void ReleaseHillBeats(double heardNow)
        {
            var keep = 0;
            for (var i = 0; i < _pendingHillBeats.Count; i++)
            {
                if (_pendingHillBeats[i] <= heardNow + FlashLeadSec)
                {
                    _model.OnHillBeat();
                    HillFlashes++;
                }
                else _pendingHillBeats[keep++] = _pendingHillBeats[i];
            }
            _pendingHillBeats.RemoveRange(keep, _pendingHillBeats.Count - keep);
        }

        /// <summary>A bell is answered by the fireflies when it is heard, not when it was scheduled.</summary>
        void ReleaseBells(double heardNow)
        {
            var keep = 0;
            for (var i = 0; i < _pendingBells.Count; i++)
            {
                var bell = _pendingBells[i];
                if (bell.AudioTime <= heardNow + FlashLeadSec)
                {
                    _stage.AnswerBell(bell.Note.PitchStep, bell.Note.Slot);
                    BellsAnswered++;
                }
                else _pendingBells[keep++] = bell;
            }
            _pendingBells.RemoveRange(keep, _pendingBells.Count - keep);
        }

        void SendBeatSweeps(double heardNow, float energy)
        {
            _dueBeats.Clear();
            _beats.Collect(heardNow, FlashLeadSec, _dueBeats);
            foreach (var beat in _dueBeats)
            {
                _stage.AddBeatSweep(energy, beat.IsDownbeat);
                BeatSweeps++;
            }
        }
    }
}
