namespace Downbeat.Core
{
    /// <summary>
    /// The two waves of the bottom row that are not a note's plain ring (<c>meadow.wind.noteRadial</c>): the long low wave of the low string and the strong wide ring of the hand drum. They only build the
    /// <see cref="WindWave"/> (<paramref name="startTime"/> is the moment the note is heard, on the clock of the wind); whoever owns the <see cref="WindField"/> adds it.
    /// </summary>
    public static class WindWaves
    {
        static readonly WindLongWaveDto DefaultLong = new WindLongWaveDto();
        static readonly WindDrumRingDto DefaultDrum = new WindDrumRingDto();

        /// <summary>
        /// A sweep that starts where the hand is and runs towards the side of the field the hand is on, lying the grass that way (a hand left of the middle lays it towards the left edge, at the middle or right of it
        /// towards the right edge). It is the same at every depth, <c>widthScale</c> times as wide as the usual front, and as strong as <c>amplitude * (1 + velocityGain * velocity01)</c>.
        /// </summary>
        public static WindWave Long(FieldPoint at, float velocity01, double startTime, WindLongWaveDto settings)
        {
            var s = settings ?? DefaultLong;
            var direction = at.X < 0f ? -1f : 1f;
            return new WindWave(at.X, at.Z, startTime, Strength(s.amplitude, s.velocityGain, velocity01), direction * s.speed, s.lifeSec, WindWaveKind.Sweep, 0f, s.widthScale);
        }

        /// <summary>A ring that spreads from where the drum was hit: strong (<c>amplitude * (1 + velocityGain * velocity01)</c>), wide (<c>widthScale</c>) and quick.</summary>
        public static WindWave Drum(FieldPoint at, float velocity01, double startTime, WindDrumRingDto settings)
        {
            var s = settings ?? DefaultDrum;
            return new WindWave(at.X, at.Z, startTime, Strength(s.amplitude, s.velocityGain, velocity01), s.speed, s.lifeSec, WindWaveKind.Radial, 0f, s.widthScale);
        }

        static float Strength(float amplitude, float velocityGain, float velocity01)
        {
            var v = velocity01 < 0f ? 0f : (velocity01 > 1f ? 1f : velocity01);
            return amplitude * (1f + velocityGain * v);
        }
    }
}
