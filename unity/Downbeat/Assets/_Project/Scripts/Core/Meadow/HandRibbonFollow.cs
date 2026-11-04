using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Where the ribbon of one hand is drawn: a One Euro filter (Casiez, Roussel and Vogel, CHI 2012) on the hand's place on the field, run every
    /// rendered frame. Hand places come with the tracker frames (15 to 30 a second, unevenly, with MediaPipe's jitter) while the app draws 60 frames
    /// a second. Between two tracker frames the ribbon keeps gliding toward the newest place instead of jumping. The slower the hand, the harder the
    /// jitter is filtered (cut-off <c>ribbon.followMinHz</c>); speed raises the cut-off by <c>ribbon.followBeta</c> Hz per m/s, so a strike is
    /// followed closely. The speed is that of the place-to-place velocity of the tracker frames, smoothed as a vector with cut-off
    /// <c>ribbon.followSpeedHz</c>: jitter back and forth cancels out there, so a still hand reads as slow and keeps the strong filtering
    /// (smoothing the speed alone would count every jitter step as movement).
    /// The first place of a hand is drawn as it is, and a <c>followMinHz</c> of 0 turns the filter off. Only the picture is smoothed: strikes
    /// are read from the raw places (<see cref="HandMotionTracker"/>, <see cref="HandInterpreter"/>).
    /// </summary>
    public sealed class HandRibbonFollow
    {
        const double MsPerSecond = 1000.0;
        const double TwoPi = 2.0 * Math.PI;

        bool _hasPlace;
        float _x;
        float _z;
        float _targetX;
        float _targetZ;
        double _targetCaptureMs;
        float _velocityX;
        float _velocityZ;

        /// <summary>Where the ribbon is drawn (metres on the field); only meaningful after the first <see cref="Step"/>.</summary>
        public FieldPoint Place => new FieldPoint(_x, _z);

        /// <summary>The smoothed velocity of the hand on the field (m/s, x and z): the wind of the hand leans the grass with it.</summary>
        public FieldPoint Velocity => new FieldPoint(_velocityX, _velocityZ);

        /// <summary>The smoothed speed of the hand on the field (m/s) that sets the cut-off; for tests and the debug overlay.</summary>
        public float Speed => (float)Math.Sqrt(_velocityX * _velocityX + _velocityZ * _velocityZ);

        /// <summary>Forgets the hand: the next place is drawn as it is (a hand that comes in, or another hand in this ribbon).</summary>
        public void Reset() => _hasPlace = false;

        /// <summary>
        /// One rendered frame of <paramref name="dtSec"/> seconds. <paramref name="place"/> is the hand's newest place and <paramref name="captureMs"/>
        /// the capture time of the tracker frame it came from: the same capture time as last call means no new tracker frame. Returns where to draw.
        /// </summary>
        public FieldPoint Step(FieldPoint place, double captureMs, float dtSec, float minHz, float beta, float speedHz)
        {
            if (!_hasPlace || minHz <= 0f)
            {
                _hasPlace = true;
                _x = _targetX = place.X;
                _z = _targetZ = place.Z;
                _targetCaptureMs = captureMs;
                _velocityX = _velocityZ = 0f;
                return Place;
            }
            if (captureMs != _targetCaptureMs)
            {
                var gapSec = (captureMs - _targetCaptureMs) / MsPerSecond;
                if (gapSec > 0.0) // a tracker that started again counts from 0: keep the velocity, take the place
                {
                    var b = Alpha(speedHz, gapSec);
                    _velocityX += b * ((float)((place.X - _targetX) / gapSec) - _velocityX);
                    _velocityZ += b * ((float)((place.Z - _targetZ) / gapSec) - _velocityZ);
                }
                _targetX = place.X;
                _targetZ = place.Z;
                _targetCaptureMs = captureMs;
            }
            var a = Alpha(minHz + beta * Speed, dtSec);
            _x += a * (_targetX - _x);
            _z += a * (_targetZ - _z);
            return Place;
        }

        /// <summary>How far a first-order low-pass with cut-off <paramref name="hz"/> moves toward its input in <paramref name="dtSec"/> seconds (exact for any step length).</summary>
        static float Alpha(double hz, double dtSec) => dtSec <= 0.0 || hz <= 0.0 ? 0f : (float)(1.0 - Math.Exp(-TwoPi * hz * dtSec));
    }
}
