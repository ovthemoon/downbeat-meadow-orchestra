using System;

namespace Downbeat.Core
{
    /// <summary>Builds the (validating) Core settings of the nature layers from the <c>nature</c> and <c>experience</c> blocks of content.json.</summary>
    public static class NatureSettingsFactory
    {
        public static EnergySettings Energy(NatureEnergyDto e)
        {
            if (e == null) throw new ArgumentException("nature.energy is missing");
            return new EnergySettings(e.riseBase, e.risePerVelocity, e.halfLifeSec, e.level1, e.level2, e.level3, e.hysteresis);
        }

        /// <param name="slotsPerBeat">From the song: the rules say which slot of a beat is the off-beat.</param>
        public static NatureRules Rules(NatureRulesDto r, int slotsPerBeat)
        {
            if (r == null) throw new ArgumentException("nature.rules is missing");
            var chords = new int[r.chords == null ? 0 : r.chords.Length][];
            for (var i = 0; i < chords.Length; i++) chords[i] = r.chords[i]?.steps ?? Array.Empty<int>();
            return new NatureRules(r.offbeatSlot, slotsPerBeat, r.fireflyChance, r.fireflyTopSteps, r.fireflyGain, chords, r.barsPerChord, r.skyGain, r.hillsAccentSlot, r.hillsAccentGain, r.hillsGain);
        }

        public static ExperienceSettings Experience(ExperienceDto x)
        {
            if (x == null) throw new ArgumentException("experience is missing");
            return new ExperienceSettings(x.wakingSec, x.noHandsSec, x.idleEnergy, x.settlingMinSec);
        }
    }
}
