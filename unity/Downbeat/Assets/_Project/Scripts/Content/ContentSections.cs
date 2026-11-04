using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Content
{
    /// <summary>
    /// Which parts of content.json differ between two copies. While the app runs only the <c>meadow</c> section can be rebuilt (a reload with F5 applies it at the next bar); the others shape things that
    /// were set up when the app started (the sound engine and its samples, the song's grid, the scored songs, the hand input, the conducting play, the two-fists ending, the automatic quality, the nature score, the screens), so a change in them waits for the next start.
    /// The one exception is <c>trail</c>: the meadow's rebuild makes the note trail again from the new content, so a change of <c>trail</c> that comes with a change of <c>meadow</c> is applied by that rebuild (and one that
    /// comes alone waits for the next start, like the others: nothing is rebuilt for it).
    /// </summary>
    public static class ContentSections
    {
        public const string Meadow = "meadow";
        /// <summary>The sections, besides the meadow's own, that the rebuild of the meadow reads again (see <c>MeadowStage.Configure</c>).</summary>
        static readonly string[] ReadByTheMeadowRebuild = { "trail" };

        public static IReadOnlyList<string> Changed(ContentDto before, ContentDto after)
        {
            var changed = new List<string>();
            Compare(changed, "schemaVersion", before.schemaVersion.ToString(), after.schemaVersion.ToString());
            Compare(changed, "audio", Json(before.audio), Json(after.audio));
            Compare(changed, "instruments", Json(before.instruments), Json(after.instruments));
            Compare(changed, "song", Json(before.song), Json(after.song));
            Compare(changed, "songs", Json(before.songs), Json(after.songs));
            Compare(changed, "hand", Json(before.hand), Json(after.hand));
            Compare(changed, Meadow, Json(before.meadow), Json(after.meadow));
            Compare(changed, "nature", Json(before.nature), Json(after.nature));
            Compare(changed, "experience", Json(before.experience), Json(after.experience));
            Compare(changed, "display", Json(before.display), Json(after.display));
            Compare(changed, "record", Json(before.record), Json(after.record));
            Compare(changed, "conduct", Json(before.conduct), Json(after.conduct));
            Compare(changed, "tutorial", Json(before.tutorial), Json(after.tutorial));
            Compare(changed, "cells", Json(before.cells), Json(after.cells));
            Compare(changed, "pose", Json(before.pose), Json(after.pose));
            Compare(changed, "cutoff", Json(before.cutoff), Json(after.cutoff));
            Compare(changed, "autoQuality", Json(before.autoQuality), Json(after.autoQuality));
            Compare(changed, "trail", Json(before.trail), Json(after.trail));
            return changed;
        }

        /// <summary>The changed sections that cannot be applied while the app runs (those the meadow's rebuild reads are applied by it when the meadow section changed too).</summary>
        public static IReadOnlyList<string> NeedingRestart(ContentDto before, ContentDto after)
        {
            var changed = Changed(before, after);
            var meadowIsRebuilt = changed.Contains(Meadow);
            return changed.Where(name => name != Meadow && !(meadowIsRebuilt && ReadByTheMeadowRebuild.Contains(name))).ToList();
        }

        static void Compare(List<string> changed, string name, string before, string after)
        {
            if (before != after) changed.Add(name);
        }

        static string Json(object value) => value == null ? string.Empty : JsonUtility.ToJson(value);

        static string Json(InstrumentDto[] instruments) => instruments == null ? string.Empty : string.Join("\n", instruments.Select(i => JsonUtility.ToJson(i)));

        static string Json(SongEntryDto[] songs) => songs == null ? string.Empty : string.Join("\n", songs.Select(song => Json(song)));
    }
}
