using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>What the meadow sees of a played note: which instrument, which pitch step, how hard, when it is heard and where on the ground it was played.</summary>
    public readonly struct MeadowNoteEvent
    {
        public Part Part { get; }
        public int PitchStep { get; }
        public float Velocity { get; }
        /// <summary>Audio-clock time at which the note is heard.</summary>
        public double AudibleTime { get; }
        /// <summary>
        /// Where on the ground a note of the lower row was played: the ground under the hand's place on the screen (<see cref="GroundOfHand"/>), the flowers around it open and the wave starts there. A note that did not know where the hand was
        /// (a keyboard key, a replay without a height) has X across the field where its column is and Z the depth of the pitch step (higher notes are farther). A note of the upper row has the same, and does nothing on the ground.
        /// </summary>
        public FieldPoint FieldPos { get; }
        /// <summary>Where across the picture the hand played, 0 left .. 1 right; NaN when the input did not know (the middle of the part's column is then the place on the ground, and the effect in the air has none).</summary>
        public float X01 { get; }
        /// <summary>Where up and down the picture the hand played, 0 top .. 1 bottom (a mouse click: the screen's y); NaN when the input did not know. The effects of the upper row are drawn at <see cref="HandScreenMap.Viewport"/> of (X01, Y01).</summary>
        public float Y01 { get; }

        public MeadowNoteEvent(Part part, int pitchStep, float velocity, double audibleTime, FieldPoint fieldPos, float x01 = float.NaN, float y01 = float.NaN)
        {
            Part = part; PitchStep = pitchStep; Velocity = velocity; AudibleTime = audibleTime; FieldPos = fieldPos; X01 = x01; Y01 = y01;
        }
    }

    /// <summary>
    /// The ground under a hand: the point of the meadow that is drawn at the screen place of a hand at (<paramref name="x01"/>, <paramref name="y01"/>) of the tracker's picture (see <see cref="HandScreenMap"/>).
    /// False when the ground cannot be found there.
    /// </summary>
    public delegate bool GroundOfHand(float x01, float y01, out FieldPoint ground);

    /// <summary>Something that can open flowers (the flower field; a recorder in the tests).</summary>
    public interface IBloomTarget
    {
        void Bloom(IReadOnlyList<int> flowerIndices, double audibleTime);
    }

    /// <summary>
    /// Turns every scheduled note of the lower row (the ground) into what the meadow does with it: the flowers of its column's species around the spot under the hand open (timed to when the note is HEARD) and the
    /// grass is pushed by a wave that starts at that moment (the low string's long wave, the hand drum's wide ring, the marimba's ring of a note). A note of the upper row (the sky) opens no flowers and pushes no grass: it plays in the air, and only <see cref="NoteRouted"/> tells
    /// of it. Others (particles, the sky effects, the score) listen to <see cref="NoteRouted"/>.
    /// </summary>
    public sealed class MeadowNoteRouter
    {
        readonly FlowerInstance[] _flowers;
        readonly MeadowLayout _layout;
        readonly WindField _wind;
        readonly IBloomTarget _bloom;
        readonly MeadowFlowersDto _flowerSettings;
        readonly MeadowWindDto _windSettings;
        readonly int _pitchSteps;
        readonly Func<double, double, double> _toWindTime;
        readonly GroundOfHand _groundOfHand;
        readonly List<int> _scratch = new List<int>(16);

        /// <param name="toWindTime">(audible time, heard-now), both audio time -> the time of the wind clock at which the note is heard.</param>
        /// <param name="groundOfHand">Where on the ground a note of the lower row was played, from where the hand is on the screen; null (or a note without a hand place) puts it where the pitch step and the column say.</param>
        public MeadowNoteRouter(FlowerInstance[] flowers, MeadowLayout layout, WindField wind, IBloomTarget bloom, MeadowFlowersDto flowerSettings, MeadowWindDto windSettings,
            int pitchSteps, Func<double, double, double> toWindTime, GroundOfHand groundOfHand = null)
        {
            _flowers = flowers;
            _layout = layout;
            _wind = wind;
            _bloom = bloom;
            _flowerSettings = flowerSettings;
            _windSettings = windSettings;
            _pitchSteps = pitchSteps;
            _toWindTime = toWindTime;
            _groundOfHand = groundOfHand;
        }

        public event Action<MeadowNoteEvent> NoteRouted;

        /// <param name="heardNow">The audio time of the sample leaving the speakers now (the note is heard at its audio time, which is later).</param>
        public void OnNote(in ScheduledNote note, double heardNow)
        {
            var species = SpeciesMap.ForPart(note.Part);
            var at = species.HasValue && TryGroundUnderHand(note, out var under) ? under : PitchSpot(note);
            var routed = new MeadowNoteEvent(note.Part, note.PitchStep, note.Velocity, note.AudioTime, at, note.X01, note.Y01);

            if (species.HasValue) RouteGroundNote(note, heardNow, at, species.Value);

            NoteRouted?.Invoke(routed);
        }

        // where the pitch step and the column of a note put it, for a note that does not know where the hand was
        FieldPoint PitchSpot(in ScheduledNote note)
        {
            var z = _layout.DepthForPitchStep(note.PitchStep, _pitchSteps);
            var across = float.IsNaN(note.X01) ? SpeciesMap.CentreOf(note.Part) : note.X01;
            return _layout.HandToField(across, _layout.HandYForDepth(z));
        }

        bool TryGroundUnderHand(in ScheduledNote note, out FieldPoint ground)
        {
            ground = default;
            return _groundOfHand != null && !float.IsNaN(note.X01) && !float.IsNaN(note.Y01) && _groundOfHand(note.X01, note.Y01, out ground);
        }

        void RouteGroundNote(in ScheduledNote note, double heardNow, FieldPoint at, FlowerSpecies species)
        {
            var count = FlowerSelector.CountFor(note.Velocity, _flowerSettings.maxPerNote);
            FlowerSelector.Select(_flowers, species, at, count, _scratch);
            if (_scratch.Count > 0) _bloom.Bloom(_scratch, note.AudioTime);

            _wind.Add(WaveOf(note.Part, at, note.Velocity, _toWindTime(note.AudioTime, heardNow)));
        }

        /// <summary>The wave a note of the lower row makes in the grass: the low string lays it in a long wave towards the side of the hand, the hand drum thumps it with a strong wide ring, the marimba pushes it in the ring of a note.</summary>
        WindWave WaveOf(Part part, FieldPoint at, float velocity, double windTime)
        {
            switch (part)
            {
                case Part.LowString: return WindWaves.Long(at, velocity, windTime, _windSettings.longWave);
                case Part.HandDrum: return WindWaves.Drum(at, velocity, windTime, _windSettings.drumRing);
                default:
                    var radial = _windSettings.noteRadial;
                    var amplitude = radial.amplitude * (1f + radial.velocityGain * Clamp01(velocity));
                    return new WindWave(at.X, at.Z, windTime, amplitude, radial.speed, radial.lifeSec, WindWaveKind.Radial, 0f);
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
