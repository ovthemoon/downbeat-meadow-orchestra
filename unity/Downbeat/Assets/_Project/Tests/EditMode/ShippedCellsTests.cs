using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>
    /// The shipped <c>content.json</c> end to end: a hand in each of the six cells conducts the instrument of that cell (and its notes resolve to the recordings of that instrument),
    /// and a fist conducts nothing. The cells, the pose reading, the conduct block and the gaps are the shipped ones, the hand tracker feeds the engine at 30 and at 6 pictures a second.
    /// </summary>
    public class ShippedCellsTests
    {
        const double RunSec = 8.0;
        const int MinNotesPerCell = 6;                  // one note a beat at 96 BPM is about 12 in 8 seconds; the first beats go to the movement window
        const float FastPalmsPerSecond = 2.5f;

        static ConductRig ShippedRig(ContentDto content) =>
            new ConductRig(content.conduct, content.hand, content.pose, content.cells, content.song.partMinGapSlots.Select(g => g.slots).ToArray());

        static PitchMapper ShippedMapper(ContentDto content) => new PitchMapper(content.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps));

        static Part[] AllParts() => Enumerable.Range(0, Parts.Count).Select(p => (Part)p).ToArray();

        [TestCase(1.0 / 30.0)]
        [TestCase(1.0 / 6.0)]
        public void ShippedContent_EachCellPlaysItsInstrument(double pictureEverySec)
        {
            var content = TestContent.LoadShipped();
            var mapper = ShippedMapper(content);

            foreach (var part in AllParts())
            {
                var still = ShippedRig(content);
                var moving = ShippedRig(content);

                still.Run(RunSec, ConductRig.StillIn(part), pictureEverySec);
                moving.Run(RunSec, ConductRig.CircleIn(part, FastPalmsPerSecond), pictureEverySec);

                foreach (var (label, rig) in new[] { ("held still", still), ("moving", moving) })
                {
                    Assert.GreaterOrEqual(rig.Notes.Count, MinNotesPerCell, $"{part} {label}: the hand plays");
                    Assert.That(rig.Notes.Select(n => n.Part), Has.All.EqualTo(part), $"{part} {label}: only the instrument of its own cell sounds");
                    foreach (var note in rig.Notes)
                    {
                        Assert.IsTrue(mapper.TryResolve(note.Part, note.PitchStep, out var sample), $"{part} {label}: step {note.PitchStep} resolves");
                        StringAssert.StartsWith(Parts.Name(part) + "_", sample.SampleId, $"{part} {label}: the note plays a recording of its own instrument");
                    }
                }
            }
        }

        [TestCase(1.0 / 30.0)]
        [TestCase(1.0 / 6.0)]
        public void ShippedContent_AFistInAnyCell_PlaysNothing(double pictureEverySec)
        {
            var content = TestContent.LoadShipped();

            foreach (var part in AllParts())
            {
                var rig = ShippedRig(content);

                rig.Run(RunSec, ConductRig.StillIn(part, 0, HandPose.Fist), pictureEverySec);

                Assert.IsEmpty(rig.Notes, $"{part}: a fist is not a conducting hand");
            }
        }

        [Test]
        public void ShippedContent_TheNewInstruments_ArePlayedFromTheirOwnRecordings()
        {
            var content = TestContent.LoadShipped();
            var mapper = ShippedMapper(content);

            foreach (var part in new[] { Part.Bell, Part.LowString, Part.HandDrum })
            {
                var instrument = mapper.InstrumentFor(part);
                Assert.That(instrument.samples, Is.Not.Empty, $"{part}: samples");
                Assert.That(instrument.samples.Select(s => s.file), Has.All.StartWith("audio/" + Parts.Name(part) + "_"), $"{part}: not the files of another instrument");
                for (var step = 0; step < mapper.PitchSteps; step++) Assert.IsTrue(mapper.TryResolve(part, step, out _), $"{part} step {step}");
            }
        }
    }
}
