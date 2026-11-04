using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>
    /// The content loader against real folders and Unity's own sound decoder: a sound file that passes the header check but that Unity cannot open must make the loader move on to the next copy of
    /// the content (not fail the whole app later), and a copy becomes the last known good only when the app is ready with it.
    /// </summary>
    public class ContentFallbackPlayModeTests
    {
        static string _work;
        static string _exeDir;
        static string _streamingDir;
        static string _persistentDir;

        [SetUp]
        public void SetUp()
        {
            _work = Path.Combine(Path.GetTempPath(), "downbeat-fallback-" + Guid.NewGuid().ToString("N"));
            _exeDir = Path.Combine(_work, "exe");
            _streamingDir = Path.Combine(_work, "streaming");
            _persistentDir = Path.Combine(_work, "persistent");
            Directory.CreateDirectory(_exeDir);
            Directory.CreateDirectory(_streamingDir);
            Directory.CreateDirectory(_persistentDir);
        }

        [TearDown]
        public void TearDown()
        {
            ContentLocator.SetActive(null);
            if (Directory.Exists(_work)) Directory.Delete(_work, true);
        }

        static string RepoContent() => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json")));

        static void CopyShipped(string destination) => new DiskFileProbe().ReplaceDirectory(RepoContent(), destination);

        /// <summary>A WAV with a good header (PCM or float tag, sensible numbers) that Unity's decoder refuses: the float format tag with 16 bits per sample.</summary>
        static void BreakOneSoundForUnity(string folder)
        {
            var path = Path.Combine(folder, "audio", "harp_A4.wav");
            var bytes = File.ReadAllBytes(path);
            bytes[20] = 3; // the format tag at offset 20: 1 = PCM becomes 3 = float, but the file still says 16 bits per sample
            File.WriteAllBytes(path, bytes);
        }

        static ContentSource Resolve(ContentOrigin[] rejected) => ContentLocator.Resolve(_exeDir, _streamingDir, _persistentDir, new DiskFileProbe(), rejected);

        static IEnumerator Load(ContentRepository repository, Action<ContentBundle> onLoaded, Action<string> onFailed) =>
            repository.LoadFirstUsable(Resolve, new DiskFileProbe(), _persistentDir, onLoaded, onFailed);

        [Test]
        public void TheBrokenSoundIsReallyOneThatTheHeaderCheckPasses()
        {
            var folder = Path.Combine(_work, "probe");
            CopyShipped(folder);
            BreakOneSoundForUnity(folder);

            var errors = ContentFolderValidator.Validate(folder, new DiskFileProbe());

            Assert.IsEmpty(errors, "this is the case the trial load exists for: " + string.Join("; ", errors));
        }

        [UnityTest]
        public IEnumerator Load_MovesOnToTheNextCopy_WhenUnityCannotOpenASoundOfTheFirst()
        {
            CopyShipped(Path.Combine(_exeDir, "content"));
            BreakOneSoundForUnity(Path.Combine(_exeDir, "content"));
            CopyShipped(Path.Combine(_persistentDir, "last-known-good"));
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            LogAssert.ignoreFailingMessages = true; // Unity logs its own error for the sound it cannot decode
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(new ContentRepository(), b => bundle = b, e => failure = e);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(bundle, failure);
            StringAssert.StartsWith(ContentLocator.UrlOf(Path.Combine(_persistentDir, "last-known-good")), bundle.BaseUrl, "the last known good copy was used");
            Assert.AreEqual(ContentOrigin.LastKnownGood, ContentLocator.Active.Origin);
            StringAssert.Contains("audio/harp_A4.wav", ContentLocator.Active.Reason);
        }

        [UnityTest]
        public IEnumerator Load_ReachesTheBuiltInCopy_WhenTheSoundsOfTheOtherTwoCannotBeOpened()
        {
            CopyShipped(Path.Combine(_exeDir, "content"));
            BreakOneSoundForUnity(Path.Combine(_exeDir, "content"));
            CopyShipped(Path.Combine(_persistentDir, "last-known-good"));
            BreakOneSoundForUnity(Path.Combine(_persistentDir, "last-known-good"));
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            LogAssert.ignoreFailingMessages = true;
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(new ContentRepository(), b => bundle = b, e => failure = e);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(bundle, failure);
            Assert.AreEqual(ContentOrigin.BuiltIn, ContentLocator.Active.Origin);
        }

        [UnityTest]
        public IEnumerator Load_FailsWithEveryReason_WhenNoCopyCanBeUsed()
        {
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            BreakOneSoundForUnity(Path.Combine(_streamingDir, "default-content"));
            LogAssert.ignoreFailingMessages = true;
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(new ContentRepository(), b => bundle = b, e => failure = e);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(bundle);
            StringAssert.Contains("audio/harp_A4.wav", failure);
            StringAssert.Contains("external content: folder not found", failure);
        }

        [UnityTest]
        public IEnumerator TheLastKnownGoodCopy_IsMadeOnlyWhenTheAppConfirmsItIsReady()
        {
            CopyShipped(Path.Combine(_exeDir, "content"));
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            var repository = new ContentRepository();
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(repository, b => bundle = b, e => failure = e);

            Assert.IsNotNull(bundle, failure);
            Assert.AreEqual(ContentOrigin.External, ContentLocator.Active.Origin);
            Assert.IsFalse(Directory.Exists(Path.Combine(_persistentDir, "last-known-good")), "loading the json and the pictures proves nothing yet: the sounds still have to load");

            repository.ConfirmLoaded();

            Assert.IsTrue(File.Exists(Path.Combine(_persistentDir, "last-known-good", "content.json")));
        }

        [UnityTest]
        public IEnumerator ConfirmLoaded_KeepsNothing_ForContentThatIsNotExternal()
        {
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            var repository = new ContentRepository();
            ContentBundle bundle = null;

            yield return Load(repository, b => bundle = b, e => { });
            repository.ConfirmLoaded();

            Assert.IsNotNull(bundle);
            Assert.AreEqual(ContentOrigin.BuiltIn, ContentLocator.Active.Origin);
            Assert.IsFalse(Directory.Exists(Path.Combine(_persistentDir, "last-known-good")));
        }
    }
}
