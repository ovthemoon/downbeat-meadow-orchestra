using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Where the steady wind is at one moment, as the shaders get it: a handful of small numbers taken from the clock in double precision and wrapped, so that days of uptime never
    /// coarsen the motion (a float clock would be a whole frame off after about three days). All of them are small floats.
    /// </summary>
    public readonly struct BreezePhase
    {
        /// <summary>How far the gust pattern has moved within its current cell (metres, 0 .. spacing).</summary>
        public float GustShift { get; }
        /// <summary>Which cell of the pattern it is, counted from the start and wrapped at <see cref="BreezeModel.GustCellWrap"/> (a whole number).</summary>
        public float GustCell { get; }
        public float SwayPhase { get; }
        public float FlutterPhase { get; }
        public float MeanPhase { get; }
        /// <summary>How far the layers and the shadows have drifted, wrapped to the repeat of the cloud noise (metres).</summary>
        public float NearShiftX { get; }
        public float NearShiftZ { get; }
        public float FarShiftX { get; }
        public float FarShiftZ { get; }
        public float ShadowShiftX { get; }
        public float ShadowShiftZ { get; }

        public BreezePhase(float gustShift, float gustCell, float swayPhase, float flutterPhase, float meanPhase,
            float nearShiftX, float nearShiftZ, float farShiftX, float farShiftZ, float shadowShiftX, float shadowShiftZ)
        {
            GustShift = gustShift; GustCell = gustCell; SwayPhase = swayPhase; FlutterPhase = flutterPhase; MeanPhase = meanPhase;
            NearShiftX = nearShiftX; NearShiftZ = nearShiftZ; FarShiftX = farShiftX; FarShiftZ = farShiftZ; ShadowShiftX = shadowShiftX; ShadowShiftZ = shadowShiftZ;
        }
    }

    /// <summary>
    /// The steady wind of the meadow, as a pure function of place and time (no state, no random numbers at run time): one prevailing direction the grass leans in, broad gusts that roll
    /// along it, a slow sway around the lean, a small slow motion of the tips, and the drift of clouds and their shadows. The grass shader (MeadowBreeze.hlsl) evaluates exactly these
    /// formulas; this class is how tests and tools ask what the grass is doing. Bend values are fractions of a blade's height, before the wind's bendScale.
    /// <para>
    /// Gusts: the pattern is a row of gusts one per cell of <c>spacing</c> metres along the wind, each moved off the middle of its cell by a hashed amount, with a hashed strength. The whole row
    /// moves along the wind at <c>speed</c>. A point of the field sees the gusts of its own cell and the ones next to it. The cell number is wrapped (and the shift within the cell is
    /// kept small by the CPU), so the pattern repeats only after <see cref="GustCellWrap"/> cells (hours).
    /// </para>
    /// </summary>
    public sealed class BreezeModel
    {
        public const int GustCellWrap = 4096;
        const float TwoPi = 6.2831853f;
        const float SpringLobeDistance = 1.9f;
        const float SpringLobeWidth = 0.9f;
        const float FrontSharpness = 0.7f;
        const float BackSoftness = 1.15f;
        const float CrossWaveShare = 0.6f;
        const float CrossModulation = 0.2f;
        const float SwayAcrossShare = 0.5f;
        const float FlutterAcrossShare = 0.6f;
        const float FlutterBladeShare = 0.3f;
        const float FlutterAlongWind = 0.7f;
        const float StiffnessSpread = 7.31f;
        const int FirstNeighbour = -1;
        const int LastNeighbour = 2;

        readonly MeadowBreezeDto _breeze;
        readonly MeadowCloudsDto _clouds;
        readonly float _gustSpeed;
        readonly float _crossFrequency;

        public BreezeModel(MeadowBreezeDto breeze, MeadowCloudsDto clouds)
        {
            _breeze = breeze ?? throw new ArgumentNullException(nameof(breeze));
            _clouds = clouds ?? throw new ArgumentNullException(nameof(clouds));
            var radians = breeze.directionDeg * (float)(Math.PI / 180.0);
            Direction = new FieldPoint((float)Math.Cos(radians), (float)Math.Sin(radians));
            Across = new FieldPoint(-Direction.Z, Direction.X);
            _gustSpeed = breeze.gust.speed;
            _crossFrequency = TwoPi / breeze.gust.wobbleLength * CrossWaveShare;
        }

        /// <summary>The way the wind blows, on the ground (unit length).</summary>
        public FieldPoint Direction { get; }
        /// <summary>A unit vector at a right angle to the wind.</summary>
        public FieldPoint Across { get; }
        public float GustSpacing => _breeze.gust.spacing;

        public BreezePhase PhaseAt(double timeSec)
        {
            var travel = _gustSpeed * timeSec;
            var cells = Math.Floor(travel / _breeze.gust.spacing);
            var shift = travel - cells * _breeze.gust.spacing;
            var cell = cells - Math.Floor(cells / GustCellWrap) * GustCellWrap;

            var drift = _clouds.driftMps * timeSec;
            var nearPeriod = CloudNoise.Period * (double)_clouds.near.cellMetres;
            var farPeriod = CloudNoise.Period * (double)_clouds.far.cellMetres;
            var shadowPeriod = CloudNoise.Period * (double)_clouds.shadow.cellMetres;
            return new BreezePhase(
                (float)shift, (float)cell,
                (float)Fraction(timeSec / _breeze.sway.periodSec), (float)Fraction(timeSec / _breeze.flutter.periodSec), (float)Fraction(timeSec / _breeze.meanPeriodSec),
                (float)Wrap(Direction.X * drift, nearPeriod), (float)Wrap(Direction.Z * drift, nearPeriod),
                (float)Wrap(Direction.X * drift, farPeriod), (float)Wrap(Direction.Z * drift, farPeriod),
                (float)Wrap(Direction.X * drift, shadowPeriod), (float)Wrap(Direction.Z * drift, shadowPeriod));
        }

        /// <summary>How hard the blade with this hash number is pushed, as a factor (one number per blade: the only way blades differ).</summary>
        public float Stiffness(float bladePhase)
        {
            var v = bladePhase * StiffnessSpread + 0.5f;
            return _breeze.stiffnessMin + (_breeze.stiffnessMax - _breeze.stiffnessMin) * (v - (float)Math.Floor(v));
        }

        /// <summary>
        /// The horizontal bend of a blade at ground point (x, z), at <paramref name="height01"/> up its length (0 the foot, 1 the tip), with the hash number <paramref name="bladePhase"/>:
        /// the lean, the gusts and the sway along the wind, the sway's small sideways part, and the tip's slow motion.
        /// </summary>
        public FieldPoint Bend(float x, float z, float height01, float bladePhase, in BreezePhase phase)
        {
            var along = x * Direction.X + z * Direction.Z;
            var across = x * Across.X + z * Across.Z;
            var mean = 1f + _breeze.meanVariation * (float)Math.Sin(TwoPi * phase.MeanPhase);
            var gust = Gust(x, z, height01, phase);
            var swayArgument = TwoPi * (along / _breeze.sway.wavelength - phase.SwayPhase);
            var push = (_breeze.lean + _breeze.gust.amplitude * gust) * mean + _breeze.sway.amplitude * (float)Math.Sin(swayArgument);
            var side = _breeze.sway.amplitude * SwayAcrossShare * (float)Math.Cos(swayArgument);
            var stiffness = Stiffness(bladePhase);
            push *= stiffness;
            side *= stiffness;

            var h3 = height01 * height01 * height01;
            var flutterArgument = TwoPi * (phase.FlutterPhase + FlutterBladeShare * bladePhase) + along * FlutterAlongWind;
            push += _breeze.flutter.amplitude * h3 * (float)Math.Sin(flutterArgument);
            side += _breeze.flutter.amplitude * FlutterAcrossShare * h3 * (float)Math.Cos(flutterArgument);

            return new FieldPoint(Direction.X * push + Across.X * side, Direction.Z * push + Across.Z * side);
        }

        /// <summary>The sum of the gusts at ground point (x, z) as the top of the blade at <paramref name="height01"/> meets them (about 0..1; a little below 0 behind a gust).</summary>
        public float Gust(float x, float z, float height01, in BreezePhase phase)
        {
            var gust = _breeze.gust;
            var along = x * Direction.X + z * Direction.Z;
            var across = x * Across.X + z * Across.Z;
            var cellPosition = (along - phase.GustShift) / gust.spacing;
            var cellFloor = (float)Math.Floor(cellPosition);
            var local = (cellPosition - cellFloor) * gust.spacing;
            var sum = 0f;
            for (var k = FirstNeighbour; k <= LastNeighbour; k++)
            {
                var id = (uint)(((int)cellFloor + k - (int)phase.GustCell) & (GustCellWrap - 1));
                var strength = gust.weakest + (1f - gust.weakest) * BreezeHash.Unit(id * 4u);
                var shiftInCell = (BreezeHash.Unit(id * 4u + 1u) * 2f - 1f) * gust.jitter;
                var wobblePhase = TwoPi * BreezeHash.Unit(id * 4u + 2u);
                var crossPhase = TwoPi * BreezeHash.Unit(id * 4u + 3u);
                var centre = (k + 0.5f + shiftInCell) * gust.spacing;
                var ahead = local - centre + gust.lagMetres * height01 + gust.frontWobble * (float)Math.Sin(across * TwoPi / gust.wobbleLength + wobblePhase);
                var crossStrength = 1f - CrossModulation + CrossModulation * (float)Math.Sin(across * _crossFrequency + crossPhase);
                sum += strength * crossStrength * GustProfile(ahead, gust.width, gust.springBack);
            }
            return sum;
        }

        /// <summary>
        /// One gust along the wind: <paramref name="ahead"/> is the distance in front of its middle (negative behind it). A quick rise (the front is steeper than the back), 1 at the middle,
        /// and a shallow dip behind it (the grass swinging back past upright).
        /// </summary>
        public static float GustProfile(float ahead, float width, float springBack)
        {
            var scale = ahead > 0f ? width * FrontSharpness : width * BackSoftness;
            var body = (float)Math.Exp(-(ahead / scale) * (ahead / scale));
            var lobe = (ahead + SpringLobeDistance * width) / (SpringLobeWidth * width);
            return body - springBack * (float)Math.Exp(-lobe * lobe);
        }

        /// <summary>How much a cloud deck shadows ground point (x, z): 0 in the light, 1 in the middle of a shadow, soft at the edges.</summary>
        public float CloudShadow(float x, float z, in BreezePhase phase)
        {
            var shadow = _clouds.shadow;
            var noise = CloudNoise.Fbm((x - phase.ShadowShiftX) / shadow.cellMetres, (z - phase.ShadowShiftZ) / shadow.cellMetres);
            var edge = 1f - shadow.coverage;
            return SmoothStep(edge - shadow.softness, edge + shadow.softness, noise);
        }

        static float SmoothStep(float low, float high, float v)
        {
            var t = Math.Min(1f, Math.Max(0f, (v - low) / (high - low)));
            return t * t * (3f - 2f * t);
        }

        static double Fraction(double v) => v - Math.Floor(v);

        static double Wrap(double v, double period) => v - Math.Floor(v / period) * period;
    }
}
