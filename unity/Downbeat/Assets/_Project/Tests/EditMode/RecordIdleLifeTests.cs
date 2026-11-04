using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Record;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>Item 11 of the polish list: the record screen is not still while nobody plays (slow motes behind the panels), and each card says when it was made.</summary>
    public class RecordIdleLifeContentTests
    {
        static bool Mentions(ContentDto content, string text) => ContentValidator.Validate(content).Any(e => e.Contains(text));

        [Test]
        public void ShippedContent_HasTwentyFourSlowMotes_AndTheTimeOfDayFormat()
        {
            var record = TestContent.LoadShipped().record;

            Assert.AreEqual(24, record.idleMotes);
            Assert.AreEqual(0.02f, record.idleMoteSpeed, 1e-6f);
            Assert.AreEqual(0.3f, record.idleMoteDimmed, 1e-6f);
            Assert.Greater(record.idleMoteAlpha, 0f);
            Assert.AreEqual("{0} {1}:{2:00}", record.text.timeFormat);
            CollectionAssert.AreEqual(new[] { "오전", "오후" }, record.text.meridiem);
            Assert.IsEmpty(ContentValidator.Validate(TestContent.LoadShipped()), "the shipped content is valid");
        }

        [Test]
        public void TheDtoDefaults_AreTheValuesOfTheShippedContent()
        {
            var shipped = TestContent.LoadShipped().record;
            var defaults = new RecordDto();
            var defaultText = new RecordTextDto();

            Assert.AreEqual(shipped.idleMotes, defaults.idleMotes, "idleMotes (tools/lib/content-build.mjs has the same default)");
            Assert.AreEqual(shipped.idleMoteSpeed, defaults.idleMoteSpeed, 1e-6f, "idleMoteSpeed");
            Assert.AreEqual(shipped.idleMoteAlpha, defaults.idleMoteAlpha, 1e-6f, "idleMoteAlpha");
            Assert.AreEqual(shipped.idleMoteDimmed, defaults.idleMoteDimmed, 1e-6f, "idleMoteDimmed");
            Assert.AreEqual(shipped.text.timeFormat, defaultText.timeFormat, "text.timeFormat");
            CollectionAssert.AreEqual(shipped.text.meridiem, defaultText.meridiem, "text.meridiem");
            CollectionAssert.AreEqual(shipped.fontNames, defaults.fontNames, "fontNames (tools/lib/content-build.mjs has the same default)");
        }

        [Test]
        public void ContentWithoutTheNewFields_StillLoadsWithTheDefaults()
        {
            // a content.json from before this change has none of the new keys: they come from the defaults of the data class
            var json = "{\"record\":{\"windowSec\":8,\"cardsShown\":6,\"text\":{\"title\":\"t\"}}}";

            var record = JsonUtility.FromJson<ContentDto>(json).record;

            Assert.AreEqual(24, record.idleMotes);
            Assert.AreEqual("{0} {1}:{2:00}", record.text.timeFormat);
            Assert.AreEqual(2, record.text.meridiem.Length);
            Assert.AreEqual(4, record.fontNames.Length, "the four fonts to try come from the defaults");
            Assert.AreEqual("Apple SD Gothic Neo", record.fontNames[0]);
        }

        [Test]
        public void AContentFileFromBeforeTheCornerNoticeWasRemoved_StillLoadsAndIsValid()
        {
            // the shipped content as the older format wrote it: the font list inside meadow.overlay, none in record
            var json = JsonUtility.ToJson(TestContent.LoadShipped());
            json = Regex.Replace(json, "\"fontNames\":\\[[^\\]]*\\],?", string.Empty);
            json = json.Replace("\"meadow\":{", "\"meadow\":{\"overlay\":{\"text\":\"old\",\"fontNames\":[\"Old Font\"],\"fontSize\":28,\"color\":\"#ffffff\",\"alpha\":0.55,\"margin\":0.02},");
            Assert.IsFalse(json.Contains("\"fontNames\":[\"Apple"), "the test content has no record.fontNames");

            var content = JsonUtility.FromJson<ContentDto>(json);

            Assert.IsEmpty(ContentValidator.Validate(content), "an older content.json still passes the checks");
            CollectionAssert.AreEqual(new RecordDto().fontNames, content.record.fontNames);
        }

        [Test]
        public void AnEmptyFontList_IsStillReported_AlsoWhenTheFileSaysSo()
        {
            var json = JsonUtility.ToJson(TestContent.LoadShipped());
            json = Regex.Replace(json, "\"fontNames\":\\[[^\\]]*\\]", "\"fontNames\":[]");

            var content = JsonUtility.FromJson<ContentDto>(json);

            Assert.AreEqual(0, content.record.fontNames.Length);
            Assert.IsTrue(Mentions(content, "record.fontNames"));
        }

        [Test]
        public void NumbersOutsideTheirRange_AreReportedWithTheirNames()
        {
            foreach (var motes in new[] { -1, 65 })
            {
                var content = TestContent.LoadShipped();
                content.record.idleMotes = motes;
                Assert.IsTrue(Mentions(content, "record.idleMotes"), "idleMotes " + motes);
            }
            foreach (var speed in new[] { 0f, 0.001f, 0.2f, float.NaN })
            {
                var content = TestContent.LoadShipped();
                content.record.idleMoteSpeed = speed;
                Assert.IsTrue(Mentions(content, "record.idleMoteSpeed"), "idleMoteSpeed " + speed);
            }
            foreach (var alpha in new[] { 0f, 1.5f, float.NaN })
            {
                var content = TestContent.LoadShipped();
                content.record.idleMoteAlpha = alpha;
                Assert.IsTrue(Mentions(content, "record.idleMoteAlpha"), "idleMoteAlpha " + alpha);
            }
            foreach (var dimmed in new[] { -0.1f, 1.1f, float.NaN })
            {
                var content = TestContent.LoadShipped();
                content.record.idleMoteDimmed = dimmed;
                Assert.IsTrue(Mentions(content, "record.idleMoteDimmed"), "idleMoteDimmed " + dimmed);
            }
        }

        [Test]
        public void NoMotesAtAll_IsAValidChoice()
        {
            var content = TestContent.LoadShipped();
            content.record.idleMotes = 0;

            Assert.IsFalse(Mentions(content, "idleMote"));
            content.record.idleMotes = 64;
            Assert.IsFalse(Mentions(content, "idleMote"));
        }

        [Test]
        public void TheTimeFormatAndTheTwoWords_AreChecked()
        {
            var empty = TestContent.LoadShipped();
            empty.record.text.timeFormat = "";
            var broken = TestContent.LoadShipped();
            broken.record.text.timeFormat = "{0 {1}";
            var oneWord = TestContent.LoadShipped();
            oneWord.record.text.meridiem = new[] { "오전" };
            var blank = TestContent.LoadShipped();
            blank.record.text.meridiem = new[] { "오전", " " };
            var missing = TestContent.LoadShipped();
            missing.record.text.meridiem = null;

            Assert.IsTrue(Mentions(empty, "record.text.timeFormat"));
            Assert.IsTrue(Mentions(broken, "record.text.timeFormat"));
            Assert.IsTrue(Mentions(oneWord, "record.text.meridiem"));
            Assert.IsTrue(Mentions(blank, "record.text.meridiem"));
            Assert.IsTrue(Mentions(missing, "record.text.meridiem"));
        }
    }

    public class RecordIdlePainterTests
    {
        const double Now = 100.0;
        const float Width = 1920f;
        const float Height = 1080f;

        static RecordPalette Palette()
        {
            var p = new RecordPalette { Background = new Color(0.02f, 0.02f, 0.1f), BackgroundBottom = new Color(0.05f, 0.04f, 0.15f), Ink = Color.white, Muted = Color.gray, Staff = Color.blue, Accent = Color.yellow, Card = new Color(0.05f, 0.05f, 0.2f) };
            for (var i = 0; i < Parts.Count; i++) p.FlowerGlow[i] = Color.white;
            for (var i = 0; i < NatureLayers.Count; i++) p.NatureLine[i] = Color.green;
            return p;
        }

        sealed class Run
        {
            public readonly RecordBuilders Builders = new RecordBuilders();
            public readonly List<TextCommand> Texts = new List<TextCommand>();
            public readonly List<ThumbnailCommand> Thumbnails = new List<ThumbnailCommand>();
        }

        static RecordPainter Painter(Action<RecordDto> change = null)
        {
            var content = TestContent.LoadShipped();
            change?.Invoke(content.record);
            return new RecordPainter(content.record, Palette(), content.song.pitchSteps);
        }

        static Run Paint(RecordPainter painter, IReadOnlyList<RecordCardView> cards, DisplayMode mode = DisplayMode.Dual, double clock = 5.0, bool performing = false)
        {
            var content = TestContent.LoadShipped();
            var height = mode == DisplayMode.Dual ? Height : 302f;
            var layout = RecordLayout.Compute(mode, Width, height, content.record.cardsShown);
            var run = new Run();
            painter.Paint(layout, Now, clock, new ScoreModel(16), cards, new DateTime(2026, 10, 30), Width, height, run.Builders, run.Texts, run.Thumbnails, performing);
            return run;
        }

        static SessionCard CardAt(int hour, int minute, string id = null) =>
            new SessionCard(id ?? $"20261030-{hour:00}{minute:00}00", new DateTime(2026, 10, 30, hour, minute, 0), 71.6, 12,
                3, Enumerable.Range(0, 12).Select(i => new ScoreMark(i * 6.0, (Part)(i % Parts.Count), i % 10, 0.8f)).ToList(), "x.png");

        [Test]
        public void OnItsOwnScreen_TheRecordDrawsTheConfiguredNumberOfMotes_InOneBuilder()
        {
            var run = Paint(Painter(), new List<RecordCardView>());

            Assert.AreEqual(24, run.Builders.Motes.QuadCount, "one quad per mote, all in the one builder (one draw call)");
        }

        [Test]
        public void TheMoteCount_FollowsTheContent_AndZeroDrawsNone()
        {
            Assert.AreEqual(5, Paint(Painter(r => r.idleMotes = 5), new List<RecordCardView>()).Builders.Motes.QuadCount);
            Assert.AreEqual(0, Paint(Painter(r => r.idleMotes = 0), new List<RecordCardView>()).Builders.Motes.QuadCount);
        }

        [Test]
        public void TheStripOverTheMeadow_HasNoMotes_ItIsSmallAndTheMeadowBehindItMovesAlready()
        {
            var run = Paint(Painter(), new List<RecordCardView>(), DisplayMode.SingleOverlay);

            Assert.AreEqual(0, run.Builders.Motes.QuadCount);
        }

        static MoteState[] Motes(RecordPainter painter) => Enumerable.Range(0, painter.MoteCount).Select(painter.MoteAt).ToArray();

        [Test]
        public void TheMotes_MoveWithTheClock_AndKeepTheirNumber()
        {
            var painter = Painter();
            Paint(painter, new List<RecordCardView>(), clock: 5.0);
            var a = Motes(painter);
            Paint(painter, new List<RecordCardView>(), clock: 15.0);
            var b = Motes(painter);
            var again = Painter();
            Paint(again, new List<RecordCardView>(), clock: 5.0);

            Assert.AreEqual(24, a.Length);
            Assert.AreEqual(a.Length, b.Length);
            Assert.Greater(a.Where((m, i) => Math.Abs(m.Y - b[i].Y) > 0.02f).Count(), 12, "ten seconds later most of them are somewhere else");
            CollectionAssert.AreEqual(a.Select(m => m.X).ToArray(), Motes(again).Select(m => m.X).ToArray(), "and the same clock gives the same place (seeded)");
        }

        [Test]
        public void WhilePerforming_ThePainterDimsTheMotes_AndBringsThemBack()
        {
            var painter = Painter();
            var none = new List<RecordCardView>();
            Paint(painter, none, clock: 0.0);
            Assert.AreEqual(1f, painter.MoteLevel, 1e-6f);

            for (var t = 0.0; t < 6.0; t += 0.05) Paint(painter, none, clock: t, performing: true);
            var dimmed = painter.MoteLevel;
            for (var t = 6.0; t < 16.0; t += 0.05) Paint(painter, none, clock: t, performing: false);

            Assert.AreEqual(0.3f, dimmed, 1e-3f);
            Assert.AreEqual(1f, painter.MoteLevel, 1e-3f);
        }

        [Test]
        public void EachCard_ShowsTheTimeOfDayItWasMade_InTheContentFormat()
        {
            var cards = new List<RecordCardView> { new RecordCardView(CardAt(15, 42), null), new RecordCardView(CardAt(15, 40), null), new RecordCardView(CardAt(0, 30), null), new RecordCardView(CardAt(12, 5), null) };

            var run = Paint(Painter(), cards);

            Assert.AreEqual("오후 3:42", run.Texts.First(t => t.Slot == "time0").Text);
            Assert.AreEqual("오후 3:40", run.Texts.First(t => t.Slot == "time1").Text);
            Assert.AreEqual("오전 12:30", run.Texts.First(t => t.Slot == "time2").Text);
            Assert.AreEqual("오후 12:05", run.Texts.First(t => t.Slot == "time3").Text);
        }

        [Test]
        public void TwoCardsOfTheSameReplayedPerformance_AreToldApartByTheirTime()
        {
            var cards = new List<RecordCardView> { new RecordCardView(CardAt(15, 44, "a"), null), new RecordCardView(CardAt(15, 42, "b"), null) };

            var run = Paint(Painter(), cards);

            Assert.AreEqual(run.Texts.First(t => t.Slot == "card0").Text, run.Texts.First(t => t.Slot == "card1").Text, "same length, same nature: the old label could not tell them apart");
            Assert.AreNotEqual(run.Texts.First(t => t.Slot == "time0").Text, run.Texts.First(t => t.Slot == "time1").Text);
        }

        [Test]
        public void TheTime_SitsInTheTopLeftCornerOfTheCard_OverItsPicture()
        {
            var layout = RecordLayout.Compute(DisplayMode.Dual, Width, Height, 6);
            var card = layout.CardRect(0);

            var run = Paint(Painter(), new List<RecordCardView> { new RecordCardView(CardAt(15, 42), null) });

            var time = run.Texts.First(t => t.Slot == "time0");
            var label = run.Texts.First(t => t.Slot == "card0");
            Assert.AreEqual(TextAnchorKind.MiddleLeft, time.Anchor);
            Assert.That(time.X, Is.InRange(card.X, card.X + card.Width * 0.2f), "at the left edge of the card");
            Assert.That(time.Y, Is.InRange(card.Y + card.Height * 0.6f, card.Top), "in the upper part, over the picture");
            Assert.Greater(time.Y, label.Y, "above the line with the length and the nature");
            Assert.Less(time.Height, label.Height, "smaller than that line");
        }

        [Test]
        public void TheStripCards_ShowTheirTimeToo()
        {
            var run = Paint(Painter(), new List<RecordCardView> { new RecordCardView(CardAt(21, 3), null) }, DisplayMode.SingleOverlay);

            Assert.AreEqual("오후 9:03", run.Texts.First(t => t.Slot == "time0").Text);
        }

        [Test]
        public void WithoutCards_ThereIsNoTime_AndNoNumbersThatCouldIdentifyAnyone()
        {
            var empty = Paint(Painter(), new List<RecordCardView>());
            var one = Paint(Painter(), new List<RecordCardView> { new RecordCardView(CardAt(15, 42), null) });

            Assert.IsFalse(empty.Texts.Any(t => t.Slot.StartsWith("time")));
            foreach (var text in one.Texts.Select(t => t.Text)) StringAssert.DoesNotContain("@", text);
        }
    }

    /// <summary>The record screen is told by the coordinator that a visit is on (the state of the experience the app already follows), so the motes can step back.</summary>
    public class RecordIdleLifeCoordinatorTests
    {
        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "downbeat-idle-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void AVisitDimsTheMotes_AndTheQuietAfterwardsBringsThemBack()
        {
            var content = TestContent.LoadShipped();
            var cameraObject = new GameObject("meadow camera");
            var camera = cameraObject.AddComponent<Camera>();
            var viewObject = new GameObject("record");
            var view = viewObject.AddComponent<RecordScreenView>();
            try
            {
                var machine = new ExperienceStateMachine(NatureSettingsFactory.Experience(content.experience));
                var coordinator = new RecordCoordinator(content, _dir, camera, view, machine);
                view.Configure(content, coordinator.Score, camera, RecordMount.Offscreen, 0);
                coordinator.Start();
                var none = new List<NatureEvent>();
                var clock = 1000.0;

                machine.Update(clock, false, false, 0f);
                coordinator.Tick(clock, clock, 0, none);
                Assert.IsFalse(view.Performing, "nobody is there");

                machine.Update(clock, true, true, 0.1f);     // a hand comes: the visit starts
                coordinator.Tick(clock, clock, 1, none);
                Assert.IsTrue(view.Performing, "a hand is in");

                for (var i = 0; i < 400 && machine.State != ExperienceState.Idle; i++)
                {
                    clock += 0.5;
                    machine.Update(clock, false, false, 0f);   // the hands leave and the meadow goes quiet
                    coordinator.Tick(clock, clock, 0, none);
                }
                Assert.AreEqual(ExperienceState.Idle, machine.State);
                Assert.IsFalse(view.Performing, "the meadow is idle again");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void TheScreen_CanBeDrawnAtAGivenClock_SoACaptureIsRepeatable()
        {
            var content = TestContent.LoadShipped();
            var cameraObject = new GameObject("meadow camera");
            var camera = cameraObject.AddComponent<Camera>();
            var viewObject = new GameObject("record");
            var view = viewObject.AddComponent<RecordScreenView>();
            Texture2D a = null, b = null, c = null;
            try
            {
                view.Configure(content, new ScoreModel(16), camera, RecordMount.Offscreen, 0);

                a = view.RenderPicture(320, 180, 0.0, 500.0);
                b = view.RenderPicture(320, 180, 0.0, 500.0);
                c = view.RenderPicture(320, 180, 0.0, 530.0);

                CollectionAssert.AreEqual(a.GetPixels32(), b.GetPixels32(), "the same clock, the same picture");
                CollectionAssert.AreNotEqual(a.GetPixels32(), c.GetPixels32(), "thirty seconds later the motes (and the breathing) are elsewhere");
            }
            finally
            {
                foreach (var t in new[] { a, b, c }) if (t != null) UnityEngine.Object.DestroyImmediate(t);
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
