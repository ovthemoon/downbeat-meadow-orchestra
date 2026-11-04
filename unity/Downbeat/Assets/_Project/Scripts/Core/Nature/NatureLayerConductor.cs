using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The three nature layers, in the order they join as the energy grows.</summary>
    public enum NatureLayer { Fireflies = 0, Sky = 1, Hills = 2 }

    /// <summary>One note of the written nature score: which layer, on which beat-grid slot, which scale step and how loud (0..1, relative).</summary>
    public readonly struct NatureNote : IEquatable<NatureNote>
    {
        public NatureNote(NatureLayer layer, long slot, int pitchStep, float gain)
        {
            Layer = layer;
            Slot = slot;
            PitchStep = pitchStep;
            Gain = gain;
        }

        public NatureLayer Layer { get; }
        public long Slot { get; }
        public int PitchStep { get; }
        public float Gain { get; }

        public bool Equals(NatureNote other) => Layer == other.Layer && Slot == other.Slot && PitchStep == other.PitchStep && Gain == other.Gain;
        public override bool Equals(object obj) => obj is NatureNote other && Equals(other);
        public override int GetHashCode() => unchecked((int)Slot * 31 + (int)Layer * 7 + PitchStep);
        public override string ToString() => $"{Layer} slot {Slot} step {PitchStep} gain {Gain:0.00}";
    }

    /// <summary>The rules of the nature score (from <c>content.json</c>, <c>nature.rules</c>).</summary>
    public sealed class NatureRules
    {
        readonly int[][] _chords;

        public NatureRules(int offbeatSlot, int slotsPerBeat, float fireflyChance, int fireflyTopSteps, float fireflyGain, int[][] chords, int barsPerChord, float skyGain, int hillsAccentSlot, float hillsAccentGain, float hillsGain)
        {
            if (slotsPerBeat < 1) throw new ArgumentOutOfRangeException(nameof(slotsPerBeat), slotsPerBeat, "must be at least 1");
            if (offbeatSlot < 0 || offbeatSlot >= slotsPerBeat) throw new ArgumentOutOfRangeException(nameof(offbeatSlot), offbeatSlot, "must be a slot inside a beat");
            if (!(fireflyChance >= 0f && fireflyChance <= 1f)) throw new ArgumentOutOfRangeException(nameof(fireflyChance), fireflyChance, "0..1");
            if (fireflyTopSteps < 1) throw new ArgumentOutOfRangeException(nameof(fireflyTopSteps), fireflyTopSteps, "must be at least 1");
            if (chords == null || chords.Length == 0) throw new ArgumentException("at least one chord is needed", nameof(chords));
            if (barsPerChord < 1) throw new ArgumentOutOfRangeException(nameof(barsPerChord), barsPerChord, "must be at least 1");
            if (hillsAccentSlot < 0) throw new ArgumentOutOfRangeException(nameof(hillsAccentSlot), hillsAccentSlot, "must not be negative");
            CheckGain(fireflyGain, nameof(fireflyGain));
            CheckGain(skyGain, nameof(skyGain));
            CheckGain(hillsAccentGain, nameof(hillsAccentGain));
            CheckGain(hillsGain, nameof(hillsGain));

            _chords = new int[chords.Length][];
            for (var i = 0; i < chords.Length; i++)
            {
                if (chords[i] == null || chords[i].Length == 0) throw new ArgumentException($"chord {i} has no notes", nameof(chords));
                _chords[i] = (int[])chords[i].Clone();
            }

            OffbeatSlot = offbeatSlot;
            SlotsPerBeat = slotsPerBeat;
            FireflyChance = fireflyChance;
            FireflyTopSteps = fireflyTopSteps;
            FireflyGain = fireflyGain;
            BarsPerChord = barsPerChord;
            SkyGain = skyGain;
            HillsAccentSlot = hillsAccentSlot;
            HillsAccentGain = hillsAccentGain;
            HillsGain = hillsGain;
        }

        static void CheckGain(float gain, string name)
        {
            if (!(gain >= 0f && gain <= 1f)) throw new ArgumentOutOfRangeException(name, gain, "0..1");
        }

        public int OffbeatSlot { get; }
        public int SlotsPerBeat { get; }
        public float FireflyChance { get; }
        public int FireflyTopSteps { get; }
        public float FireflyGain { get; }
        public int BarsPerChord { get; }
        public float SkyGain { get; }
        public int HillsAccentSlot { get; }
        public float HillsAccentGain { get; }
        public float HillsGain { get; }
        public int ChordCount => _chords.Length;

        public int[] ChordSteps(int index) => (int[])_chords[index].Clone();
        internal int[] ChordStepsUnsafe(int index) => _chords[index];
    }

    /// <summary>
    /// Writes the nature score for a range of beat-grid slots and an energy level: fireflies (bells between the beats) from level 1, the sky (a chord on
    /// the bar line) from level 2, the hills (a low drum) at level 3. It keeps no state of what it has played: what a slot holds depends only on the slot, the seed and
    /// the chord source (when there is one), so the plan does not change with how the range is cut, and a run can be played again note for note.
    /// </summary>
    public sealed class NatureLayerConductor
    {
        const uint SaltChance = 0x1111u;
        const uint SaltPitch = 0x2222u;
        const uint SaltGain = 0x3333u;
        const float FireflyGainSpread = 0.4f;

        readonly NatureRules _rules;
        readonly int _slotsPerBar;
        readonly int _pitchSteps;
        readonly int _seed;
        Func<long, int[]> _chordSource;

        public NatureLayerConductor(NatureRules rules, int slotsPerBar, int pitchSteps, int seed)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (slotsPerBar < 1) throw new ArgumentOutOfRangeException(nameof(slotsPerBar), slotsPerBar, "must be at least 1");
            if (pitchSteps < 1) throw new ArgumentOutOfRangeException(nameof(pitchSteps), pitchSteps, "must be at least 1");
            if (rules.FireflyTopSteps > pitchSteps) throw new ArgumentException("the fireflies use more steps than the scale has", nameof(rules));
            if (rules.HillsAccentSlot >= slotsPerBar) throw new ArgumentException("the drum accent is outside the bar", nameof(rules));
            if (slotsPerBar % rules.SlotsPerBeat != 0) throw new ArgumentException("a bar has to hold whole beats", nameof(slotsPerBar));
            for (var c = 0; c < rules.ChordCount; c++)
                foreach (var step in rules.ChordStepsUnsafe(c))
                    if (step < 0 || step >= pitchSteps) throw new ArgumentException($"chord {c} has step {step} outside the scale (0..{pitchSteps - 1})", nameof(rules));

            _slotsPerBar = slotsPerBar;
            _pitchSteps = pitchSteps;
            _seed = seed;
        }

        /// <summary>
        /// Lets something outside choose the chord of the sky: <paramref name="stepsForBar"/> is asked for the scale steps of a bar (counted from slot 0, so it can be negative) each time the
        /// sky plays one. No answer (null or empty), or a step outside the scale, puts the chord of the rules back for that bar. <c>null</c> takes the source away again.
        /// </summary>
        public void SetChordSource(Func<long, int[]> stepsForBar) => _chordSource = stepsForBar;

        /// <summary>Appends the notes of the slots in [<paramref name="fromSlot"/>, <paramref name="toSlotExclusive"/>) for <paramref name="level"/> (0 = silence).</summary>
        public void Plan(long fromSlot, long toSlotExclusive, int level, List<NatureNote> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (level <= 0) return;
            for (var slot = fromSlot; slot < toSlotExclusive; slot++) PlanSlot(slot, level, output);
        }

        void PlanSlot(long slot, int level, List<NatureNote> output)
        {
            if (level >= 1) PlanFirefly(slot, output);

            var slotInBar = FloorMod(slot, _slotsPerBar);
            if (level >= 2 && slotInBar == 0) PlanChord(slot, output);
            if (level >= 3) PlanDrum(slot, slotInBar, output);
        }

        void PlanFirefly(long slot, List<NatureNote> output)
        {
            if (FloorMod(slot, _rules.SlotsPerBeat) != _rules.OffbeatSlot) return;
            if (Unit(slot, SaltChance) >= _rules.FireflyChance) return;

            var pick = Math.Min(_rules.FireflyTopSteps - 1, (int)(Unit(slot, SaltPitch) * _rules.FireflyTopSteps));
            var gain = _rules.FireflyGain * (1f - FireflyGainSpread * Unit(slot, SaltGain));
            output.Add(new NatureNote(NatureLayer.Fireflies, slot, _pitchSteps - _rules.FireflyTopSteps + pick, gain));
        }

        void PlanChord(long slot, List<NatureNote> output)
        {
            var bar = FloorDiv(slot, _slotsPerBar);
            var steps = _chordSource?.Invoke(bar);
            if (!IsPlayableChord(steps))
            {
                var index = (int)FloorMod(FloorDiv(bar, _rules.BarsPerChord), _rules.ChordCount);
                steps = _rules.ChordStepsUnsafe(index);
            }
            foreach (var step in steps) output.Add(new NatureNote(NatureLayer.Sky, slot, step, _rules.SkyGain));
        }

        /// <summary>True when the chord has notes and every one of them is a step of the scale: a chord from outside is not trusted, and one bad step spoils the whole bar.</summary>
        bool IsPlayableChord(int[] steps)
        {
            if (steps == null || steps.Length == 0) return false;
            foreach (var step in steps)
                if (step < 0 || step >= _pitchSteps) return false;
            return true;
        }

        void PlanDrum(long slot, long slotInBar, List<NatureNote> output)
        {
            if (slotInBar == 0) output.Add(new NatureNote(NatureLayer.Hills, slot, 0, _rules.HillsGain));
            else if (slotInBar == _rules.HillsAccentSlot) output.Add(new NatureNote(NatureLayer.Hills, slot, 0, _rules.HillsGain * _rules.HillsAccentGain));
        }

        /// <summary>A number in [0, 1) that depends only on the seed, the slot and the salt (a splitmix-style hash).</summary>
        float Unit(long slot, uint salt)
        {
            unchecked
            {
                var x = (ulong)slot * 0x9E3779B97F4A7C15UL + (ulong)(uint)_seed * 0xBF58476D1CE4E5B9UL + salt * 0x94D049BB133111EBUL;
                x ^= x >> 30;
                x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27;
                x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (x >> 40) / 16777216f;
            }
        }

        static long FloorMod(long value, long modulus)
        {
            var r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        static long FloorDiv(long value, long divisor)
        {
            var q = value / divisor;
            return value % divisor < 0 ? q - 1 : q;
        }
    }
}
