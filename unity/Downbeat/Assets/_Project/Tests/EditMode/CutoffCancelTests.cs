using Downbeat.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Downbeat.Tests
{
    /// <summary>
    /// When the piece is ended the sound of the notes that were scheduled but are not heard yet is cancelled; what those notes asked of the picture is taken back too (flowers, wind, petals and star dust,
    /// the mark on the score, the flash), and a note that is heard before the end is not touched. The stage as a whole is in CutoffVisualsPlayModeTests.
    /// </summary>
    public class CutoffCancelTests
    {
        static WindWave Wave(double start) => new WindWave(0f, 0f, start, 1f, 1f, 2f, WindWaveKind.Radial, 0f);

        [Test]
        public void Wind_RemoveStartingAfter_TakesBackTheWavesThatHaveNotStarted()
        {
            var wind = new WindField(8, 1f);
            wind.Add(Wave(1.0));
            wind.Add(Wave(2.0));
            wind.Add(Wave(3.0));
            wind.Add(Wave(3.5));

            Assert.AreEqual(2, wind.CountStartingAfter(2.5));
            var removed = wind.RemoveStartingAfter(2.5);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(2, wind.Count);
            Assert.AreEqual(0, wind.CountStartingAfter(2.5));
            var copy = new WindWaveGpu[8];
            Assert.AreEqual(2, wind.CopyActive(2.5, copy), "the two that had started are still there");
            Assert.AreEqual(0, wind.RemoveStartingAfter(2.5), "and taking back again takes back nothing");
        }

        [Test]
        public void Wind_RemoveStartingAfter_ALaterWaveCanTakeTheSlot()
        {
            var wind = new WindField(2, 1f);
            wind.Add(Wave(5.0));
            wind.Add(Wave(6.0));
            wind.RemoveStartingAfter(4.0);

            wind.Add(Wave(1.0));

            Assert.AreEqual(1, wind.Count);
        }

        [Test]
        public void Score_RemoveMarksAfter_TakesBackTheNewestNotesNotHeardYet()
        {
            var score = new ScoreModel(8);
            for (var i = 0; i < 5; i++) score.Add(new ScoreMark(i * 0.5, Part.Harp, 3, 0.8f));   // 0, 0.5, 1, 1.5, 2

            score.RemoveMarksAfter(1.2);

            Assert.AreEqual(3, score.Count);
            var shown = new List<ScoreMark>();
            score.CopyVisible(10.0, 20.0, shown);
            Assert.AreEqual(3, shown.Count);
            Assert.AreEqual(1.0, shown[2].Time, 1e-9);
        }

        [Test]
        public void Score_RemoveMarksAfter_WorksOnARingThatHasWrapped()
        {
            var score = new ScoreModel(4);
            for (var i = 0; i < 7; i++) score.Add(new ScoreMark(i, Part.Harp, 3, 0.8f));   // the ring holds 3, 4, 5, 6

            score.RemoveMarksAfter(4.5);

            Assert.AreEqual(2, score.Count);
            var shown = new List<ScoreMark>();
            score.CopyVisible(10.0, 20.0, shown);
            Assert.AreEqual(new[] { 3.0, 4.0 }, new[] { shown[0].Time, shown[1].Time });
            score.RemoveMarksAfter(100.0);
            Assert.AreEqual(2, score.Count, "nothing is after that");
        }

        [Test]
        public void VisualQueue_RemoveAfter_DropsTheFlashesOfNotesThatAreNotHeard()
        {
            var queue = new NoteVisualQueue();
            queue.Enqueue(new ScheduledNote(1, 1.0, 0.9, Part.Marimba, 3, 0.8f, false, 0.5f, 0.75f));
            queue.Enqueue(new ScheduledNote(2, 2.0, 1.9, Part.Marimba, 3, 0.8f, false, 0.5f, 0.75f));
            queue.Enqueue(new ScheduledNote(3, 3.0, 2.9, Part.Marimba, 3, 0.8f, false, 0.5f, 0.75f));

            var removed = queue.RemoveAfter(1.5);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(1, queue.Count);
            var due = new List<ScheduledNote>();
            queue.DrainDue(10.0, 0.0, due);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(1.0, due[0].AudioTime, 1e-9);
        }

        [Test]
        public void BloomRun_Earlier_IsWhatTheFlowerDidBeforeANoteThatWasCancelled()
        {
            var settings = new BloomSettings(0.05f, 0.35f, 3f, 4f, 0.15f);
            var first = BloomRun.First(10.0, 0f);
            var again = first.Retrigger(10.4, settings);

            Assert.IsFalse(first.HasEarlier);
            Assert.IsTrue(again.HasEarlier);
            var back = again.Earlier();
            Assert.AreEqual(10.0, back.AudibleTime, 1e-9);
            Assert.IsFalse(back.HasEarlier);
            Assert.AreEqual(first.Value(10.3, settings), back.Value(10.3, settings), 1e-6f, "it goes on as if the second note had never been asked for");
        }
    }
}
