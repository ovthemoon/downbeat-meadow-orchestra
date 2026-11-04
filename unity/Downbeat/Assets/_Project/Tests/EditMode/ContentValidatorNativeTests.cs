using System.IO;
using System.Linq;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The five ways a content folder can be bad (design chapter 7): broken JSON, a file that is not there, a number out of range, a repeated id, a damaged picture or sound.</summary>
    public class ContentValidatorNativeTests
    {
        const string Root = "/app/content";

        static FakeFileProbe Probe() => new FakeFileProbe().AddShippedContent(Root);

        static string File_(string relative) => FakeFileProbe.Join(Root, relative);

        static void ReplaceJson(FakeFileProbe probe, System.Action<ContentDto> change)
        {
            var dto = JsonUtility.FromJson<ContentDto>(probe.ReadAllText(File_("content.json")));
            change(dto);
            probe.Put(File_("content.json"), JsonUtility.ToJson(dto));
        }

        static System.Collections.Generic.IReadOnlyList<string> Validate(FakeFileProbe probe) => ContentFolderValidator.Validate(Root, probe);

        [Test]
        public void Validator_AcceptsShippedContent()
        {
            var errors = Validate(Probe());

            Assert.IsEmpty(errors, string.Join("; ", errors));
        }

        [Test]
        public void Validator_AcceptsShippedContent_OnTheRealDisk()
        {
            var errors = ContentFolderValidator.Validate(Path.GetDirectoryName(TestContent.RepoContentPath), new DiskFileProbe());

            Assert.IsEmpty(errors, string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsBrokenJson()
        {
            var probe = Probe().Put(File_("content.json"), "{ 일부러 망가뜨린 파일");

            var errors = Validate(probe);

            Assert.AreEqual(1, errors.Count, string.Join("; ", errors));
            StringAssert.Contains("content.json is not valid JSON", errors[0]);
        }

        [Test]
        public void Validator_RejectsAnEmptyContentJson_AndAMissingOne()
        {
            StringAssert.Contains("empty", Validate(Probe().Put(File_("content.json"), "  \n"))[0]);
            StringAssert.Contains("content.json is missing", Validate(Probe().Remove(File_("content.json")))[0]);
        }

        [Test]
        public void Validator_RejectsAFileThatIsNotThere()
        {
            var errors = Validate(Probe().Remove(File_("audio/harp_A4.wav")));

            Assert.IsTrue(errors.Any(e => e.Contains("audio/harp_A4.wav") && e.Contains("is missing")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsAMissingBackdropPlate()
        {
            var probe = Probe();
            var plate = JsonUtility.FromJson<ContentDto>(probe.ReadAllText(File_("content.json"))).meadow.sky.stops[0].backdrop;

            var errors = Validate(probe.Remove(File_(plate)));

            Assert.IsTrue(errors.Any(e => e.Contains(plate) && e.Contains("is missing")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsANumberOutOfRange()
        {
            var probe = Probe();
            ReplaceJson(probe, dto => dto.song.bpm = 500f);

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains("song.bpm")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsARepeatedId()
        {
            var probe = Probe();
            ReplaceJson(probe, dto => dto.instruments[1].id = dto.instruments[0].id);

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains("duplicated")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsADamagedWav()
        {
            var probe = Probe();
            var whole = probe.Bytes(File_("audio/harp_A4.wav"));
            probe.Put(File_("audio/harp_A4.wav"), whole.Take(whole.Length / 3).ToArray());

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains("audio/harp_A4.wav") && e.Contains("cut short")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsADamagedImage()
        {
            var probe = Probe();
            var plate = JsonUtility.FromJson<ContentDto>(probe.ReadAllText(File_("content.json"))).meadow.sky.stops[1].backdrop;
            var whole = probe.Bytes(File_(plate));
            probe.Put(File_(plate), whole.Take(whole.Length / 2).ToArray());

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains(plate) && e.Contains("cut short")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_RejectsAMissingTutorialPicture_OfEitherHand()
        {
            foreach (var picture in new[] { "tutorial/hand-open.png", "tutorial/hand-fist.png" })
            {
                var errors = Validate(Probe().Remove(File_(picture)));

                Assert.IsTrue(errors.Any(e => e.Contains(picture) && e.Contains("is missing")), $"{picture}: {string.Join("; ", errors)}");
            }
        }

        [Test]
        public void Validator_RejectsADamagedTutorialPicture()
        {
            var probe = Probe();
            var whole = probe.Bytes(File_("tutorial/hand-open.png"));
            probe.Put(File_("tutorial/hand-open.png"), whole.Take(whole.Length - 12).ToArray());   // without the end chunk of a PNG

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains("tutorial/hand-open.png")), string.Join("; ", errors));
        }

        [Test]
        public void Validator_AcceptsAnEmptyPicturePath_WhichMeansTheHandIsDrawnInCode()
        {
            var probe = Probe().Remove(File_("tutorial/hand-open.png")).Remove(File_("tutorial/hand-fist.png"));
            ReplaceJson(probe, dto => { dto.tutorial.openHandImage = string.Empty; dto.tutorial.fistImage = string.Empty; });

            var errors = Validate(probe);

            Assert.IsEmpty(errors, string.Join("; ", errors));
        }

        [Test]
        public void Validator_DoesNotLookForAPictureOutsideTheContentFolder()
        {
            var probe = Probe();
            ReplaceJson(probe, dto => dto.tutorial.fistImage = "../outside.png");

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains("tutorial.fistImage")), string.Join("; ", errors));
            Assert.IsFalse(errors.Any(e => e.Contains("is missing")), "a path like that is reported, not looked for: " + string.Join("; ", errors));
        }

        [Test]
        public void Validator_ReportsEveryProblem_NotJustTheFirst()
        {
            var probe = Probe().Remove(File_("audio/harp_A4.wav")).Remove(File_("audio/pad_A3.wav"));

            var errors = Validate(probe);

            Assert.IsTrue(errors.Any(e => e.Contains("harp_A4.wav")));
            Assert.IsTrue(errors.Any(e => e.Contains("pad_A3.wav")));
        }
    }
}
