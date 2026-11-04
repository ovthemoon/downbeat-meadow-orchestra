using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Downbeat.Core
{
    /// <summary>
    /// Which of the wanted operating-system fonts are really there. The immediate mode GUI does not fall back to a later name of
    /// <c>Font.CreateDynamicFontFromOSFont(names, size)</c> when the first name is not installed (it draws no text at all, e.g. "Apple SD Gothic Neo" on Windows),
    /// so a font for it is made from the installed names only.
    /// <para>
    /// A Korean Windows may list a family by its Korean name ("맑은 고딕", not "Malgun Gothic"), so a wanted name also matches the other spellings of its family
    /// (<see cref="Families"/>), and names are compared without regard to case or spaces.
    /// </para>
    /// </summary>
    public static class FontChoice
    {
        /// <summary>The most installed names that look Korean that <see cref="NoneInstalledWarning"/> lists.</summary>
        public const int MaxListedFonts = 10;

        static readonly string[] KoreanNameHints = { "Gothic", "Nanum", "Noto", "Malgun" };

        /// <summary>The spellings of one family that an operating system may list it under (the English one and the Korean one). Spaces and case do not matter, so "Nanum Gothic" is "NanumGothic".</summary>
        static readonly string[][] Families =
        {
            new[] { "Apple SD Gothic Neo", "Apple SD 산돌고딕 Neo" },
            new[] { "Malgun Gothic", "맑은 고딕" },
            new[] { "NanumGothic", "나눔고딕" },
        };

        static readonly Dictionary<string, string[]> FamilyOfKey = Families
            .SelectMany(family => family.Select(name => (key: Key(name), family)))
            .ToDictionary(entry => entry.key, entry => entry.family, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// For each wanted name, in the wanted order: the installed names of its family, in the spelling of the installed list, and then the wanted spelling itself (so either resolver of the system finds one).
        /// A wanted name that has no installed spelling adds nothing. A name is listed once (case ignored). Empty when none is installed or an input is null.
        /// </summary>
        public static string[] InstalledFirst(IReadOnlyList<string> wanted, IReadOnlyList<string> installed)
        {
            if (wanted == null || installed == null) return Array.Empty<string>();
            var installedByKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in installed)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var key = Key(name);
                if (!installedByKey.TryGetValue(key, out var spellings)) installedByKey[key] = spellings = new List<string>();
                spellings.Add(name);
            }
            var chosen = new List<string>();
            foreach (var name in wanted)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var found = SpellingsOf(name).SelectMany(spelling => installedByKey.TryGetValue(Key(spelling), out var list) ? list : new List<string>()).ToList();
                if (found.Count == 0) continue;
                foreach (var spelling in found.Append(name)) AddOnce(chosen, spelling);
            }
            return chosen.ToArray();
        }

        /// <summary>The installed names that look like a Korean font, at most <paramref name="max"/>: the names with a Hangul letter first, then the ones with "Gothic", "Nanum", "Noto" or "Malgun" in them, each group in the installed order.</summary>
        public static string[] KoreanLooking(IReadOnlyList<string> installed, int max = MaxListedFonts)
        {
            if (installed == null || max <= 0) return Array.Empty<string>();
            var named = installed.Where(name => !string.IsNullOrEmpty(name)).ToList();
            var hangul = named.Where(name => name.Any(IsHangul));
            var hinted = named.Where(name => !name.Any(IsHangul) && HasKoreanHint(name));
            return hangul.Concat(hinted).Take(max).ToArray();
        }

        /// <summary>The one info line that says which names the panel's font was made from.</summary>
        public static string UsedFontsLine(IReadOnlyList<string> used) => "[songs] panel font: " + (used == null ? string.Empty : string.Join(", ", used));

        /// <summary>The one line to log when none of the wanted fonts is installed: what was wanted and which installed names look Korean, so the first log of a new machine shows the real spelling.</summary>
        public static string NoneInstalledWarning(IReadOnlyList<string> wanted, IReadOnlyList<string> installed)
        {
            var line = new StringBuilder("[songs] none of the panel fonts is installed: ");
            line.Append(wanted == null ? string.Empty : string.Join(", ", wanted));
            var korean = KoreanLooking(installed);
            line.Append("; installed fonts that look Korean (").Append(installed?.Count ?? 0).Append(" installed in all): ");
            line.Append(korean.Length == 0 ? "none" : string.Join(", ", korean));
            return line.ToString();
        }

        static IEnumerable<string> SpellingsOf(string name) => FamilyOfKey.TryGetValue(Key(name), out var family) ? family : new[] { name };

        static void AddOnce(List<string> names, string name)
        {
            if (!names.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
        }

        /// <summary>A name without its spaces; the comparisons of the names ignore case on top of that.</summary>
        static string Key(string name) => new string(name.Where(letter => !char.IsWhiteSpace(letter)).ToArray());

        static bool HasKoreanHint(string name) => KoreanNameHints.Any(hint => name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);

        static bool IsHangul(char letter) =>
            (letter >= '가' && letter <= '힣')      // syllables
            || (letter >= 'ᄀ' && letter <= 'ᇿ')   // jamo
            || (letter >= '㄰' && letter <= '㆏');  // compatibility jamo
    }
}
