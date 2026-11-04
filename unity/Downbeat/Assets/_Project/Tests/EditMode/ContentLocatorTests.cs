using System;
using System.IO;
using Downbeat.Content;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class ContentLocatorTests
    {
        const string ExeDir = "/exhibit";
        const string StreamingDir = "/exhibit/Downbeat_Data/StreamingAssets";
        const string PersistentDir = "/user/appdata";
        const string External = "/exhibit/content";
        const string LastKnownGood = "/user/appdata/last-known-good";
        const string BuiltIn = "/exhibit/Downbeat_Data/StreamingAssets/default-content";

        static ContentSource Resolve(FakeFileProbe probe, params ContentOrigin[] rejected) => ContentLocator.Resolve(ExeDir, StreamingDir, PersistentDir, probe, rejected);

        static void BreakJson(FakeFileProbe probe, string folder) => probe.Put(FakeFileProbe.Join(folder, "content.json"), "{ broken");

        [Test]
        public void Resolve_UsesExternal_WhenValid()
        {
            var probe = new FakeFileProbe().AddShippedContent(External).AddShippedContent(LastKnownGood).AddShippedContent(BuiltIn);

            var source = Resolve(probe);

            Assert.AreEqual(ContentOrigin.External, source.Origin);
            Assert.AreEqual(External, source.RootDir);
            Assert.AreEqual(string.Empty, source.Reason, "the first choice needs no explanation");
        }

        [Test]
        public void Resolve_FallsBackToLastKnownGood_WhenExternalBroken()
        {
            var probe = new FakeFileProbe().AddShippedContent(External).AddShippedContent(LastKnownGood).AddShippedContent(BuiltIn);
            BreakJson(probe, External);

            var source = Resolve(probe);

            Assert.AreEqual(ContentOrigin.LastKnownGood, source.Origin);
            Assert.AreEqual(LastKnownGood, source.RootDir);
            StringAssert.Contains(External, source.Reason);
            StringAssert.Contains("content.json is not valid JSON", source.Reason);
        }

        [Test]
        public void Resolve_UsesBuiltIn_WhenNothingElse()
        {
            var probe = new FakeFileProbe().AddShippedContent(BuiltIn);

            var source = Resolve(probe);

            Assert.AreEqual(ContentOrigin.BuiltIn, source.Origin);
            Assert.AreEqual(BuiltIn, source.RootDir);
            StringAssert.Contains("external content: folder not found", source.Reason, "a new PC has no content folder next to the program: say so");
            StringAssert.Contains("last known good: folder not found", source.Reason);
        }

        [Test]
        public void Resolve_UsesBuiltIn_WhenExternalAndLastKnownGoodAreBroken()
        {
            var probe = new FakeFileProbe().AddShippedContent(External).AddShippedContent(LastKnownGood).AddShippedContent(BuiltIn);
            BreakJson(probe, External);
            probe.Remove(FakeFileProbe.Join(LastKnownGood, "audio/harp_A4.wav"));

            var source = Resolve(probe);

            Assert.AreEqual(ContentOrigin.BuiltIn, source.Origin);
            StringAssert.Contains("external content rejected", source.Reason);
            StringAssert.Contains("last known good rejected", source.Reason);
            StringAssert.Contains("audio/harp_A4.wav", source.Reason);
        }

        [Test]
        public void Resolve_SkipsAnOriginThatFailedToLoad()
        {
            var probe = new FakeFileProbe().AddShippedContent(External).AddShippedContent(LastKnownGood).AddShippedContent(BuiltIn);

            var source = Resolve(probe, ContentOrigin.External);

            Assert.AreEqual(ContentOrigin.LastKnownGood, source.Origin);
            StringAssert.Contains("external content: could not be loaded", source.Reason);
        }

        [Test]
        public void Resolve_StillReturnsBuiltIn_WhenEverythingIsBroken_SoTheAppCanSayWhy()
        {
            var probe = new FakeFileProbe();

            var source = Resolve(probe);

            Assert.AreEqual(ContentOrigin.BuiltIn, source.Origin);
            StringAssert.Contains("built-in content is also unusable", source.Reason);
        }

        [Test]
        public void Promote_CopiesAnExternalFolderToLastKnownGood()
        {
            var probe = new FakeFileProbe().AddShippedContent(External).AddShippedContent(LastKnownGood);
            probe.Put(FakeFileProbe.Join(LastKnownGood, "old-file.txt"), "left over from an older content");

            var done = ContentLocator.Promote(new ContentSource(External, ContentOrigin.External, string.Empty), PersistentDir, probe);

            Assert.IsTrue(done);
            Assert.AreEqual(1, probe.Replacements.Count);
            Assert.AreEqual(External + " -> " + LastKnownGood, probe.Replacements[0]);
            Assert.IsFalse(probe.Has(FakeFileProbe.Join(LastKnownGood, "old-file.txt")), "the old copy is replaced, not mixed with the new one");
            Assert.IsTrue(probe.Has(FakeFileProbe.Join(LastKnownGood, "content.json")));
        }

        [Test]
        public void Promote_DoesNothing_ForContentThatIsNotExternal()
        {
            var probe = new FakeFileProbe().AddShippedContent(LastKnownGood).AddShippedContent(BuiltIn);

            Assert.IsFalse(ContentLocator.Promote(new ContentSource(LastKnownGood, ContentOrigin.LastKnownGood, "x"), PersistentDir, probe));
            Assert.IsFalse(ContentLocator.Promote(new ContentSource(BuiltIn, ContentOrigin.BuiltIn, "x"), PersistentDir, probe));
            Assert.AreEqual(0, probe.Replacements.Count);
        }

        [Test]
        public void Promote_DoesNotThrow_WhenTheCopyFails_AndKeepsTheOldCopy()
        {
            var probe = new FakeFileProbe().AddShippedContent(External).AddShippedContent(LastKnownGood);
            probe.FailReplaceWith = new IOException("disk full");

            var done = ContentLocator.Promote(new ContentSource(External, ContentOrigin.External, string.Empty), PersistentDir, probe);

            Assert.IsFalse(done);
            Assert.IsTrue(probe.Has(FakeFileProbe.Join(LastKnownGood, "content.json")));
        }

        [Test]
        public void DiskFileProbe_ReplaceDirectory_CopiesEverythingAndRemovesWhatWasThere()
        {
            var work = Path.Combine(Path.GetTempPath(), "downbeat-locator-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = Path.Combine(work, "src");
                var destination = Path.Combine(work, "dst");
                Directory.CreateDirectory(Path.Combine(source, "sub"));
                File.WriteAllText(Path.Combine(source, "a.txt"), "a");
                File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "b");
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "stale.txt"), "stale");

                new DiskFileProbe().ReplaceDirectory(source, destination);

                Assert.AreEqual("a", File.ReadAllText(Path.Combine(destination, "a.txt")));
                Assert.AreEqual("b", File.ReadAllText(Path.Combine(destination, "sub", "b.txt")));
                Assert.IsFalse(File.Exists(Path.Combine(destination, "stale.txt")));
                Assert.IsFalse(Directory.Exists(destination + ".new"), "no temporary folder is left behind");
                Assert.IsFalse(Directory.Exists(destination + ".old"), "and the old copy that was set aside is gone");
            }
            finally
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
        }

        static string TempFolder() => Path.Combine(Path.GetTempPath(), "downbeat-locator-" + Guid.NewGuid().ToString("N"));

        static void WriteFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        [Test]
        public void DiskFileProbe_ReplaceDirectory_ClearsTheReadOnlyMarkOfTheCopies()
        {
            var work = TempFolder();
            try
            {
                var source = Path.Combine(work, "src");
                var destination = Path.Combine(work, "dst");
                WriteFile(Path.Combine(source, "locked-by-the-operator.txt"), "x");
                File.SetAttributes(Path.Combine(source, "locked-by-the-operator.txt"), FileAttributes.ReadOnly);

                new DiskFileProbe().ReplaceDirectory(source, destination);
                new DiskFileProbe().ReplaceDirectory(source, destination);   // a second time: the first copy must be deletable

                var copy = Path.Combine(destination, "locked-by-the-operator.txt");
                Assert.IsTrue(File.Exists(copy));
                Assert.AreEqual(0, (int)(File.GetAttributes(copy) & FileAttributes.ReadOnly), "a read-only file in the content folder must not make the copy undeletable for ever");
            }
            finally
            {
                if (Directory.Exists(work)) ClearReadOnlyAndDelete(work);
            }
        }

        static void ClearReadOnlyAndDelete(string folder)
        {
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }

        [Test]
        public void DiskFileProbe_ReplaceDirectory_CleansUpLeftoversOfAnEarlierReplaceThatDidNotFinish()
        {
            var work = TempFolder();
            try
            {
                var source = Path.Combine(work, "src");
                var destination = Path.Combine(work, "dst");
                WriteFile(Path.Combine(source, "a.txt"), "new");
                WriteFile(Path.Combine(destination, "a.txt"), "current");
                WriteFile(Path.Combine(destination + ".new", "half.txt"), "half");   // a half-made copy
                WriteFile(Path.Combine(destination + ".old", "older.txt"), "older");

                new DiskFileProbe().ReplaceDirectory(source, destination);

                Assert.AreEqual("new", File.ReadAllText(Path.Combine(destination, "a.txt")));
                Assert.IsFalse(Directory.Exists(destination + ".new"));
                Assert.IsFalse(Directory.Exists(destination + ".old"));
            }
            finally
            {
                if (Directory.Exists(work)) ClearReadOnlyAndDelete(work);
            }
        }

        [Test]
        public void DiskFileProbe_ReplaceDirectory_PutsTheOldCopyBack_WhenTheNewOneCannotBeMovedIn()
        {
            var work = TempFolder();
            try
            {
                var source = Path.Combine(work, "src");
                var destination = Path.Combine(work, "dst");
                WriteFile(Path.Combine(source, "a.txt"), "new");
                WriteFile(Path.Combine(destination, "a.txt"), "current");
                var probe = new DiskFileProbe(step => { if (step == DiskFileProbe.StepOldSetAside) throw new IOException("a locked file"); });

                Assert.Throws<IOException>(() => probe.ReplaceDirectory(source, destination));

                Assert.AreEqual("current", File.ReadAllText(Path.Combine(destination, "a.txt")), "the last known good copy is still there, and whole");
                Assert.IsFalse(Directory.Exists(destination + ".new"));
                Assert.IsFalse(Directory.Exists(destination + ".old"));
            }
            finally
            {
                if (Directory.Exists(work)) ClearReadOnlyAndDelete(work);
            }
        }

        [Test]
        public void DiskFileProbe_Recover_RestoresTheOldCopy_WhenThePowerWentBetweenTheTwoMoves()
        {
            var work = TempFolder();
            try
            {
                var destination = Path.Combine(work, "dst");
                WriteFile(Path.Combine(destination + ".old", "a.txt"), "last good");
                WriteFile(Path.Combine(destination + ".new", "a.txt"), "never finished");

                new DiskFileProbe().RecoverInterruptedReplace(destination);

                Assert.AreEqual("last good", File.ReadAllText(Path.Combine(destination, "a.txt")));
                Assert.IsFalse(Directory.Exists(destination + ".old"));
                Assert.IsFalse(Directory.Exists(destination + ".new"), "an unfinished copy is never used");
            }
            finally
            {
                if (Directory.Exists(work)) ClearReadOnlyAndDelete(work);
            }
        }

        [Test]
        public void DiskFileProbe_Recover_DropsTheOldCopy_WhenTheNewOneIsAlreadyInPlace()
        {
            var work = TempFolder();
            try
            {
                var destination = Path.Combine(work, "dst");
                WriteFile(Path.Combine(destination, "a.txt"), "new");
                WriteFile(Path.Combine(destination + ".old", "a.txt"), "old");

                new DiskFileProbe().RecoverInterruptedReplace(destination);

                Assert.AreEqual("new", File.ReadAllText(Path.Combine(destination, "a.txt")));
                Assert.IsFalse(Directory.Exists(destination + ".old"));
            }
            finally
            {
                if (Directory.Exists(work)) ClearReadOnlyAndDelete(work);
            }
        }

        [Test]
        public void Resolve_UsesALastKnownGoodThatWasLeftAsOld_AfterAPowerLossBetweenTheMoves()
        {
            var probe = new FakeFileProbe().AddShippedContent(LastKnownGood + ".old").AddShippedContent(BuiltIn);

            var source = Resolve(probe);

            Assert.AreEqual(ContentOrigin.LastKnownGood, source.Origin, "the old copy was set aside but never replaced: it is the copy");
            Assert.AreEqual(LastKnownGood, source.RootDir);
        }

        [Test]
        public void DiskFileProbe_ReplaceDirectory_LeavesTheOldCopyWhenTheSourceIsMissing()
        {
            var work = Path.Combine(Path.GetTempPath(), "downbeat-locator-" + Guid.NewGuid().ToString("N"));
            try
            {
                var destination = Path.Combine(work, "dst");
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");

                Assert.Throws<DirectoryNotFoundException>(() => new DiskFileProbe().ReplaceDirectory(Path.Combine(work, "nowhere"), destination));

                Assert.AreEqual("keep", File.ReadAllText(Path.Combine(destination, "keep.txt")));
            }
            finally
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
        }
    }
}
