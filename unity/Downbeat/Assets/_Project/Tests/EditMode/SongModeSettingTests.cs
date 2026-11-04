using System;
using System.IO;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>Which value of the song mode switch a run starts with (the command line beats the saved choice, which beats the content), and how the choice is kept in a small file.</summary>
    public class SongModeSettingTests
    {
        [Test]
        public void Resolve_LaunchBeatsSavedBeatsContent()
        {
            foreach (var saved in new bool?[] { true, false })
            {
                foreach (var content in new[] { true, false })
                {
                    Assert.IsTrue(SongModeSetting.Resolve("on", saved, content), $"--songs on wins (saved {saved}, content {content})");
                    Assert.IsFalse(SongModeSetting.Resolve("off", saved, content), $"--songs off wins (saved {saved}, content {content})");
                    Assert.AreEqual(saved.Value, SongModeSetting.Resolve(null, saved, content), $"no option: the saved choice wins (content {content})");
                }
            }
            Assert.IsTrue(SongModeSetting.Resolve(null, null, true), "nothing else: the content value");
            Assert.IsFalse(SongModeSetting.Resolve(null, null, false), "nothing else: the content value");
        }

        [Test]
        public void Resolve_LaunchValueIsNotCaseSensitive_AndAnythingElseIsNotAnAnswer()
        {
            Assert.IsFalse(SongModeSetting.Resolve("OFF", true, true));
            Assert.IsTrue(SongModeSetting.Resolve(" On ", false, false));
            Assert.IsFalse(SongModeSetting.Resolve("", false, true), "a bare --songs says nothing: the saved choice decides");
            Assert.IsTrue(SongModeSetting.Resolve("maybe", null, true), "an unknown value is ignored: the content value decides");
        }

        [Test]
        public void Resolve_NamesAnUnknownLaunchValueAndWhatIsUsedInstead()
        {
            foreach (var value in new[] { "true", "1", "yes", "" })
            {
                var fromSaved = SongModeSetting.Resolve(value, false, true, out var savedProblem);
                Assert.IsFalse(fromSaved, $"'{value}' answers nothing: the saved choice decides");
                Assert.AreEqual($"--songs '{value}' is not on or off; the saved choice (off) is used", savedProblem);

                var fromContent = SongModeSetting.Resolve(value, null, true, out var contentProblem);
                Assert.IsTrue(fromContent, $"'{value}' answers nothing: with no saved choice the content value decides");
                Assert.AreEqual($"--songs '{value}' is not on or off; the content's choice (on) is used", contentProblem);
            }
        }

        [Test]
        public void Resolve_HasNothingToSayAboutAValueItUnderstoodOrAnOptionThatWasNotGiven()
        {
            foreach (var value in new[] { null, "on", "off", "ON", " Off " })
            {
                SongModeSetting.Resolve(value, true, false, out var problem);
                Assert.IsNull(problem, $"'{value}' is fine");
            }
        }

        [Test]
        public void Encode_IsTheSmallJsonOfTheSpec()
        {
            Assert.AreEqual("{\"songMode\":true}", SongModeSetting.Encode(true));
            Assert.AreEqual("{\"songMode\":false}", SongModeSetting.Encode(false));
        }

        [Test]
        public void SwitchLogLine_NamesTheValueAndTheLocalTime()
        {
            var time = new DateTime(2026, 11, 1, 10, 42, 5, DateTimeKind.Local);

            Assert.AreEqual("[songs] switch on (tab) at 2026-11-01 10:42:05", SongModeSetting.SwitchLogLine(true, time));
            Assert.AreEqual("[songs] switch off (tab) at 2026-11-01 10:42:05", SongModeSetting.SwitchLogLine(false, time));
        }

        [Test]
        public void SwitchLogLine_DoesNotDependOnTheCultureOfTheMachine()
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");   // another calendar and other digits

                Assert.AreEqual("[songs] switch off (tab) at 2026-11-01 09:03:07", SongModeSetting.SwitchLogLine(false, new DateTime(2026, 11, 1, 9, 3, 7)));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        }

        [Test]
        public void Decode_ReadsWhatEncodeWrote_AndAHandEditedFile()
        {
            Assert.AreEqual(true, SongModeSetting.Decode(SongModeSetting.Encode(true)));
            Assert.AreEqual(false, SongModeSetting.Decode(SongModeSetting.Encode(false)));
            Assert.AreEqual(false, SongModeSetting.Decode("\n{\n  \"songMode\" : false\n}\n"), "spaces and new lines are fine");
        }

        [Test]
        public void Decode_Garbage_IsNull()
        {
            foreach (var garbage in new[] { null, "", "   ", "garbage", "{", "{}", "{\"songMode\":1}", "{\"songMode\":\"true\"}", "{\"songMode\":null}", "{\"songmode\":true}", "[true]", "true", "{\"songMode\":true} trailing" })
            {
                Assert.IsNull(SongModeSetting.Decode(garbage), $"'{garbage}' is not a choice");
            }
        }
    }

    public class SongModeStoreTests
    {
        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "downbeat-settings-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Store_SavesAndLoads_AndSurvivesABadFile()
        {
            var path = Path.Combine(_dir, "settings.json");
            var store = new SongModeStore(path);
            Assert.IsNull(store.Load(), "no file yet: no saved choice");

            store.Save(false);
            Assert.AreEqual(false, store.Load());
            Assert.AreEqual("{\"songMode\":false}", File.ReadAllText(path), "the folder was made, the file holds the choice");
            store.Save(true);
            Assert.AreEqual(true, new SongModeStore(path).Load(), "another start of the app reads the same choice");

            File.WriteAllText(path, "this is not json");
            LogAssert.Expect(LogType.Warning, new Regex(@"\[songs\].*not understood"));
            Assert.IsNull(store.Load(), "a bad file is no choice, not an error");
            store.Save(false);
            Assert.AreEqual(false, store.Load(), "and the next save repairs it");
        }

        [Test]
        public void Store_WhenThePathCannotBeWritten_WarnsAndDoesNotThrow()
        {
            Directory.CreateDirectory(_dir);
            var store = new SongModeStore(_dir);   // a folder where the file should be

            LogAssert.Expect(LogType.Warning, new Regex(@"\[songs\].*could not save"));
            Assert.DoesNotThrow(() => store.Save(true));
            Assert.IsNull(store.Load());
        }

        [Test]
        public void PathFor_BadOption_FallsBackWithAWarning()
        {
            var bad = "bad\0path";   // a character that no path has
            var launch = LaunchArgs.Parse(new[] { "--settings-file", bad });
            var fallback = Path.Combine(_dir, "settings.json");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[songs\] --settings-file bad\?path is not a usable path, using " + Regex.Escape(fallback)));
            string path = null;
            Assert.DoesNotThrow(() => path = SongModeStore.PathFor(launch, _dir));

            Assert.AreEqual(fallback, path, "the app starts with the file in the data folder");
            Assert.DoesNotThrow(() => new SongModeStore(path).Save(true), "and that store works");
            Assert.AreEqual(true, new SongModeStore(path).Load());
        }

        [Test]
        public void PathFor_IsTheOptionWhenGiven_ElseSettingsJsonInTheDataFolder()
        {
            var given = Path.Combine(_dir, "mine", "settings.json");

            Assert.AreEqual(Path.GetFullPath(given), SongModeStore.PathFor(LaunchArgs.Parse(new[] { "--settings-file", given }), "data"));
            Assert.AreEqual(Path.Combine("data", "settings.json"), SongModeStore.PathFor(LaunchArgs.Empty, "data"));
            Assert.AreEqual(Path.Combine("data", "settings.json"), SongModeStore.PathFor(LaunchArgs.Parse(new[] { "--settings-file" }), "data"), "a bare option names no file");
        }
    }
}
