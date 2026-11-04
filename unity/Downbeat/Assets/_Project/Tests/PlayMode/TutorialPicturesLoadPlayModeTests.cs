using System;
using System.Collections;
using System.IO;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>
    /// The pictures of the tutorial's hands go through the content loader like the other pictures of the content, in whichever copy of the content is used (next to the program, the last known good one, the built-in one).
    /// They are nice to have: one that cannot be loaded costs a warning and that hand is drawn in code, it never makes a copy of the content unusable. A folder that names a picture that is not there or is damaged is
    /// passed over by the folder checks like any other bad folder.
    /// </summary>
    public class TutorialPicturesLoadPlayModeTests
    {
        const string Open = "tutorial/hand-open.png";
        const string Fist = "tutorial/hand-fist.png";

        static string _work;
        static string _exeDir;
        static string _streamingDir;
        static string _persistentDir;

        [SetUp]
        public void SetUp()
        {
            _work = Path.Combine(Path.GetTempPath(), "downbeat-tutorial-pictures-" + Guid.NewGuid().ToString("N"));
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

        static string In(string folder, string relative) => Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>A PNG that passes the check of the header and of the end chunk but whose picture Unity cannot decode: the middle of the file is nothing.</summary>
        static void HollowOut(string folder, string relative)
        {
            var path = In(folder, relative);
            var bytes = File.ReadAllBytes(path);
            for (var i = 64; i < bytes.Length - 16; i++) bytes[i] = 0;
            File.WriteAllBytes(path, bytes);
        }

        static ContentSource Resolve(ContentOrigin[] rejected) => ContentLocator.Resolve(_exeDir, _streamingDir, _persistentDir, new DiskFileProbe(), rejected);

        static IEnumerator Load(ContentRepository repository, Action<ContentBundle> onLoaded, Action<string> onFailed) =>
            repository.LoadFirstUsable(Resolve, new DiskFileProbe(), _persistentDir, onLoaded, onFailed);

        [UnityTest]
        public IEnumerator TheContent_LoadsBothPictures_WithTheOtherImagesOfTheContent()
        {
            CopyShipped(Path.Combine(_exeDir, "content"));
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(new ContentRepository(), b => bundle = b, e => failure = e);

            Assert.IsNotNull(bundle, failure);
            Assert.IsTrue(bundle.TryTexture(Open, out var open));
            Assert.IsTrue(bundle.TryTexture(Fist, out var fist));
            Assert.AreEqual(512, open.width);
            Assert.AreEqual(512, fist.height);
            Assert.IsTrue(open.format == TextureFormat.RGBA32 || open.format == TextureFormat.ARGB32, "with an alpha channel: " + open.format);
            Assert.IsTrue(bundle.TryTexture(bundle.Dto.meadow.sky.stops[0].backdrop, out _), "the backdrop plates come the same way");
            Assert.IsFalse(bundle.TryTexture("tutorial/nothing.png", out _));
            Assert.IsFalse(bundle.TryTexture(string.Empty, out _));
            Assert.IsFalse(bundle.TryTexture(null, out _));
        }

        [UnityTest]
        public IEnumerator APictureThatIsNotThere_CostsAWarning_NotTheCopy()
        {
            // a copy that the folder checks have not looked at (they would have passed it over): the loader itself must not give up on it
            var folder = Path.Combine(_work, "no-fist");
            CopyShipped(folder);
            File.Delete(In(folder, Fist));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[content\] the tutorial picture tutorial/hand-fist\.png could not be loaded"));
            ContentBundle bundle = null;
            string failure = null;

            yield return new ContentRepository().LoadFirstUsable(rejected => new ContentSource(folder, ContentOrigin.External, string.Empty), new DiskFileProbe(), _persistentDir, b => bundle = b, e => failure = e);

            Assert.IsNotNull(bundle, "the content is used: " + failure);
            Assert.IsTrue(bundle.TryTexture(Open, out _), "the other hand is there");
            Assert.IsFalse(bundle.TryTexture(Fist, out _), "this one is left out, the tutorial draws it in code");
        }

        [UnityTest]
        public IEnumerator APictureUnityCannotDecode_CostsAWarning_AndIsNotTheQuestionMarkOfUnity()
        {
            var folder = Path.Combine(_work, "hollow");
            CopyShipped(folder);
            HollowOut(folder, Open);
            Assert.IsEmpty(ContentFolderValidator.Validate(folder, new DiskFileProbe()), "the folder checks pass it: this is what the loader has to catch");
            LogAssert.ignoreFailingMessages = true;   // Unity logs its own error for a picture it cannot decode
            ContentBundle bundle = null;
            string failure = null;

            yield return new ContentRepository().LoadFirstUsable(rejected => new ContentSource(folder, ContentOrigin.External, string.Empty), new DiskFileProbe(), _persistentDir, b => bundle = b, e => failure = e);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(bundle, "the content is used: " + failure);
            Assert.IsFalse(bundle.TryTexture(Open, out var open), "a picture that cannot be decoded is not handed out as the small placeholder of Unity" + (open != null ? $" ({open.width} x {open.height})" : string.Empty));
            Assert.IsTrue(bundle.TryTexture(Fist, out _));
        }

        [UnityTest]
        public IEnumerator AFolderWithoutTheirPictures_IsPassedOver_ForTheLastKnownGoodCopy()
        {
            CopyShipped(Path.Combine(_exeDir, "content"));
            File.Delete(In(Path.Combine(_exeDir, "content"), Open));
            CopyShipped(Path.Combine(_persistentDir, "last-known-good"));
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(new ContentRepository(), b => bundle = b, e => failure = e);

            Assert.IsNotNull(bundle, failure);
            Assert.AreEqual(ContentOrigin.LastKnownGood, ContentLocator.Active.Origin, "the folder next to the program names a picture that is not there");
            StringAssert.Contains(Open, ContentLocator.Active.Reason);
            Assert.IsTrue(bundle.TryTexture(Open, out _), "and the copy that is used has the pictures");
        }

        [UnityTest]
        public IEnumerator TheBuiltInCopy_HasThePicturesToo()
        {
            CopyShipped(Path.Combine(_streamingDir, "default-content"));
            ContentBundle bundle = null;
            string failure = null;

            yield return Load(new ContentRepository(), b => bundle = b, e => failure = e);

            Assert.IsNotNull(bundle, failure);
            Assert.AreEqual(ContentOrigin.BuiltIn, ContentLocator.Active.Origin);
            Assert.IsTrue(bundle.TryTexture(Open, out _));
            Assert.IsTrue(bundle.TryTexture(Fist, out _));
        }

        [UnityTest]
        public IEnumerator AnEmptyPath_LoadsNothing_AndSaysNothing()
        {
            var folder = Path.Combine(_work, "drawn-in-code");
            CopyShipped(folder);
            File.Delete(In(folder, Open));
            File.Delete(In(folder, Fist));
            var json = File.ReadAllText(In(folder, "content.json"));
            json = json.Replace("\"openHandImage\": \"tutorial/hand-open.png\"", "\"openHandImage\": \"\"").Replace("\"fistImage\": \"tutorial/hand-fist.png\"", "\"fistImage\": \"\"");
            File.WriteAllText(In(folder, "content.json"), json);
            ContentBundle bundle = null;
            string failure = null;

            yield return new ContentRepository().LoadFirstUsable(rejected => new ContentSource(folder, ContentOrigin.External, string.Empty), new DiskFileProbe(), _persistentDir, b => bundle = b, e => failure = e);

            Assert.IsNotNull(bundle, failure);
            Assert.IsEmpty(ContentFolderValidator.Validate(folder, new DiskFileProbe()));
            Assert.IsFalse(bundle.TryTexture(Open, out _));
            Assert.AreEqual(string.Empty, bundle.Dto.tutorial.openHandImage);
        }
    }
}
