using System;
using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The note trail behind a hand: where it is drawn (smoothed between tracker frames), how many notes are born, and when a hand has no trail.</summary>
    public class NoteTrailTests
    {
        const double RenderFps = 60.0;
        const double RenderDt = 1.0 / RenderFps;
        // A hand crossing the screen at 0.4 of the width a second; the tracker sees it 6 times a second, the picture is drawn 60 times a second.
        const float HandSpeed = 0.4f;
        const float FastHand = 2f;   // picture widths a second: the most notes
        const float StartX = 0.1f;
        const float HandY = 0.5f;

        static TrailSettings Defaults => TrailSettings.From(null);

        static TrailHandFrame Frame(float x, float y, double captureMs, bool isFist = false, bool visible = true, int tag = 0) =>
            new TrailHandFrame(visible, isFist, x, y, captureMs, handId: 1, epoch: 0, tag: tag);

        /// <summary>Runs one hand for <paramref name="seconds"/> and calls <paramref name="onFrame"/> after every drawn frame with the time and the place of the hand as the tracker last saw it.</summary>
        static void Run(HandNoteTrail trail, double seconds, double trackerFps, Func<double, (float x, float y)> path, Func<double, bool> isFist, Action<double, (float x, float y)> onFrame)
        {
            var trackerStep = 1.0 / trackerFps;
            var nextCapture = 0.0;
            var seen = path(0.0);
            var captureMs = 0.0;
            for (var frame = 0; frame * RenderDt <= seconds + 1e-9; frame++)
            {
                var now = frame * RenderDt;
                if (now >= nextCapture - 1e-9)
                {
                    seen = path(nextCapture);
                    captureMs = nextCapture * 1000.0;
                    nextCapture += trackerStep;
                }
                trail.Step(now, Frame(seen.x, seen.y, captureMs, isFist(now)));
                onFrame(now, seen);
            }
        }

        static void Run(HandNoteTrail trail, double seconds, double trackerFps, Func<double, (float x, float y)> path, Func<double, bool> isFist) =>
            Run(trail, seconds, trackerFps, path, isFist, (now, seen) => { });

        /// <summary>A hand that goes straight on to the right at <paramref name="speed"/> picture widths a second (0 = held still).</summary>
        static Func<double, (float x, float y)> Crossing(float speed) => t => (StartX + speed * (float)t, HandY);

        [Test]
        public void Trail_FollowsTheHand_SmoothlyAt6Fps()
        {
            // Arrange
            var trail = new HandNoteTrail(Defaults, seed: 3);
            var places = new List<float>();
            var trackerJump = HandSpeed / 6f;

            // Act: 2 seconds of a hand that the tracker reports six times a second
            Run(trail, 2.0, 6.0, Crossing(HandSpeed), _ => false, (now, seen) => places.Add(trail.Place.X));

            // Assert: the drawn place glides: after the first second no drawn frame moves it by more than a quarter of what one tracker frame moves the hand,
            // and it never stands still (a hand that moves 0.0067 a frame is drawn moving every frame)
            var biggestStep = 0f;
            var smallestStep = float.MaxValue;
            for (var i = 61; i < places.Count; i++)
            {
                var step = places[i] - places[i - 1];
                biggestStep = Math.Max(biggestStep, step);
                smallestStep = Math.Min(smallestStep, step);
            }
            Assert.Less(biggestStep, 0.25f * trackerJump, $"the biggest drawn step {biggestStep} against a tracker jump of {trackerJump}");
            Assert.Greater(smallestStep, 0.2f * (HandSpeed / (float)RenderFps), $"the smallest drawn step {smallestStep}");
            var handNow = StartX + HandSpeed * 2.0f;
            Assert.Less(Math.Abs(handNow - places[places.Count - 1]), 0.2f, "and it keeps up with the hand");
        }

        [Test]
        public void Trail_AtSixPicturesASecond_StillLeavesNotes_FromTheMovementOfThePlace()
        {
            // the tracker's own speed of a hand (HandMotion.SpeedPalms) is measured over 70 ms and is 0 for pictures 167 ms apart: the trail reads the movement of the place instead
            var trail = new HandNoteTrail(Defaults, seed: 3);

            Run(trail, 2.0, 6.0, Crossing(0.6f), _ => false);

            Assert.GreaterOrEqual(trail.Notes.Count, 3, "a hand that crosses the picture in two seconds has its notes at 6 pictures a second");
            Assert.Greater(trail.Movement01, 0.4f);
        }

        [Test]
        public void Trail_FollowsTheHand_TheNotesItBornAreSpreadAlongThePath_NotTeleported()
        {
            var trail = new HandNoteTrail(Defaults, seed: 3);

            Run(trail, 1.6, 6.0, Crossing(0.6f), _ => false);

            var notes = trail.Notes;
            Assert.GreaterOrEqual(notes.Count, 4);
            for (var i = 1; i < notes.Count; i++)
            {
                var dx = notes[i].X - notes[i - 1].X;
                var dy = notes[i].Y - notes[i - 1].Y;
                Assert.Less(Math.Sqrt(dx * dx + dy * dy), 0.2, $"note {i} sits next to the one before it");
                Assert.Greater(notes[i].BirthSec, notes[i - 1].BirthSec - 1e-9);
            }
        }

        [Test]
        public void Trail_MoreMovement_MoreNotes()
        {
            var counts = new List<int>();
            foreach (var speed in new[] { 0f, 0.2f, 0.5f, 1.2f })
            {
                var trail = new HandNoteTrail(Defaults, seed: 5);
                var born = 0;
                var path = Crossing(speed);
                var seen = path(0.0);
                for (var frame = 0; frame <= 3 * RenderFps; frame++)
                {
                    var now = frame * RenderDt;
                    if (frame % 10 == 0) seen = path(now);   // a picture of the tracker 6 times a second
                    born += trail.Step(now, Frame(seen.x, seen.y, Math.Floor(now * 6.0) / 6.0 * 1000.0));
                }
                counts.Add(born);
            }

            Assert.AreEqual(0, counts[0], "a hand that does not move leaves nothing");
            Assert.Less(counts[0], counts[1]);
            Assert.Less(counts[1], counts[2]);
            Assert.Less(counts[2], counts[3]);
            Assert.LessOrEqual(counts[3], Defaults.NotesPerSec * 3 + 1, "and never more than notesPerSec a second");
        }

        [Test]
        public void Trail_RespectsMaxNotes()
        {
            var settings = TrailSettings.From(new TrailDto { notesPerSec = 30f, maxNotes = 12, lifeSec = 4f });
            var trail = new HandNoteTrail(settings, seed: 1);
            var most = 0;

            Run(trail, 10.0, 30.0, Crossing(FastHand), _ => false, (now, seen) => most = Math.Max(most, trail.Notes.Count));

            Assert.AreEqual(12, most, "the trail fills up to maxNotes and not one note more");
        }

        [Test]
        public void Trail_RespectsTheDefaultMaxNotes_AtTheHighestRate()
        {
            var settings = TrailSettings.From(new TrailDto { notesPerSec = 30f, maxNotes = 40, lifeSec = 4f });
            var trail = new HandNoteTrail(settings, seed: 1);
            var most = 0;

            Run(trail, 10.0, 30.0, Crossing(FastHand), _ => false, (now, seen) => most = Math.Max(most, trail.Notes.Count));

            Assert.AreEqual(40, most);
        }

        [Test]
        public void Trail_FistHand_HasNoTrail()
        {
            var fist = new HandNoteTrail(Defaults, seed: 2);
            var open = new HandNoteTrail(Defaults, seed: 2);
            var mostFist = 0;
            var mostOpen = 0;

            Run(fist, 3.0, 6.0, Crossing(1f), _ => true, (now, seen) => mostFist = Math.Max(mostFist, fist.Notes.Count));
            Run(open, 3.0, 6.0, Crossing(1f), _ => false, (now, seen) => mostOpen = Math.Max(mostOpen, open.Notes.Count));

            Assert.AreEqual(0, mostFist, "a fist plays nothing and leaves nothing");
            Assert.Greater(mostOpen, 0, "the same movement with an open hand does");
        }

        [Test]
        public void Trail_HandThatClosesIntoAFist_StopsBeingBorn_AndTheOldNotesFadeOutOnTheirOwn()
        {
            var trail = new HandNoteTrail(Defaults, seed: 2);

            Run(trail, 4.0, 6.0, Crossing(1f), t => t >= 1.5);

            Assert.AreEqual(0, trail.Notes.Count, "the last note was born before 1.5 s and has lived out its lifeSec by 4 s");
        }

        [Test]
        public void Trail_HandThatLeaves_StopsBeingBorn_AndTheNotesFadeOutAfterLifeSec()
        {
            var trail = new HandNoteTrail(Defaults, seed: 2);
            var countJustAfterLeaving = 0;
            for (var frame = 0; frame < 2 * RenderFps; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.01f * frame, 0.5f, frame * RenderDt * 1000.0));
            var leavesAt = 2.0;

            // Act
            for (var frame = 0; frame <= 1.5 * RenderFps; frame++)
            {
                var now = leavesAt + frame * RenderDt;
                trail.Step(now, Frame(0f, 0f, 0.0, visible: false));
                if (frame == 0) countJustAfterLeaving = trail.Notes.Count;
            }

            // Assert
            Assert.Greater(countJustAfterLeaving, 0, "the notes that are there stay and fade");
            Assert.AreEqual(0, trail.Notes.Count, "and a second and a half later (lifeSec is 1) they are all gone");
        }

        [Test]
        public void Trail_ANote_LivesLifeSecAndFadesWithAge()
        {
            var trail = new HandNoteTrail(Defaults, seed: 2);
            for (var frame = 0; frame <= 40; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.01f * frame, 0.5f, frame * RenderDt * 1000.0));
            Assert.Greater(trail.Notes.Count, 0);
            var note = trail.Notes[0];
            var life = Defaults.LifeSec;

            var young = TrailNote.Alpha(note.Age01(note.BirthSec + 0.1 * life, life));
            var middle = TrailNote.Alpha(note.Age01(note.BirthSec + 0.5 * life, life));
            var old = TrailNote.Alpha(note.Age01(note.BirthSec + 0.9 * life, life));
            var gone = TrailNote.Alpha(note.Age01(note.BirthSec + 1.2 * life, life));

            Assert.AreEqual(1f, young, 1e-6f, "a note is at full strength in the first part of its life");
            Assert.Greater(young, middle, "and fades after that");
            Assert.Greater(middle, old);
            Assert.Greater(old, 0f);
            Assert.AreEqual(0f, gone, 1e-6f);
            Assert.AreEqual(1f, TrailNote.Alpha(0f), 1e-6f, "a note is born at full opacity");
            Assert.AreEqual(0f, TrailNote.Alpha(-0.01f), 1e-6f, "and is not there before its birth");
            Assert.AreEqual(0f, TrailNote.Alpha(1f), 1e-6f);
            Assert.Greater(middle, 0.7f, "at half its life it still reads clearly");
        }

        [Test]
        public void Trail_ANote_IsFullSizeWhenBorn_AndGrowsSmallerAsItFades()
        {
            Assert.AreEqual(1f, TrailNote.Scale(0f), 1e-6f);
            Assert.AreEqual(TrailNote.EndScale, TrailNote.Scale(1f), 1e-6f);
            Assert.Greater(TrailNote.Scale(0.3f), TrailNote.Scale(0.7f));
            Assert.Greater(TrailNote.Scale(0.7f), TrailNote.EndScale);
            Assert.Greater(TrailNote.EndScale, 0.5f, "a slight scale-down, not a shrinking to nothing");
            Assert.AreEqual(TrailNote.EndScale, TrailNote.Scale(2f), 1e-6f, "and no smaller than that after the end");
        }

        [Test]
        public void Trail_ANewHand_DoesNotGlideOverFromWhereTheLastOneWas()
        {
            var trail = new HandNoteTrail(Defaults, seed: 2);
            for (var frame = 0; frame < 30; frame++) trail.Step(frame * RenderDt, Frame(0.1f, 0.3f, frame * RenderDt * 1000.0));

            trail.Step(1.0, new TrailHandFrame(true, false, 0.9f, 0.8f, 2000.0, handId: 7, epoch: 0, tag: 0));

            Assert.AreEqual(0.9f, trail.Place.X, 1e-6f);
            Assert.AreEqual(0.8f, trail.Place.Y, 1e-6f);
        }

        [Test]
        public void Trail_APauseOfTheFrames_DoesNotBurstNotesOut()
        {
            var trail = new HandNoteTrail(Defaults, seed: 2);
            for (var frame = 0; frame <= 60; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.015f * frame, 0.5f, frame * RenderDt * 1000.0));   // a hand that moves fast
            var now = 1.0;

            var born = trail.Step(now + 10.0, Frame(1.1f, 0.5f, (now + 10.0) * 1000.0));

            Assert.LessOrEqual(born, 2, "ten seconds in one frame is a hitch, not ten seconds of notes");
        }

        [Test]
        public void Trail_NotesCarryTheColourTheyWereBornWith()
        {
            var trail = new HandNoteTrail(Defaults, seed: 2);
            for (var frame = 0; frame < 60; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.015f * frame, 0.5f, frame * RenderDt * 1000.0, tag: 11));
            for (var frame = 60; frame < 90; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.015f * frame, 0.5f, frame * RenderDt * 1000.0, tag: 22));

            var tags = new HashSet<int>();
            for (var i = 0; i < trail.Notes.Count; i++) tags.Add(trail.Notes[i].Tag);

            CollectionAssert.AreEquivalent(new[] { 11, 22 }, tags, "the notes of the old cell keep the old colour while the new ones take the new");
        }

        [Test]
        public void TheSameSeed_GivesTheSameTrail()
        {
            var a = new HandNoteTrail(Defaults, seed: 9);
            var b = new HandNoteTrail(Defaults, seed: 9);

            Run(a, 2.0, 6.0, Crossing(0.6f), _ => false);
            Run(b, 2.0, 6.0, Crossing(0.6f), _ => false);

            Assert.AreEqual(a.Notes.Count, b.Notes.Count);
            for (var i = 0; i < a.Notes.Count; i++) Assert.AreEqual(a.Notes[i].X, b.Notes[i].X);
        }

        [Test]
        public void ScreenFollow_FirstPlaceIsDrawnAsItIs_AndResetForgetsIt()
        {
            var follow = new ScreenFollow();
            var first = follow.Step(0.25f, 0.75f, 1000.0, 1f / 60f, 0.8f, 2f, 1.5f);
            follow.Step(0.5f, 0.75f, 1167.0, 1f / 60f, 0.8f, 2f, 1.5f);

            follow.Reset();
            var again = follow.Step(0.9f, 0.1f, 1333.0, 1f / 60f, 0.8f, 2f, 1.5f);

            Assert.AreEqual(0.25f, first.X, 1e-6f);
            Assert.AreEqual(0.75f, first.Y, 1e-6f);
            Assert.AreEqual(0.9f, again.X, 1e-6f);
            Assert.AreEqual(0.1f, again.Y, 1e-6f);
        }

        [Test]
        public void ScreenFollow_GlidesInBothAxesBetweenTrackerFrames()
        {
            var follow = new ScreenFollow();
            follow.Step(0.2f, 0.2f, 1000.0, 1f / 60f, 0.8f, 2f, 1.5f);

            var first = follow.Step(0.4f, 0.6f, 1167.0, 1f / 60f, 0.8f, 2f, 1.5f);
            var second = follow.Step(0.4f, 0.6f, 1167.0, 1f / 60f, 0.8f, 2f, 1.5f);

            Assert.That(first.X, Is.GreaterThan(0.2f).And.LessThan(0.4f));
            Assert.That(first.Y, Is.GreaterThan(0.2f).And.LessThan(0.6f));
            Assert.Greater(second.X, first.X);
            Assert.Greater(second.Y, first.Y);
        }

        [Test]
        public void TheCountsOfALowTier_AreScaledDown_AndAScaleOfZeroTurnsTheTrailOff()
        {
            var settings = Defaults;

            var low = settings.WithCountScale(settings.LowCountScale);
            var off = settings.WithCountScale(0f);

            Assert.AreEqual(settings.NotesPerSec * 0.5f, low.NotesPerSec, 1e-5f);
            Assert.AreEqual(20, low.MaxNotes);
            Assert.AreEqual(settings.LifeSec, low.LifeSec);
            Assert.AreEqual(0, off.MaxNotes);
            var trail = new HandNoteTrail(off, seed: 1);
            for (var frame = 0; frame < 120; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.015f * frame, 0.5f, frame * RenderDt * 1000.0));
            Assert.AreEqual(0, trail.Notes.Count);
        }

        [Test]
        public void ANewTier_ChangesTheCountsWhileTheTrailRuns()
        {
            var trail = new HandNoteTrail(Defaults, seed: 4);
            for (var frame = 0; frame < 90; frame++) trail.Step(frame * RenderDt, Frame(0.1f + 0.012f * frame, 0.5f, frame * RenderDt * 1000.0));
            Assert.Greater(trail.Notes.Count, 1);

            trail.Configure(Defaults.WithCountScale(0f));

            Assert.AreEqual(0, trail.Notes.Count, "a trail that is turned off keeps no notes");
        }

        [Test]
        public void MovementAmount_IsZeroWhenStill_AndFullFromFullSpeed()
        {
            Assert.AreEqual(0f, HandNoteTrail.MovementAmount(0.01f, 0.05f, 0.8f), 1e-6f);
            Assert.AreEqual(0f, HandNoteTrail.MovementAmount(0.05f, 0.05f, 0.8f), 1e-6f);
            Assert.AreEqual(0.5f, HandNoteTrail.MovementAmount(0.425f, 0.05f, 0.8f), 1e-5f);
            Assert.AreEqual(1f, HandNoteTrail.MovementAmount(3f, 0.05f, 0.8f), 1e-6f);
            Assert.AreEqual(1f, HandNoteTrail.MovementAmount(1f, 1f, 1f), "no range: either all or nothing, never NaN");
        }

        [Test]
        public void AHandHeldStill_WithTheJitterOfARealTracker_LeavesNoNotes()
        {
            var trail = new HandNoteTrail(Defaults, seed: 6);
            var random = new Random(11);
            var most = 0;

            Run(trail, 6.0, 6.0, t => (0.5f + 0.003f * (float)(random.NextDouble() * 2 - 1), 0.5f + 0.003f * (float)(random.NextDouble() * 2 - 1)), _ => false,
                (now, seen) => most = Math.Max(most, trail.Notes.Count));

            Assert.AreEqual(0, most, "a hand that is held still, its picture jitter included, is not moving");
        }

        [Test]
        public void TheNoteGlyph_HasAHead_AStemAndAFlag_AndNothingInTheCorners()
        {
            // the glyph is a quaver on a unit square (u to the right, v up)
            Assert.Greater(NoteGlyphShape.Alpha(0.36f, 0.22f), 0.95f, "the head");
            Assert.Greater(NoteGlyphShape.Alpha(0.495f, 0.55f), 0.95f, "the stem");
            Assert.Greater(NoteGlyphShape.Alpha(0.563f, 0.665f), 0.95f, "the flag");
            Assert.AreEqual(0f, NoteGlyphShape.Alpha(0.02f, 0.02f), 0.02f, "the corner is empty");
            Assert.AreEqual(0f, NoteGlyphShape.Alpha(0.98f, 0.98f), 0.02f);
            Assert.Less(NoteGlyphShape.Alpha(0.15f, 0.65f), 0.3f, "and so is the space beside the stem");
        }

        [Test]
        public void TheNoteGlyph_IsTheColourOfTheCellInside_AndNearlyBlackAtItsEdge_SoALightNoteReadsOnABrightSky()
        {
            // the inside of the stem
            var inside = NoteGlyphShape.Sample(0.492f, 0.55f, 1f / 128f, out var insideGrey);
            // 0.02 of the square left of the stem (the flag is on its right): outside the glyph, inside its outline (0.034 wide)
            var outline = NoteGlyphShape.Sample(0.492f - 0.028f - 0.02f, 0.55f, 1f / 128f, out var outlineGrey);
            // 0.07 of the square left of it: past the outline, in the soft halo
            var halo = NoteGlyphShape.Sample(0.492f - 0.028f - 0.07f, 0.55f, 1f / 128f, out var haloGrey);
            var far = NoteGlyphShape.Sample(0.95f, 0.1f, 1f / 128f, out _);

            Assert.AreEqual(1f, inside, 1e-3f);
            Assert.AreEqual(1f, insideGrey, 1e-3f, "the glyph takes the colour of the cell as it is");
            Assert.Greater(outline, 0.95f, "the outline is solid");
            Assert.Less(outlineGrey, 0.1f, "and nearly black");
            Assert.Greater(halo, 0.1f, "the halo is a soft shadow");
            Assert.Less(halo, outline, "that is fainter than the outline");
            Assert.Less(haloGrey, 0.1f, "and dark too");
            Assert.AreEqual(0f, far, 0.02f, "and it is gone well inside the square");
            Assert.AreEqual(NoteGlyphShape.OutlineGrey, outlineGrey, 0.02f);
        }
    }
}
