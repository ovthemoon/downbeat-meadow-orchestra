namespace Downbeat.Core
{
    /// <summary>A point of the picture in viewport units: x to the right, y up, both 0..1 across the picture.</summary>
    public readonly struct ScreenPoint
    {
        public float X { get; }
        public float Y { get; }

        public ScreenPoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Where a hand is drawn on the screen: the filter of <see cref="HandRibbonFollow"/> (a One Euro filter, run every drawn frame, the slower the hand the harder the jitter is filtered) on a point of the
    /// picture instead of a point of the field. The tracker may see the hand only 6 times a second; between two of its pictures the point keeps gliding toward the newest place, so what is drawn at it moves
    /// every frame instead of jumping. The numbers (<c>minHz</c>, <c>beta</c>, <c>speedHz</c>) are those of <see cref="HandRibbonFollow.Step"/> with speeds in picture widths and heights a second.
    /// </summary>
    public sealed class ScreenFollow
    {
        readonly HandRibbonFollow _filter = new HandRibbonFollow();

        /// <summary>Where the hand is drawn; only meaningful after the first <see cref="Step"/>.</summary>
        public ScreenPoint Place => From(_filter.Place);

        /// <summary>The smoothed velocity of the hand on the picture (picture sizes a second).</summary>
        public ScreenPoint Velocity => From(_filter.Velocity);

        /// <summary>The smoothed speed of the hand on the picture (picture sizes a second) that sets the cut-off.</summary>
        public float Speed => _filter.Speed;

        /// <summary>Forgets the hand: the next place is drawn as it is (a hand that comes in, or another hand in this slot).</summary>
        public void Reset() => _filter.Reset();

        /// <summary>
        /// One drawn frame of <paramref name="dtSec"/> seconds. (<paramref name="x"/>, <paramref name="y"/>) is the hand's newest place and <paramref name="captureMs"/> the capture time of the tracker
        /// frame it came from: the same capture time as last call means no new tracker frame. Returns where to draw.
        /// </summary>
        public ScreenPoint Step(float x, float y, double captureMs, float dtSec, float minHz, float beta, float speedHz) =>
            From(_filter.Step(new FieldPoint(x, y), captureMs, dtSec, minHz, beta, speedHz));

        // the filter works on two coordinates and does not care what they are: the second one (FieldPoint.Z) carries y
        static ScreenPoint From(FieldPoint point) => new ScreenPoint(point.X, point.Z);
    }
}
