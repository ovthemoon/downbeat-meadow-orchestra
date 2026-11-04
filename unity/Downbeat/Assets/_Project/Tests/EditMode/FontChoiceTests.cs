using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>Which of the wanted fonts are installed (the immediate mode GUI draws nothing when the first name is missing), and that the Tab panel makes its font from them.</summary>
    public class FontChoiceTests
    {
        static readonly string[] Wanted = { "Apple SD Gothic Neo", "Malgun Gothic", "NanumGothic", "Noto Sans CJK KR" };

        [Test]
        public void InstalledFirst_FirstWantedMissing_StartsWithTheSecond()
        {
            var installed = new[] { "Arial", "Malgun Gothic", "Noto Sans CJK KR" };

            var chosen = FontChoice.InstalledFirst(Wanted, installed);

            Assert.AreEqual(new[] { "Malgun Gothic", "Noto Sans CJK KR" }, chosen);
        }

        [Test]
        public void InstalledFirst_KeepsTheWantedOrderNotTheInstalledOrder()
        {
            var installed = new[] { "Noto Sans CJK KR", "NanumGothic", "Apple SD Gothic Neo" };

            var chosen = FontChoice.InstalledFirst(Wanted, installed);

            Assert.AreEqual(new[] { "Apple SD Gothic Neo", "NanumGothic", "Noto Sans CJK KR" }, chosen);
        }

        [Test]
        public void InstalledFirst_IgnoresCase_AndReturnsTheInstalledSpelling()
        {
            var installed = new[] { "MALGUN GOTHIC", "nanumgothic" };

            var chosen = FontChoice.InstalledFirst(Wanted, installed);

            Assert.AreEqual(new[] { "MALGUN GOTHIC", "nanumgothic" }, chosen);
        }

        [Test]
        public void InstalledFirst_KoreanWindowsNames_AreFoundAndKeptWithTheWantedSpelling()
        {
            var installed = new[] { "Arial", "맑은 고딕", "나눔고딕", "Noto Sans CJK KR" };

            var chosen = FontChoice.InstalledFirst(Wanted, installed);

            Assert.AreEqual(new[] { "맑은 고딕", "Malgun Gothic", "나눔고딕", "NanumGothic", "Noto Sans CJK KR" }, chosen, "the installed spelling first, the wanted one right after it");
        }

        [Test]
        public void InstalledFirst_MixedEnglishAndKoreanNames_KeepTheWantedOrder()
        {
            var installed = new[] { "Malgun Gothic", "나눔고딕", "Apple SD 산돌고딕 Neo" };

            var chosen = FontChoice.InstalledFirst(Wanted, installed);

            Assert.AreEqual(new[] { "Apple SD 산돌고딕 Neo", "Apple SD Gothic Neo", "Malgun Gothic", "나눔고딕", "NanumGothic" }, chosen);
        }

        [Test]
        public void InstalledFirst_IgnoresSpaces()
        {
            Assert.AreEqual(new[] { "나눔 고딕", "NanumGothic" }, FontChoice.InstalledFirst(Wanted, new[] { "나눔 고딕" }), "a Korean name with a space");
            Assert.AreEqual(new[] { "Nanum Gothic", "NanumGothic" }, FontChoice.InstalledFirst(Wanted, new[] { "Nanum Gothic" }), "an English name with a space");
            Assert.AreEqual(new[] { "NanumGothic", "나눔 고딕" }, FontChoice.InstalledFirst(new[] { "나눔 고딕" }, new[] { "NanumGothic" }), "a wanted Korean name finds the English installed one");
        }

        [Test]
        public void InstalledFirst_OnlyTheWantedFamiliesAreMatched()
        {
            var chosen = FontChoice.InstalledFirst(new[] { "Malgun Gothic" }, new[] { "나눔고딕", "MS Gothic" });

            Assert.IsEmpty(chosen, "another family is not an alias, even when it has Gothic in its name");
        }

        [Test]
        public void InstalledFirst_NoneInstalled_IsEmpty()
        {
            var chosen = FontChoice.InstalledFirst(Wanted, new[] { "Arial", "Times New Roman" });

            Assert.IsEmpty(chosen);
        }

        [Test]
        public void InstalledFirst_RepeatedWantedName_AppearsOnce()
        {
            var chosen = FontChoice.InstalledFirst(new[] { "Malgun Gothic", "malgun gothic", "NanumGothic" }, new[] { "Malgun Gothic", "NanumGothic" });

            Assert.AreEqual(new[] { "Malgun Gothic", "NanumGothic" }, chosen);
        }

        [Test]
        public void InstalledFirst_EmptyAndNullNames_AreSkipped()
        {
            var chosen = FontChoice.InstalledFirst(new[] { null, "", "Malgun Gothic" }, new[] { null, "", "Malgun Gothic" });

            Assert.AreEqual(new[] { "Malgun Gothic" }, chosen);
        }

        [Test]
        public void InstalledFirst_NullInputs_AreEmpty()
        {
            Assert.IsEmpty(FontChoice.InstalledFirst(null, new[] { "Arial" }), "no wanted list");
            Assert.IsEmpty(FontChoice.InstalledFirst(Wanted, null), "no installed list");
            Assert.IsEmpty(FontChoice.InstalledFirst(null, null), "neither");
        }

        [Test]
        public void KoreanLooking_ListsHangulNamesFirst_ThenTheOtherHintsInOrder_UpToTheLimit()
        {
            var installed = new[] { "Arial", "MS Gothic", "맑은 고딕", "Times New Roman", "Noto Serif", "나눔고딕", "NANUMBARUN", "Malgun Gothic Semilight", "Calibri" };

            Assert.AreEqual(new[] { "맑은 고딕", "나눔고딕", "MS Gothic", "Noto Serif", "NANUMBARUN", "Malgun Gothic Semilight" }, FontChoice.KoreanLooking(installed));
            Assert.AreEqual(new[] { "맑은 고딕", "나눔고딕", "MS Gothic" }, FontChoice.KoreanLooking(installed, 3), "at most the limit, the Hangul names are kept before the others");
            Assert.AreEqual(new[] { "맑은 고딕" }, FontChoice.KoreanLooking(installed, 1));
            Assert.IsEmpty(FontChoice.KoreanLooking(null), "no list");
        }

        [Test]
        public void KoreanLooking_ManyGothicNamesBeforeAHangulOne_StillShowsTheHangulOne()
        {
            var installed = Enumerable.Range(0, 30).Select(n => "Gothic " + n).Append("맑은 고딕").ToArray();

            var listed = FontChoice.KoreanLooking(installed);

            Assert.AreEqual(FontChoice.MaxListedFonts, listed.Length);
            Assert.AreEqual("맑은 고딕", listed[0], "the Hangul name is not pushed out by the first ten Gothic names");
        }

        [Test]
        public void UsedFontsLine_ListsTheNamesInOrder()
        {
            Assert.AreEqual("[songs] panel font: 맑은 고딕, Malgun Gothic", FontChoice.UsedFontsLine(new[] { "맑은 고딕", "Malgun Gothic" }));
        }

        [Test]
        public void NoneInstalledWarning_NamesTheWantedFonts_AndTheInstalledOnesThatLookKorean()
        {
            var installed = new[] { "Arial" }.Concat(Enumerable.Range(0, 14).Select(n => "Gothic " + n)).ToArray();

            var line = FontChoice.NoneInstalledWarning(Wanted, installed);

            StringAssert.StartsWith("[songs] none of the panel fonts is installed: Apple SD Gothic Neo, Malgun Gothic, NanumGothic, Noto Sans CJK KR;", line);
            StringAssert.Contains("15 installed in all", line);
            StringAssert.Contains("Gothic 0, Gothic 1", line);
            StringAssert.Contains("Gothic 9", line);
            StringAssert.DoesNotContain("Gothic 10", line, "ten names at most");
            Assert.IsFalse(line.Contains("\n"), "one line");
            StringAssert.EndsWith("none", FontChoice.NoneInstalledWarning(Wanted, new[] { "Arial" }), "nothing that looks Korean says so");
        }

        [Test]
        public void SettingsPanel_FirstWantedFontMissing_BuildsItsFontFromTheInstalledOne()
        {
            var installedNames = Font.GetOSInstalledFontNames();
            if (installedNames == null || installedNames.Length == 0) Assert.Ignore("this machine reports no installed fonts");
            var host = new GameObject("panel-font-test");
            try
            {
                var panel = host.AddComponent<SettingsPanelView>();

                LogAssert.Expect(LogType.Log, "[songs] panel font: " + installedNames[0]);
                panel.Configure(new[] { "No Such Font Gothic", installedNames[0] }, null);

                Assert.IsNotNull(panel.FontNamesInUse, "the panel made a font");
                Assert.AreEqual(new[] { installedNames[0] }, panel.FontNamesInUse, "the missing first name is left out, so the immediate mode GUI still draws the words");

                LogAssert.Expect(LogType.Log, "[songs] panel font: " + installedNames[installedNames.Length - 1]);
                panel.Configure(new[] { "No Such Font Gothic", installedNames[installedNames.Length - 1] }, null);   // a second call releases the first font (a Destroy would fail in edit mode)

                Assert.AreEqual(new[] { installedNames[installedNames.Length - 1] }, panel.FontNamesInUse, "the second call made a new font");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SettingsPanel_NoWantedFontInstalled_WarnsOnceAndKeepsTheWantedList()
        {
            var wanted = new[] { "No Such A", "No Such B" };
            var host = new GameObject("panel-font-test");
            try
            {
                var panel = host.AddComponent<SettingsPanelView>();

                LogAssert.Expect(LogType.Warning, new Regex(@"^\[songs\] none of the panel fonts is installed: No Such A, No Such B; installed fonts that look Korean"));
                panel.Configure(wanted, null);

                Assert.AreEqual(wanted, panel.FontNamesInUse, "nothing is installed, so the call is the old one with the wanted list");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
