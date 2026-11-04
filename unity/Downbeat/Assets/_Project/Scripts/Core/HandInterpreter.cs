using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>A strike that has been decided: which part, which step, how hard, and the timing facts needed to measure it.</summary>
    public readonly struct HandStrike
    {
        public int HandId { get; }
        public Part Part { get; }
        public int PitchStep { get; }
        public float Velocity { get; }
        /// <summary>Highest downward speed seen between the trigger and the decision, palm lengths per second.</summary>
        public float PeakSpeed { get; }
        /// <summary>Capture time of the frame where the speed first crossed the trigger threshold.</summary>
        public double TriggerCaptureMs { get; }
        /// <summary>Timing of the frame that completed the decision (the trigger frame plus the wait for the peak).</summary>
        public HandTiming Timing { get; }
        /// <summary>Where across the picture the hand was when it struck, 0 left .. 1 right (as the player sees it).</summary>
        public float X01 { get; }
        /// <summary>Where up and down the picture the hand was when it struck, 0 top .. 1 bottom.</summary>
        public float Y01 { get; }

        public HandStrike(int handId, Part part, int pitchStep, float velocity, float peakSpeed, double triggerCaptureMs, HandTiming timing, float x01 = float.NaN, float y01 = float.NaN)
        {
            X01 = x01;
            Y01 = y01;
            HandId = handId; Part = part; PitchStep = pitchStep; Velocity = velocity; PeakSpeed = peakSpeed; TriggerCaptureMs = triggerCaptureMs; Timing = timing;
        }

        /// <summary>Extra delay the wait for the speed peak added, ms.</summary>
        public double PeakWaitMs => Timing.CaptureMs - TriggerCaptureMs;
    }

    /// <summary>
    /// PLACEHOLDER strike rule (the interaction design is being reworked; this is a thin layer on top of <see cref="IHandMotionView"/>).
    /// A downward palm speed above the trigger threshold starts a strike; the note is decided when the speed peaks (or after a short wait),
    /// taking the part from the cell of the picture the palm is in (three columns, two rows), the pitch from its height and the strength from the peak speed, all measured
    /// where the strike started. The hand must slow down before it can strike again. Two hands are independent by id. Pure, driven only by
    /// frame timestamps: the same frames always give the same strikes.
    /// </summary>
    public sealed class HandInterpreter : IHandInterpreter
    {
        enum Phase
        {
            Armed,
            Peaking,
            Disarmed,
        }

        sealed class Strike
        {
            public Phase Phase;
            public int Epoch = -1;
            public double TriggerMs;
            public double LastTriggerMs = double.NegativeInfinity;
            public float Peak;
            public float TriggerX;
            public float TriggerY;
            public int LastStep = -1;
            public int LastPart = -1;
        }

        HandInterpreterSettings _settings = HandInterpreterSettings.Default;
        int _pitchSteps = 1;
        readonly Strike[] _strikes = new Strike[HandBufferLayout.MaxHands];

        public HandInterpreter()
        {
            Reset();
        }

        public HandInterpreter(HandInterpreterSettings settings, int pitchSteps) : this()
        {
            Configure(settings, pitchSteps);
        }

        public void Configure(in HandInterpreterSettings settings, int pitchSteps)
        {
            if (pitchSteps < 1) throw new ArgumentOutOfRangeException(nameof(pitchSteps));
            _settings = settings;
            _pitchSteps = pitchSteps;
        }

        public void Reset()
        {
            for (var i = 0; i < _strikes.Length; i++) _strikes[i] = new Strike();
        }

        public void Process(in HandFrame frame, IHandMotionView motion, List<HandStrike> output)
        {
            var now = frame.CaptureMs;
            var timing = new HandTiming(frame.Seq, frame.CaptureMs, frame.InferEndMs, frame.ReceivedMs);
            for (var id = 0; id < _strikes.Length; id++)
            {
                var strike = _strikes[id];
                if (!motion.TryGetById(id, out var hand))
                {
                    // the hand is gone for good: a strike that was waiting for its peak is still a strike
                    if (strike.Phase == Phase.Peaking) Decide(id, strike, timing, output);
                    _strikes[id] = new Strike { LastTriggerMs = strike.LastTriggerMs };
                    continue;
                }
                if (hand.Epoch != strike.Epoch)
                {
                    // a new hand (or a hand that came back after a long gap): start over, keep only the retrigger clock
                    strike = new Strike { Epoch = hand.Epoch, LastTriggerMs = strike.LastTriggerMs };
                    _strikes[id] = strike;
                }

                if (!hand.IsTracked)
                {
                    if (strike.Phase == Phase.Peaking && now - strike.TriggerMs >= _settings.PeakWaitMs) Decide(id, strike, timing, output);
                    continue;
                }
                Step(id, strike, hand, now, timing, output);
            }
        }

        void Step(int id, Strike strike, in HandMotion hand, double now, in HandTiming timing, List<HandStrike> output)
        {
            var speed = hand.HasVelocity ? hand.DownSpeedPalms : float.NaN;
            switch (strike.Phase)
            {
                case Phase.Armed:
                    if (!float.IsNaN(speed) && speed >= _settings.TriggerSpeed && hand.Pose != HandPose.Fist && now - strike.LastTriggerMs >= _settings.MinRetriggerMs)
                    {
                        strike.Phase = Phase.Peaking;
                        strike.TriggerMs = now;
                        strike.LastTriggerMs = now;
                        strike.Peak = speed;
                        strike.TriggerX = hand.X;
                        strike.TriggerY = hand.Y;
                        if (_settings.PeakWaitMs <= 0) Decide(id, strike, timing, output);
                    }
                    break;
                case Phase.Peaking:
                    if (speed > strike.Peak) strike.Peak = speed;
                    if (now - strike.TriggerMs >= _settings.PeakWaitMs || speed < strike.Peak * _settings.PeakDropRatio) Decide(id, strike, timing, output);
                    break;
                default:
                    if (float.IsNaN(speed) || speed < _settings.RearmSpeed) strike.Phase = Phase.Armed;
                    break;
            }
        }

        void Decide(int handId, Strike strike, in HandTiming timing, List<HandStrike> output)
        {
            var part = (int)CellPicker.Pick(strike.TriggerX, strike.TriggerY, (Part)Math.Max(strike.LastPart, 0), strike.LastPart >= 0, CellPicker.ColumnMarginFor(_settings.PartHysteresis), _settings.Cells);
            var heightPosition = PitchHeightMap.Position(1f - strike.TriggerY, _pitchSteps);
            var marginSteps = _settings.HeightHysteresis / PitchHeightMap.UsableHeight * _pitchSteps;
            var step = PickBand(heightPosition, _pitchSteps, strike.LastStep, marginSteps);
            strike.LastPart = part;
            strike.LastStep = step;
            var velocity = VelocityMapper.FromStrikeSpeed(strike.Peak, _settings.TriggerSpeed, _settings.FullSpeed, _settings.MinVelocity);
            output.Add(new HandStrike(handId, (Part)part, step, velocity, strike.Peak, strike.TriggerMs, timing.WithTrigger(strike.TriggerMs), strike.TriggerX, strike.TriggerY));
            strike.Phase = Phase.Disarmed;
        }

        /// <summary>Which band a position falls in; stays in the previous band while the position is within the dead band around its edges.</summary>
        public static int PickBand(float position, int count, int previous, float marginBands)
        {
            var raw = (int)position;
            if (raw >= count) raw = count - 1;
            if (raw < 0) raw = 0;
            if (previous >= 0 && previous != raw && position >= previous - marginBands && position < previous + 1 + marginBands) return previous;
            return raw;
        }
    }
}
