using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The parts of the wind that <c>--wind-mute</c> can switch off one at a time (to find out which one makes the grass do something).</summary>
    [Flags]
    public enum WindTerm
    {
        None = 0,
        /// <summary>The whole steady wind (lean, gusts, sway, tip flutter).</summary>
        Breeze = 1,
        /// <summary>The sweeps of the beat and the rings of the notes.</summary>
        Waves = 2,
        /// <summary>The grass parting around a hand.</summary>
        Hands = 4,
        /// <summary>The few dew tips that catch the sun.</summary>
        Sparkle = 8,
        /// <summary>Only the gusts that roll across the field (and their light and the ripple on the plate).</summary>
        Gusts = 16,
        /// <summary>Only the slow sway around the lean.</summary>
        Sway = 32,
        /// <summary>Only the small slow motion of the tips.</summary>
        Flutter = 64,
    }

    /// <summary>Reads <c>--wind-mute breeze,waves,hands,sparkle,gusts,sway,flutter</c> (or <c>all</c>) and hands it to the shaders as 1 = kept, 0 = muted.</summary>
    public static class WindMute
    {
        const string All = "all";

        /// <summary>The muted terms; names that are not a term are collected in <paramref name="unknown"/> (comma-separated) so that a typo is reported, not ignored.</summary>
        public static WindTerm Parse(string text, out string unknown)
        {
            var terms = WindTerm.None;
            var bad = new List<string>();
            foreach (var raw in (text ?? string.Empty).Split(','))
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;
                if (string.Equals(name, All, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (WindTerm term in Enum.GetValues(typeof(WindTerm))) terms |= term;
                    continue;
                }
                if (Enum.TryParse(name, true, out WindTerm one) && one != WindTerm.None && Enum.IsDefined(typeof(WindTerm), one)) terms |= one;
                else bad.Add(name);
            }
            unknown = string.Join(",", bad);
            return terms;
        }

        public static (float breeze, float waves, float hands, float sparkle, float gusts, float sway, float flutter) ToShaderFlags(WindTerm muted) =>
            (Kept(muted, WindTerm.Breeze), Kept(muted, WindTerm.Waves), Kept(muted, WindTerm.Hands), Kept(muted, WindTerm.Sparkle), Kept(muted, WindTerm.Gusts), Kept(muted, WindTerm.Sway), Kept(muted, WindTerm.Flutter));

        static float Kept(WindTerm muted, WindTerm term) => (muted & term) == term ? 0f : 1f;
    }
}
