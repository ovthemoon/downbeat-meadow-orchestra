using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class DisplayPlanTests
    {
        [Test]
        public void Resolve_OneDisplay_IsSingleOverlay()
        {
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(1, "auto"));
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(1, "dual"), "two screens cannot be had on one");
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(0, "auto"));
        }

        [Test]
        public void Resolve_TwoDisplays_Auto_IsDual()
        {
            Assert.AreEqual(DisplayMode.Dual, DisplayPlan.Resolve(2, "auto"));
            Assert.AreEqual(DisplayMode.Dual, DisplayPlan.Resolve(3, "dual"));
            Assert.AreEqual(DisplayMode.Dual, DisplayPlan.Resolve(2, null), "no preference is auto");
            Assert.AreEqual(DisplayMode.Dual, DisplayPlan.Resolve(2, "nonsense"));
        }

        [Test]
        public void Resolve_ForcedSingle_WithTwoDisplays()
        {
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(2, "single"));
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(2, " SINGLE "));
        }

        [Test]
        public void Assignment_PutsTheRecordOnTheOtherScreen()
        {
            var a = DisplayAssignment.For(DisplayMode.Dual, 0, 1, 2);
            var b = DisplayAssignment.For(DisplayMode.Dual, 1, 0, 2);

            Assert.AreEqual((0, 1), (a.MeadowDisplay, a.RecordDisplay));
            Assert.AreEqual((1, 0), (b.MeadowDisplay, b.RecordDisplay));
        }

        [Test]
        public void Assignment_ASingleOverlayUsesTheMeadowScreen_AndAnIndexPastTheEndIsClamped()
        {
            var single = DisplayAssignment.For(DisplayMode.SingleOverlay, 0, 1, 2);
            var clamped = DisplayAssignment.For(DisplayMode.Dual, 5, 0, 2);

            Assert.AreEqual((0, 0), (single.MeadowDisplay, single.RecordDisplay));
            Assert.AreEqual(1, clamped.MeadowDisplay);
            Assert.AreEqual(0, clamped.RecordDisplay);
            Assert.Throws<ArgumentOutOfRangeException>(() => DisplayAssignment.For(DisplayMode.Dual, 0, 1, 0));
        }

        [Test]
        public void Resolve_AutoOnAMac_IsSingle_EvenWithSeveralScreens_ButAnExplicitDualStillWorks()
        {
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(3, "auto", isMac: true), "Unity on this Mac reports screens that are not monitors");
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(2, null, isMac: true));
            Assert.AreEqual(DisplayMode.Dual, DisplayPlan.Resolve(2, "dual", isMac: true), "asked for by name");
            Assert.AreEqual(DisplayMode.SingleOverlay, DisplayPlan.Resolve(1, "dual", isMac: true));
            Assert.AreEqual(DisplayMode.Dual, DisplayPlan.Resolve(2, "auto", isMac: false), "elsewhere auto is still dual with two");
        }

        [Test]
        public void Assignment_UsesTheRecordIndexOfTheContent_NotJustTheOtherOne()
        {
            var third = DisplayAssignment.For(DisplayMode.Dual, 0, 2, 3);
            var second = DisplayAssignment.For(DisplayMode.Dual, 1, 2, 3);

            Assert.AreEqual((0, 2), (third.MeadowDisplay, third.RecordDisplay), "three monitors: the record can go to the third");
            Assert.AreEqual((1, 2), (second.MeadowDisplay, second.RecordDisplay));
        }

        [Test]
        public void Assignment_ARecordIndexThatClashesOrIsOutOfReach_FallsBackToAnotherScreen()
        {
            var same = DisplayAssignment.For(DisplayMode.Dual, 0, 0, 2);
            var tooFar = DisplayAssignment.For(DisplayMode.Dual, 0, 5, 2);

            Assert.AreNotEqual(same.MeadowDisplay, same.RecordDisplay);
            Assert.AreEqual(1, tooFar.RecordDisplay, "past the last screen: the last one");
            Assert.AreNotEqual(tooFar.MeadowDisplay, tooFar.RecordDisplay);
        }

        [Test]
        public void Fullscreen_IsBorderlessUnlessExclusiveIsAsked()
        {
            Assert.AreEqual(FullscreenChoice.Borderless, FullscreenChoices.Parse(null));
            Assert.AreEqual(FullscreenChoice.Borderless, FullscreenChoices.Parse("borderless"));
            Assert.AreEqual(FullscreenChoice.Borderless, FullscreenChoices.Parse("nonsense"));
            Assert.AreEqual(FullscreenChoice.Exclusive, FullscreenChoices.Parse("Exclusive"));
        }
    }

    public class ScoreModelTests
    {
        static ScoreMark Mark(double time, int step = 3, Part part = Part.Harp) => new ScoreMark(time, part, step, 0.8f);

        [Test]
        public void Score_DropsOldest_WhenFull()
        {
            var score = new ScoreModel(4);
            for (var i = 0; i < 6; i++) score.Add(Mark(i));

            var visible = new List<ScoreMark>();
            score.CopyVisible(100.0, 1000.0, visible);

            Assert.AreEqual(4, score.Count);
            CollectionAssert.AreEqual(new[] { 2.0, 3.0, 4.0, 5.0 }, visible.Select(m => m.Time).ToArray(), "0 and 1 were dropped, the rest stay in order");
        }

        [Test]
        public void Score_CopyVisible_RespectsWindow()
        {
            var score = new ScoreModel(16);
            foreach (var t in new[] { 1.0, 5.0, 9.0, 10.0, 12.0 }) score.Add(Mark(t));
            var visible = new List<ScoreMark>();

            score.CopyVisible(10.0, 8.0, visible);   // (2, 10]

            CollectionAssert.AreEqual(new[] { 5.0, 9.0, 10.0 }, visible.Select(m => m.Time).ToArray(), "1 is too old, 12 is not heard yet");
        }

        [Test]
        public void Score_NatureLayers_JoinAndLeave_AndRememberWhen()
        {
            var score = new ScoreModel(8);

            score.SetLayerActive(NatureLayer.Fireflies, true, 10.0);
            score.SetLayerActive(NatureLayer.Fireflies, true, 12.0);   // said again: no change
            score.SetLayerActive(NatureLayer.Sky, true, 14.0);

            Assert.IsTrue(score.IsLayerActive(NatureLayer.Fireflies));
            Assert.IsFalse(score.IsLayerActive(NatureLayer.Hills));
            Assert.AreEqual(10.0, score.LayerChangedAt(NatureLayer.Fireflies));
            Assert.AreEqual(2, score.ActiveLayerCount);
            Assert.IsTrue(double.IsNegativeInfinity(score.LayerChangedAt(NatureLayer.Hills)));

            score.SetLayerActive(NatureLayer.Sky, false, 20.0);
            Assert.AreEqual(1, score.ActiveLayerCount);
            Assert.AreEqual(20.0, score.LayerChangedAt(NatureLayer.Sky));
        }

        [Test]
        public void Score_NatureGlints_AreWindowedLikeTheNotes()
        {
            var score = new ScoreModel(8);
            score.AddNature(NatureLayer.Sky, 3.0, 0.8f);
            score.AddNature(NatureLayer.Hills, 9.0, 1f);
            score.AddNature(NatureLayer.Hills, 11.0, 1f);
            var visible = new List<NatureMark>();

            score.CopyVisibleNature(10.0, 5.0, visible);

            Assert.AreEqual(1, visible.Count);
            Assert.AreEqual(NatureLayer.Hills, visible[0].Layer);
            Assert.AreEqual(9.0, visible[0].Time);
        }

        [Test]
        public void Score_ClampsVelocity_AndRejectsACapacityOfZero()
        {
            Assert.AreEqual(1f, new ScoreMark(0, Part.Bell, 0, 7f).Velocity);
            Assert.AreEqual(0f, new ScoreMark(0, Part.Bell, 0, -2f).Velocity);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreModel(0));
        }
    }

    public class ScoreLayoutTests
    {
        [Test]
        public void Layout_NowIsRightEdge()
        {
            var now = new ScoreMark(50.0, Part.Recorder, 4, 1f);
            var oldest = new ScoreMark(42.0, Part.Recorder, 4, 1f);

            Assert.AreEqual(1000f, ScoreLayout.Position(now, 50.0, 8.0, 1000f, 400f, 10).X, 1e-3f);
            Assert.AreEqual(0f, ScoreLayout.Position(oldest, 50.0, 8.0, 1000f, 400f, 10).X, 1e-3f, "the left edge is the window ago");
            Assert.AreEqual(500f, ScoreLayout.Position(new ScoreMark(46.0, Part.Recorder, 4, 1f), 50.0, 8.0, 1000f, 400f, 10).X, 1e-3f);
        }

        [Test]
        public void Layout_HigherPitchIsHigher()
        {
            float Y(int step) => ScoreLayout.Position(new ScoreMark(0, Part.Harp, step, 1f), 0, 8.0, 1000f, 400f, 10).Y;

            for (var step = 1; step < 10; step++) Assert.Greater(Y(step), Y(step - 1));
            Assert.Greater(Y(0), 0f);
            Assert.Less(Y(9), 400f);
        }

        [Test]
        public void Layout_FiveStaffLines_FallOnTheOddSteps_SoTheNotesSitOnAndBetweenLines()
        {
            var lines = Enumerable.Range(0, 5).Select(i => ScoreLayout.StaffLineY(i, 400f, 10)).ToArray();

            for (var i = 1; i < 5; i++) Assert.Greater(lines[i], lines[i - 1]);
            Assert.AreEqual(ScoreLayout.StepY(1, 400f, 10), lines[0], 1e-3f);
            Assert.AreEqual(ScoreLayout.StepY(9, 400f, 10), lines[4], 1e-3f);
            Assert.AreNotEqual(ScoreLayout.StepY(2, 400f, 10), lines[1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreLayout.StaffLineY(5, 400f, 10));
        }

        [Test]
        public void Layout_RejectsNonsense()
        {
            var mark = new ScoreMark(0, Part.Harp, 1, 1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreLayout.Position(mark, 0, 0.0, 100f, 100f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreLayout.Position(mark, 0, 8.0, 100f, 100f, 0));
        }
    }

    public class RecordLayoutTests
    {
        static IEnumerable<LayoutRect> Parts(RecordLayout l) => new[] { l.Title, l.Score, l.Nature, l.Cards }.Where(r => !r.IsEmpty);

        [TestCase(DisplayMode.Dual, 1920f, 1080f)]
        [TestCase(DisplayMode.Dual, 2560f, 1440f)]
        [TestCase(DisplayMode.SingleOverlay, 1920f, 302f)]
        [TestCase(DisplayMode.SingleOverlay, 1280f, 201f)]
        public void Layout_PartsStayInsideTheScreen_AndDoNotOverlap(DisplayMode mode, float w, float h)
        {
            var layout = RecordLayout.Compute(mode, w, h, 6);
            var parts = Parts(layout).ToList();

            foreach (var r in parts)
            {
                Assert.GreaterOrEqual(r.X, 0f);
                Assert.GreaterOrEqual(r.Y, 0f);
                Assert.LessOrEqual(r.Right, w + 1e-3f);
                Assert.LessOrEqual(r.Top, h + 1e-3f);
                Assert.Greater(r.Width, 0f);
                Assert.Greater(r.Height, 0f);
            }
            for (var i = 0; i < parts.Count; i++)
                for (var j = i + 1; j < parts.Count; j++) Assert.IsFalse(parts[i].Overlaps(parts[j]), $"{i} and {j} overlap");
        }

        [Test]
        public void Layout_Dual_HasATitle_Single_HasNone()
        {
            Assert.IsFalse(RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, 6).Title.IsEmpty);
            Assert.IsTrue(RecordLayout.Compute(DisplayMode.SingleOverlay, 1920f, 302f, 6).Title.IsEmpty);
        }

        [Test]
        public void Layout_CardsFitTheirArea_InOneRowOfSix_OrTwoRowsOfSix()
        {
            var six = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, 6);
            var twelve = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, 12);

            Assert.AreEqual((6, 1), (six.CardColumns, six.CardRows));
            Assert.AreEqual((6, 2), (twelve.CardColumns, twelve.CardRows));
            Assert.Greater(six.CardHeight, twelve.CardHeight);
            for (var i = 0; i < 12; i++)
            {
                var r = twelve.CardRect(i);
                Assert.GreaterOrEqual(r.X, twelve.Cards.X - 1e-3f);
                Assert.GreaterOrEqual(r.Y, twelve.Cards.Y - 1e-3f);
                Assert.LessOrEqual(r.Right, twelve.Cards.Right + 1e-3f);
                Assert.LessOrEqual(r.Top, twelve.Cards.Top + 1e-3f);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => twelve.CardRect(12));
        }

        [Test]
        public void Layout_Cards_AreBigEnoughToReadFromTwoMetres()
        {
            var layout = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, 6);

            Assert.GreaterOrEqual(layout.CardWidth, 270f, "a thumbnail of 16:9 at 270 px or more across");
            Assert.GreaterOrEqual(layout.CardHeight, 225f);
            Assert.GreaterOrEqual(layout.Score.Height, 0.33f * 1080f, "the score is the main thing on the screen");
            Assert.GreaterOrEqual(layout.Nature.Height / 3f, 55f, "a nature line is tall enough for a label that reads from afar");
        }

        [Test]
        public void Layout_Strip_StartsTheNatureLinesAtTheBottomMarginAndStacksTheScoreOnTop()
        {
            var layout = RecordLayout.Compute(DisplayMode.SingleOverlay, 1920f, 302f, 6);

            Assert.AreEqual(layout.Margin, layout.Nature.Y, 1e-3f, "nothing else is drawn in the lower left corner: the nature lines start at the bottom margin");
            Assert.AreEqual(layout.Nature.Top + layout.Gap, layout.Score.Y, 1e-3f, "the score sits right above the nature lines, one gap away");
            Assert.AreEqual(302f - layout.Margin, layout.Score.Top, 1e-3f, "and reaches up to the top margin");
        }

        [Test]
        public void Layout_StripShowsAtMostThreeCards()
        {
            var layout = RecordLayout.Compute(DisplayMode.SingleOverlay, 1920f, 302f, 6);

            Assert.AreEqual((3, 1), (layout.CardColumns, layout.CardRows));
        }

        [Test]
        public void Layout_RejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RecordLayout.Compute(DisplayMode.Dual, 0f, 1080f, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, 13));
        }
    }

    public class CardTextTests
    {
        [Test]
        public void Duration_ReadsAsMinutesAndSeconds()
        {
            Assert.AreEqual("1분 12초", CardText.Duration(71.6, "{0}분 {1:00}초"));
            Assert.AreEqual("0분 05초", CardText.Duration(5.2, "{0}분 {1:00}초"));
            Assert.AreEqual("0분 00초", CardText.Duration(-3, "{0}분 {1:00}초"));
            Assert.AreEqual("자연 3", CardText.Nature(3, "자연 {0}"));
            Assert.AreEqual("10월 29일", CardText.Date(new DateTime(2026, 10, 29), "{0}월 {1}일"));
        }

        [Test]
        public void Text_UsesTheInvariantCulture()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");   // other digits
                Assert.AreEqual("1:05", CardText.Duration(65, "{0}:{1:00}"));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }
    }

    public class SessionRecorderTests
    {
        static readonly DateTime Start = new DateTime(2026, 10, 29, 22, 15, 30);

        static ScheduledNote Note(double audioTime, int step = 4, Part part = Part.Recorder, float velocity = 0.7f) =>
            new ScheduledNote(1, audioTime, audioTime - 0.1, part, step, velocity, false);

        static SessionRecorder Recorder(int minNotes = 3, int maxMarks = 64) => new SessionRecorder(minNotes, maxMarks);

        [Test]
        public void Recorder_NoCardForTinySession()
        {
            var recorder = Recorder();
            recorder.OnSessionStarted(100.0, Start);
            recorder.OnNote(Note(101.0));
            recorder.OnNote(Note(102.0));

            Assert.IsNull(recorder.OnSessionEnded(150.0), "two notes: somebody walked past");
        }

        [Test]
        public void Recorder_CardHasCountsAndMaxLevel()
        {
            var recorder = Recorder();
            recorder.OnSessionStarted(100.0, Start);
            for (var i = 0; i < 10; i++) recorder.OnNote(Note(101.0 + i * 2.0, step: i % 10));
            recorder.OnLevel(1, 105.0);
            recorder.OnLevel(3, 112.0);
            recorder.OnLevel(2, 120.0);   // down again: the maximum stays

            var card = recorder.OnSessionEnded(190.0);

            Assert.IsNotNull(card);
            Assert.AreEqual(10, card.NoteCount);
            Assert.AreEqual(3, card.MaxLevel);
            Assert.AreEqual(19.0, card.DurationSec, 1e-9, "from the start of the visit to its last note, not to the quiet after it");
            Assert.AreEqual(Start, card.StartedLocal);
            Assert.AreEqual("20261029-221530", card.Id);
            Assert.AreEqual(10, card.ScoreLine.Count);
            Assert.AreEqual(1.0, card.ScoreLine[0].Time, 1e-9, "times are counted from the start of the visit");
            Assert.AreEqual(string.Empty, card.ThumbnailFile);
        }

        SessionRecorder RecorderWithAVisit(Func<SongLabel> source)
        {
            var recorder = Recorder();
            recorder.SetSongSource(source);
            recorder.OnSessionStarted(100.0, Start);
            for (var i = 0; i < 5; i++) recorder.OnNote(Note(101.0 + i));
            return recorder;
        }

        [Test]
        public void Recorder_PutsTheSongOfTheVisitOnTheCard()
        {
            var named = RecorderWithAVisit(() => new SongLabel("quiet-meadow", "잔잔한 들판")).OnSessionEnded(150.0);
            var freePlay = RecorderWithAVisit(() => default).OnSessionEnded(150.0);
            var noSource = RecorderWithAVisit(null).OnSessionEnded(150.0);

            Assert.AreEqual("quiet-meadow", named.SongId);
            Assert.AreEqual("잔잔한 들판", named.SongTitle);
            Assert.IsNull(freePlay.SongId, "default is no song");
            Assert.IsNull(freePlay.SongTitle);
            Assert.IsNull(noSource.SongId, "no source is free play");
            Assert.IsNull(noSource.SongTitle);
        }

        [Test]
        public void Recorder_ReadsTheSongWhenTheVisitEnds_AndTheNextVisitStartDoesNotForgetTheSource()
        {
            var song = new SongLabel("first", "First");
            var reads = 0;
            var recorder = Recorder();
            recorder.SetSongSource(() => { reads++; return song; });

            recorder.OnSessionStarted(100.0, Start);
            for (var i = 0; i < 5; i++) recorder.OnNote(Note(101.0 + i));
            Assert.AreEqual(0, reads, "not read while the visit goes on");
            song = new SongLabel("second", "Second");   // the song of the visit as it is when the visit ends
            var firstCard = recorder.OnSessionEnded(150.0);

            recorder.OnSessionStarted(200.0, Start.AddMinutes(5));
            for (var i = 0; i < 5; i++) recorder.OnNote(Note(201.0 + i));
            song = new SongLabel("third", "Third");
            var secondCard = recorder.OnSessionEnded(250.0);

            Assert.AreEqual("second", firstCard.SongId);
            Assert.AreEqual("third", secondCard.SongId, "the source set once serves every visit");
            Assert.AreEqual(2, reads);
        }

        [Test]
        public void Recorder_ATinyVisitMakesNoCard_AndDoesNotAskForTheSong()
        {
            var reads = 0;
            var recorder = Recorder();
            recorder.SetSongSource(() => { reads++; return new SongLabel("x", "X"); });
            recorder.OnSessionStarted(100.0, Start);
            recorder.OnNote(Note(101.0));

            Assert.IsNull(recorder.OnSessionEnded(150.0));
            Assert.AreEqual(0, reads);
        }

        [Test]
        public void Recorder_TheSourceCanBeTakenAway()
        {
            var recorder = RecorderWithAVisit(() => new SongLabel("x", "X"));
            recorder.SetSongSource(null);

            Assert.IsNull(recorder.OnSessionEnded(150.0).SongId);
        }

        [Test]
        public void Recorder_ScoreLineIsCappedAt64_KeepingFirstAndLast_InOrder()
        {
            var recorder = Recorder();
            recorder.OnSessionStarted(0.0, Start);
            for (var i = 0; i < 200; i++) recorder.OnNote(Note(1.0 + i, step: i % 10));

            var card = recorder.OnSessionEnded(500.0);

            Assert.AreEqual(200, card.NoteCount, "the count is of all the notes");
            Assert.AreEqual(64, card.ScoreLine.Count);
            Assert.AreEqual(1.0, card.ScoreLine[0].Time, 1e-9);
            Assert.AreEqual(200.0, card.ScoreLine[63].Time, 1e-9);
            for (var i = 1; i < 64; i++) Assert.Greater(card.ScoreLine[i].Time, card.ScoreLine[i - 1].Time);
        }

        [Test]
        public void Recorder_ClimaxReachedFiresWhenTheVisitReachesANewHighestLevel_Only()
        {
            var recorder = Recorder();
            var fired = 0;
            recorder.ClimaxReached += () => fired++;
            recorder.OnLevel(1, 5.0);                    // not recording: nothing
            recorder.OnSessionStarted(10.0, Start);

            recorder.OnLevel(0, 11.0);
            recorder.OnLevel(1, 12.0);
            recorder.OnLevel(1, 13.0);
            recorder.OnLevel(2, 14.0);
            recorder.OnLevel(1, 15.0);
            recorder.OnLevel(3, 16.0);

            Assert.AreEqual(3, fired, "level 1, 2 and 3 are each new once");
        }

        [Test]
        public void Recorder_ANewVisitStartsFromNothing()
        {
            var recorder = Recorder();
            recorder.OnSessionStarted(0.0, Start);
            for (var i = 0; i < 5; i++) recorder.OnNote(Note(1.0 + i));
            recorder.OnLevel(3, 5.0);
            recorder.OnSessionEnded(60.0);

            recorder.OnSessionStarted(100.0, Start.AddMinutes(5));
            for (var i = 0; i < 4; i++) recorder.OnNote(Note(101.0 + i));
            var second = recorder.OnSessionEnded(150.0);

            Assert.AreEqual(4, second.NoteCount);
            Assert.AreEqual(0, second.MaxLevel);
            Assert.AreEqual("20261029-222030", second.Id);
        }

        [Test]
        public void Recorder_IgnoresNotesOutsideAVisit_AndAnEndWithoutAStart()
        {
            var recorder = Recorder();

            recorder.OnNote(Note(5.0));
            Assert.AreEqual(0, recorder.NoteCount);
            Assert.IsNull(recorder.OnSessionEnded(10.0));
        }

        [Test]
        public void Recorder_KeepsAtMostTenThousandNotes_ButStillCountsThemAll()
        {
            var recorder = Recorder();
            recorder.OnSessionStarted(0.0, Start);

            for (var i = 0; i < SessionRecorder.MaxStoredNotes + 500; i++) recorder.OnNote(Note(1.0 + i * 0.01));

            Assert.AreEqual(SessionRecorder.MaxStoredNotes, recorder.StoredMarkCount, "a visit that never ends cannot grow the list for ever");
            Assert.AreEqual(SessionRecorder.MaxStoredNotes + 500, recorder.NoteCount);
            var card = recorder.OnSessionEnded(99999.0);
            Assert.AreEqual(SessionRecorder.MaxStoredNotes + 500, card.NoteCount);
            Assert.AreEqual(64, card.ScoreLine.Count);
        }

        [Test]
        public void Recorder_RejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRecorder(0, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRecorder(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRecorder(3, 9999));
        }
    }

    public class CardStoreTests
    {
        sealed class MemoryFileSystem : ICardFileSystem
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
            public readonly HashSet<string> Directories = new HashSet<string>();
            public bool DirectoryExists(string path) => Directories.Contains(path);
            public void CreateDirectory(string path) => Directories.Add(path);
            public bool FileExists(string path) => Files.ContainsKey(path);
            public void WriteAllText(string path, string text) => Files[path] = System.Text.Encoding.UTF8.GetBytes(text);
            public void WriteAllBytes(string path, byte[] bytes) => Files[path] = bytes;
            public string ReadAllText(string path) => System.Text.Encoding.UTF8.GetString(Files[path]);

            public IReadOnlyList<string> GetFiles(string directory, string searchPattern)
            {
                var ext = searchPattern.TrimStart('*');
                return Files.Keys.Where(k => Path.GetDirectoryName(k) == directory && k.EndsWith(ext, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            }
        }

        const string Root = "/data/cards";

        static SessionCard Card(DateTime started, int notes = 12, int maxLevel = 2, string id = null, SongLabel song = default)
        {
            var marks = Enumerable.Range(0, notes).Select(i => new ScoreMark(i * 1.5, (Part)(i % 3), i % 10, 0.3f + 0.05f * (i % 10))).ToList();
            return new SessionCard(id ?? started.ToString("yyyyMMdd-HHmmss"), started, notes * 1.5, notes, maxLevel, marks, string.Empty, song.IsEmpty ? null : song.Id, song.IsEmpty ? null : song.Title);
        }

        static CardStore Store(MemoryFileSystem fs, List<string> warnings = null) => new CardStore(fs, new JsonCardCodec(), Root, warnings == null ? (Action<string>)null : warnings.Add);

        [Test]
        public void Store_SaveThenLoadDay_RoundTrips()
        {
            var fs = new MemoryFileSystem();
            var store = Store(fs);
            var started = new DateTime(2026, 10, 29, 20, 5, 0);

            var saved = store.Save(Card(started), new byte[] { 1, 2, 3 });
            var loaded = store.LoadDay(new DateTime(2026, 10, 29));

            Assert.AreEqual(1, loaded.Count);
            var card = loaded[0];
            Assert.AreEqual(saved.Id, card.Id);
            Assert.AreEqual(started, card.StartedLocal);
            Assert.AreEqual(12, card.NoteCount);
            Assert.AreEqual(2, card.MaxLevel);
            Assert.AreEqual(18.0, card.DurationSec, 1e-4);
            Assert.AreEqual(12, card.ScoreLine.Count);
            Assert.AreEqual(Part.Recorder, card.ScoreLine[1].Part);
            Assert.AreEqual(3, card.ScoreLine[3].PitchStep);
            Assert.AreEqual(4.5, card.ScoreLine[3].Time, 1e-4);
            Assert.AreEqual(card.Id + ".png", card.ThumbnailFile);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fs.Files[store.ThumbnailPath(card)]);
        }

        [Test]
        public void Store_DayFolderChangesAtMidnight()
        {
            var fs = new MemoryFileSystem();
            var store = Store(fs);
            var before = new DateTime(2026, 10, 29, 23, 59, 30);
            var after = new DateTime(2026, 10, 30, 0, 0, 30);

            store.Save(Card(before), null);
            store.Save(Card(after), null);

            Assert.AreNotEqual(store.DayFolder(before), store.DayFolder(after));
            StringAssert.EndsWith("2026-10-29", store.DayFolder(before));
            StringAssert.EndsWith("2026-10-30", store.DayFolder(after));
            Assert.AreEqual(1, store.LoadDay(new DateTime(2026, 10, 29)).Count);
            Assert.AreEqual(1, store.LoadDay(new DateTime(2026, 10, 30)).Count, "the new day starts with its own cards only");
            Assert.AreEqual(0, store.LoadDay(new DateTime(2026, 10, 31)).Count);
        }

        [Test]
        public void Store_SkipsCorruptJson_AndSaysSo()
        {
            var fs = new MemoryFileSystem();
            var warnings = new List<string>();
            var store = Store(fs, warnings);
            var day = new DateTime(2026, 10, 29, 20, 0, 0);
            store.Save(Card(day), null);
            fs.WriteAllText(Path.Combine(store.DayFolder(day), "broken.json"), "{ this is not json");
            fs.WriteAllText(Path.Combine(store.DayFolder(day), "empty.json"), "");
            fs.WriteAllText(Path.Combine(store.DayFolder(day), "wrong.json"), "{\"version\":1,\"id\":\"x\",\"startedLocal\":\"yesterday\"}");
            store.Save(Card(day.AddMinutes(10)), null);

            var loaded = store.LoadDay(day);

            Assert.AreEqual(2, loaded.Count, "the two good cards are still there");
            Assert.AreEqual(3, warnings.Count);
            Assert.IsTrue(warnings.All(w => w.Contains("skipped")));
        }

        [Test]
        public void Store_LoadDay_IsOldestFirst_AndASecondCardInTheSameSecondKeepsBoth()
        {
            var fs = new MemoryFileSystem();
            var store = Store(fs);
            var t = new DateTime(2026, 10, 29, 12, 0, 0);
            store.Save(Card(t.AddMinutes(30)), null);
            var first = store.Save(Card(t), null);
            var second = store.Save(Card(t), null);

            var loaded = store.LoadDay(t);

            Assert.AreEqual(3, loaded.Count);
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(t, loaded[0].StartedLocal);
            Assert.AreEqual(t.AddMinutes(30), loaded[2].StartedLocal);
        }

        [Test]
        public void Store_AnIdThatReachesOutOfTheFolder_IsRefused()
        {
            var codec = new JsonCardCodec();
            var hostile = new SessionCardDto { version = 1, id = "../../evil", startedLocal = "2026-10-29T10:00:00", durationSec = 1, noteCount = 1, maxLevel = 0, scoreLine = new ScoreMarkDto[0] };
            var hostileThumb = new SessionCardDto { version = 1, id = "ok", startedLocal = "2026-10-29T10:00:00", durationSec = 1, noteCount = 1, maxLevel = 0, scoreLine = new ScoreMarkDto[0], thumbnailFile = "..\\..\\x.png" };

            Assert.IsFalse(codec.TryDecode(UnityEngine.JsonUtility.ToJson(hostile), out _, out var problem1));
            Assert.IsFalse(codec.TryDecode(UnityEngine.JsonUtility.ToJson(hostileThumb), out _, out var problem2));
            StringAssert.Contains("id", problem1);
            StringAssert.Contains("thumbnail", problem2);
        }

        [Test]
        public void Card_WithLegacyPartNames_StillLoads()
        {
            var legacy = new SessionCardDto
            {
                version = 1, id = "20261029-200000", startedLocal = "2026-10-29T20:00:00", durationSec = 30, noteCount = 3, maxLevel = 1, thumbnailFile = "x.png",
                scoreLine = new[]
                {
                    new ScoreMarkDto { t = 1f, part = "strings", step = 2, v = 0.5f },
                    new ScoreMarkDto { t = 2f, part = "winds", step = 4, v = 0.6f },
                    new ScoreMarkDto { t = 3f, part = "perc", step = 6, v = 0.7f },
                },
            };

            Assert.IsTrue(new JsonCardCodec().TryDecode(UnityEngine.JsonUtility.ToJson(legacy), out var card, out var problem), problem);

            CollectionAssert.AreEqual(new[] { Part.Harp, Part.Recorder, Part.Marimba }, card.ScoreLine.Select(m => m.Part).ToArray(), "the part that took the place of each old one");
        }

        [Test]
        public void Card_WritesAllSixPartNames_AndReadsThemBack()
        {
            var marks = Enumerable.Range(0, Parts.Count).Select(i => new ScoreMark(i, (Part)i, i, 0.5f)).ToList();
            var card = new SessionCard("20261029-200000", new DateTime(2026, 10, 29, 20, 0, 0), 10, Parts.Count, 1, marks, "x.png");

            var json = new JsonCardCodec().Encode(card);
            Assert.IsTrue(new JsonCardCodec().TryDecode(json, out var back, out var problem), problem);

            foreach (var name in new[] { "harp", "recorder", "bell", "lowstring", "marimba", "handdrum" }) StringAssert.Contains($"\"{name}\"", json);
            CollectionAssert.AreEqual(marks.Select(m => m.Part).ToArray(), back.ScoreLine.Select(m => m.Part).ToArray());
        }

        [Test]
        public void Card_Json_HoldsNumbersAndTheTimeOnly_NothingThatIdentifiesAPerson()
        {
            var allowed = new HashSet<string> { "version", "id", "startedLocal", "durationSec", "noteCount", "maxLevel", "scoreLine", "thumbnailFile", "songId", "songTitle", "t", "part", "step", "v" };
            var started = new DateTime(2026, 10, 29, 20, 5, 0);
            var withoutSong = new JsonCardCodec().Encode(Card(started));
            var withSong = new JsonCardCodec().Encode(Card(started, song: new SongLabel("quiet-meadow", "Quiet Meadow")));

            foreach (var json in new[] { withoutSong, withSong })
            {
                var keys = System.Text.RegularExpressions.Regex.Matches(json, "\"([A-Za-z]+)\":").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

                CollectionAssert.IsSubsetOf(keys, allowed);
                Assert.IsFalse(json.Contains("camera") || json.Contains("face") || json.Contains("name"));
            }
            StringAssert.Contains("\"songTitle\":\"Quiet Meadow\"", withSong, "the title of the song is the one new thing a card says");
        }

        [Test]
        public void Card_WithASong_RoundTrips_AndAnOldCardStillReads()
        {
            var fs = new MemoryFileSystem();
            var store = Store(fs);
            var started = new DateTime(2026, 10, 29, 20, 5, 0);

            var saved = store.Save(Card(started, song: new SongLabel("quiet-meadow", "잔잔한 들판")), new byte[] { 1 });
            var songless = store.Save(Card(started.AddMinutes(5)), null);
            var loaded = store.LoadDay(started);

            Assert.AreEqual("quiet-meadow", saved.SongId, "the card as stored keeps the song through the new id and thumbnail");
            Assert.AreEqual("잔잔한 들판", saved.SongTitle);
            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual("quiet-meadow", loaded[0].SongId);
            Assert.AreEqual("잔잔한 들판", loaded[0].SongTitle);
            Assert.IsNull(loaded[1].SongId, "a card of free play has no song");
            Assert.IsNull(loaded[1].SongTitle);
            Assert.AreEqual(songless.Id, loaded[1].Id);

            const string oldCard = "{\"version\":1,\"id\":\"20261029-200000\",\"startedLocal\":\"2026-10-29T20:00:00\",\"durationSec\":30.0,\"noteCount\":3,\"maxLevel\":1,"
                + "\"scoreLine\":[{\"t\":1.0,\"part\":\"harp\",\"step\":2,\"v\":0.5}],\"thumbnailFile\":\"x.png\"}";
            Assert.IsTrue(new JsonCardCodec().TryDecode(oldCard, out var old, out var problem), problem);
            Assert.AreEqual(3, old.NoteCount);
            Assert.IsNull(old.SongId, "a card written before songs existed reads with no song");
            Assert.IsNull(old.SongTitle);
            Assert.AreEqual(1, SessionCardDto.CurrentVersion, "the format did not change version, so no old card became unreadable");
        }

        [Test]
        public void Card_SongTitle_IsCutToSixtyCharacters_OnTheWayInAndOnTheWayBackIn()
        {
            var exactly60 = new string('가', 60);
            var tooLong = new string('나', 61);
            var emojiAtTheCut = new string('a', 59) + "\uD83C\uDF3C" + "tail";   // the 60th character would be half of a surrogate pair

            var kept = new SessionCard("a", new DateTime(2026, 10, 29, 20, 0, 0), 1, 1, 0, null, string.Empty, "id", exactly60);
            var cut = new SessionCard("b", new DateTime(2026, 10, 29, 20, 0, 0), 1, 1, 0, null, string.Empty, "id", tooLong);
            var halfPair = new SessionCard("c", new DateTime(2026, 10, 29, 20, 0, 0), 1, 1, 0, null, string.Empty, "id", emojiAtTheCut);
            var dto = cut.ToDto();
            dto.songTitle = new string('다', 500);   // a card file somebody edited by hand
            Assert.IsTrue(SessionCard.TryFromDto(dto, out var fromFile, out var problem), problem);

            Assert.AreEqual(exactly60, kept.SongTitle);
            Assert.AreEqual(new string('나', 60), cut.SongTitle);
            Assert.AreEqual(new string('a', 59), halfPair.SongTitle, "never half of a pair");
            Assert.AreEqual(new string('다', 60), fromFile.SongTitle);
        }

        [Test]
        public void Card_BlankSongFields_MeanNoSong()
        {
            var card = new SessionCard("a", new DateTime(2026, 10, 29, 20, 0, 0), 1, 1, 0, null, string.Empty, "  ", "");

            Assert.IsNull(card.SongId);
            Assert.IsNull(card.SongTitle);
        }

        [Test]
        public void Store_ARealFolder_WorksToo()
        {
            var dir = Path.Combine(Path.GetTempPath(), "downbeat-cards-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CardStore(new CardStoreIO(), new JsonCardCodec(), dir, null);
                var started = new DateTime(2026, 10, 29, 21, 0, 0);

                store.Save(Card(started), new byte[] { 9, 9 });
                var loaded = store.LoadDay(started);

                Assert.AreEqual(1, loaded.Count);
                Assert.IsTrue(File.Exists(store.ThumbnailPath(loaded[0])));
                Assert.IsTrue(Directory.Exists(Path.Combine(dir, "2026-10-29")));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }

    public class RecordContentTests
    {
        static bool Mentions(ContentDto content, string text) => ContentValidator.Validate(content).Any(e => e.Contains(text));

        [Test]
        public void ShippedContent_HasBorderlessAsTheDefaultFullscreenMode_AndAStripOf28Percent()
        {
            var content = TestContent.LoadShipped();

            Assert.AreEqual("auto", content.display.mode);
            Assert.AreEqual("borderless", content.display.fullscreenMode);
            Assert.AreEqual(0.28f, content.display.overlayFraction);
            Assert.AreEqual(0, content.display.meadowIndex);
            Assert.AreEqual(1, content.display.recordIndex);
            Assert.AreEqual(6, content.record.cardsShown);
            Assert.AreEqual(256, content.record.scoreCapacity);
            Assert.AreEqual(8f, content.record.windowSec);
        }

        [Test]
        public void MissingBlocks_AreReported()
        {
            var content = TestContent.LoadShipped();
            content.display = null;
            content.record = null;

            Assert.IsTrue(Mentions(content, "display is missing"));
            Assert.IsTrue(Mentions(content, "record is missing"));
        }

        [Test]
        public void NonsenseValues_AreReportedWithTheirNames()
        {
            var content = TestContent.LoadShipped();
            content.display.mode = "triple";
            content.display.fullscreenMode = "fancy";
            content.display.recordIndex = content.display.meadowIndex;
            content.display.overlayFraction = 0.9f;
            content.record.windowSec = 0f;
            content.record.cardsShown = 13;
            content.record.cardFolder = "../escape";
            content.record.colors.ink = "red";
            content.record.text.layers = new[] { "a" };
            content.record.text.durationFormat = "{0";

            foreach (var name in new[] { "display.mode", "display.fullscreenMode", "display.recordIndex", "display.overlayFraction", "record.windowSec", "record.cardsShown", "record.cardFolder", "record.colors.ink", "record.text.layers", "record.text.durationFormat" })
                Assert.IsTrue(Mentions(content, name), name);
        }

        [Test]
        public void ARecordScreenWithoutAFontToTry_IsReported()
        {
            var empty = TestContent.LoadShipped();
            empty.record.fontNames = new string[0];
            var missing = TestContent.LoadShipped();
            missing.record.fontNames = null;

            Assert.IsTrue(Mentions(empty, "record.fontNames"));
            Assert.IsTrue(Mentions(missing, "record.fontNames"));
        }

        [Test]
        public void TheShippedContentTriesTheKoreanFontsOfMacAndWindowsFirst()
        {
            var content = TestContent.LoadShipped();

            CollectionAssert.AreEqual(new[] { "Apple SD Gothic Neo", "Malgun Gothic", "NanumGothic", "Noto Sans CJK KR" }, content.record.fontNames);
            Assert.IsFalse(Mentions(content, "record.fontNames"));
        }
    }
}
