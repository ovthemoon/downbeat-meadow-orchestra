using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Record;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class RecordPainterTests
    {
        const double Now = 100.0;

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

        static Run Paint(ScoreModel score, IReadOnlyList<RecordCardView> cards, DisplayMode mode = DisplayMode.Dual, double now = Now)
        {
            var content = TestContent.LoadShipped();
            var painter = new RecordPainter(content.record, Palette(), content.song.pitchSteps);
            var height = mode == DisplayMode.Dual ? 1080f : 302f;
            var layout = RecordLayout.Compute(mode, 1920f, height, content.record.cardsShown);
            var run = new Run();
            painter.Paint(layout, now, 5.0, score, cards, new DateTime(2026, 10, 29), 1920f, height, run.Builders, run.Texts, run.Thumbnails);
            return run;
        }

        static SessionCard Card(double duration = 71.6, int maxLevel = 3, int notes = 12)
        {
            var marks = Enumerable.Range(0, notes).Select(i => new ScoreMark(i * duration / notes, (Part)(Parts.Count / 2 + i % 3), i % 10, 0.8f)).ToList();   // notes of the ground row: a flower each
            return new SessionCard("20261029-200000", new DateTime(2026, 10, 29, 20, 0, 0), duration, notes, maxLevel, marks, "x.png");
        }

        [Test]
        public void EachVisibleNote_IsOneFlowerOnTheScore_AndNoteOutsideTheWindowIsNone()
        {
            var score = new ScoreModel(64);
            score.Add(new ScoreMark(Now - 1.0, Part.LowString, 3, 0.8f));
            score.Add(new ScoreMark(Now - 3.0, Part.Marimba, 5, 0.8f));
            score.Add(new ScoreMark(Now - 7.5, Part.HandDrum, 6, 0.8f));
            score.Add(new ScoreMark(Now - 20.0, Part.HandDrum, 6, 0.8f));   // older than the window
            score.Add(new ScoreMark(Now + 1.0, Part.HandDrum, 6, 0.8f));    // not heard yet

            var run = Paint(score, new List<RecordCardView>());

            Assert.AreEqual(3 + FlowerIconPainter.IconCount, run.Builders.Flowers.QuadCount, "three notes of the ground and the three icons of the title");
        }

        [TestCase(Part.Harp)]
        [TestCase(Part.Recorder)]
        [TestCase(Part.Bell)]
        public void Record_SkyNote_DrawsALightDot(Part sky)
        {
            var quiet = Paint(new ScoreModel(16), new List<RecordCardView>());
            var score = new ScoreModel(16);
            score.Add(new ScoreMark(Now - 1.0, sky, 5, 0.8f));

            var run = Paint(score, new List<RecordCardView>());

            Assert.AreEqual(quiet.Builders.Flowers.QuadCount, run.Builders.Flowers.QuadCount, "a note of the sky is no flower");
            Assert.AreEqual(quiet.Builders.Glow.QuadCount + 2, run.Builders.Glow.QuadCount, "but a small light: a halo and a heart");
        }

        static void AssertSameColour(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-5f, message);
            Assert.AreEqual(expected.g, actual.g, 1e-5f, message);
            Assert.AreEqual(expected.b, actual.b, 1e-5f, message);
        }

        [Test]
        public void Record_SkyNote_IsTheColourOfItsCell_AndGroundNoteGlowsInTheColourOfItsFlower()
        {
            var palette = Palette();
            palette.FlowerGlow[(int)Part.Bell] = new Color(0.2f, 0.4f, 0.9f);
            palette.FlowerGlow[(int)Part.Marimba] = new Color(0.9f, 0.4f, 0.2f);
            var content = TestContent.LoadShipped();
            var painter = new RecordPainter(content.record, palette, content.song.pitchSteps);
            var layout = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, content.record.cardsShown);
            var score = new ScoreModel(16);
            score.Add(new ScoreMark(Now - 1.0, Part.Bell, 5, 0.8f));
            var sky = new Run();
            painter.Paint(layout, Now, 5.0, score, new List<RecordCardView>(), new DateTime(2026, 10, 29), 1920f, 1080f, sky.Builders, sky.Texts, sky.Thumbnails);
            var ground = new Run();
            var groundScore = new ScoreModel(16);
            groundScore.Add(new ScoreMark(Now - 1.0, Part.Marimba, 5, 0.8f));
            painter.Paint(layout, Now, 5.0, groundScore, new List<RecordCardView>(), new DateTime(2026, 10, 29), 1920f, 1080f, ground.Builders, ground.Texts, ground.Thumbnails);

            AssertSameColour(palette.FlowerGlow[(int)Part.Bell], sky.Builders.Glow.QuadColor(sky.Builders.Glow.QuadCount - 1), "the light dot has the colour of the bell's cell");
            AssertSameColour(palette.FlowerGlow[(int)Part.Marimba], ground.Builders.Glow.QuadColor(ground.Builders.Glow.QuadCount - 1), "and the glow behind a flower the colour of that flower");
        }

        [Test]
        public void AnEmptyMeadow_ShowsTheHint_AndNoCardsMessage()
        {
            var run = Paint(new ScoreModel(16), new List<RecordCardView>());

            CollectionAssert.Contains(run.Texts.Select(t => t.Slot).ToList(), "hint");
            CollectionAssert.Contains(run.Texts.Select(t => t.Slot).ToList(), "nocards");
            CollectionAssert.Contains(run.Texts.Select(t => t.Slot).ToList(), "title");
            Assert.AreEqual(0, run.Thumbnails.Count);
        }

        [Test]
        public void ThePlayedScore_HidesTheHint()
        {
            var score = new ScoreModel(16);
            score.Add(new ScoreMark(Now - 0.5, Part.Harp, 3, 0.8f));

            var run = Paint(score, new List<RecordCardView>());

            CollectionAssert.DoesNotContain(run.Texts.Select(t => t.Slot).ToList(), "hint");
        }

        [Test]
        public void ThreeNatureLines_AlwaysHaveALabel_AndAJoinedLineLightsUp()
        {
            var score = new ScoreModel(16);
            var quiet = Paint(score, new List<RecordCardView>());
            score.SetLayerActive(NatureLayer.Fireflies, true, Now - 5.0);
            var joined = Paint(score, new List<RecordCardView>());

            foreach (var run in new[] { quiet, joined })
                for (var i = 0; i < 3; i++) Assert.IsTrue(run.Texts.Any(t => t.Slot == "layer" + i), "layer" + i);
            var label0Quiet = quiet.Texts.First(t => t.Slot == "layer0").Color;
            var label0Joined = joined.Texts.First(t => t.Slot == "layer0").Color;
            Assert.Greater(label0Joined.a, label0Quiet.a, "the name of a layer that has joined is brighter");
            Assert.Greater(joined.Builders.Glow.QuadCount, quiet.Builders.Glow.QuadCount, "and has a glow");
        }

        [Test]
        public void NatureGlints_AreDrawn_WhenTheyAreInTheWindow()
        {
            var score = new ScoreModel(16);
            var before = Paint(score, new List<RecordCardView>()).Builders.Glow.QuadCount;
            score.AddNature(NatureLayer.Sky, Now - 2.0, 0.8f);
            score.AddNature(NatureLayer.Hills, Now - 30.0, 1f);

            var after = Paint(score, new List<RecordCardView>()).Builders.Glow.QuadCount;

            Assert.AreEqual(before + 1, after, "one glint in the window, one outside");
        }

        [Test]
        public void ACard_ShowsItsThumbnail_ItsScoreLine_ItsTimeAndNatureCount()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                var run = Paint(new ScoreModel(16), new List<RecordCardView> { new RecordCardView(Card(), texture), new RecordCardView(Card(), null) });

                Assert.AreEqual(1, run.Thumbnails.Count, "only the card with a picture has a thumbnail");
                var label = run.Texts.First(t => t.Slot == "card0").Text;
                StringAssert.Contains("1분 12초", label);
                StringAssert.Contains("자연 3", label);
                CollectionAssert.DoesNotContain(run.Texts.Select(t => t.Slot).ToList(), "nocards");
                Assert.AreEqual(FlowerIconPainter.IconCount + 24, run.Builders.Flowers.QuadCount, "the 12 notes of each of the two cards, and the title icons");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static SessionCard CardWithSong(string title, string id = "quiet-meadow") =>
            new SessionCard("20261101-200000", new DateTime(2026, 11, 1, 20, 0, 0), 60.0, 12, 3, Card().ScoreLine, "x.png", id, title);

        [Test]
        public void RecordView_CardWithASong_ShowsItsTitle()
        {
            var cards = new List<RecordCardView> { new RecordCardView(CardWithSong("잔잔한 들판"), null), new RecordCardView(Card(), null) };

            var run = Paint(new ScoreModel(16), cards);

            var song = run.Texts.First(t => t.Slot == "song0");
            Assert.AreEqual("잔잔한 들판", song.Text);
            var detail = run.Texts.First(t => t.Slot == "card0");
            Assert.Less(song.Y, detail.Y, "the song is a line under the duration and the nature count");
            var rect = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, TestContent.LoadShipped().record.cardsShown).CardRect(0);
            Assert.GreaterOrEqual(song.Y - song.Height * 0.5f, rect.Y, "inside the card");
            Assert.LessOrEqual(detail.Y + detail.Height * 0.5f, rect.Top, "and so is the line above it");
            CollectionAssert.DoesNotContain(run.Texts.Select(t => t.Slot).ToList(), "song1", "a card of free play has no song line");
            Assert.AreEqual("", cards[1].SongLabel);
            Assert.AreEqual("잔잔한 들판", cards[0].SongLabel);
        }

        [Test]
        public void RecordView_CardWithoutASong_LooksAsItDidBeforeSongs()
        {
            var run = Paint(new ScoreModel(16), new List<RecordCardView> { new RecordCardView(Card(), null), new RecordCardView(Card(), null) });

            var rect = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, TestContent.LoadShipped().record.cardsShown).CardRect(1);
            const float ThumbPad = 10f;   // the card as shipped before songs: the thumbnail 16:9 under a pad, the label centred in what is left below it
            var below = rect.Height - ThumbPad - (rect.Width - 2f * ThumbPad) * 9f / 16f;
            var detail = run.Texts.First(t => t.Slot == "card1");
            Assert.AreEqual(rect.Y + below * 0.5f, detail.Y, 1e-3f);
            Assert.AreEqual(rect.X + rect.Width * 0.5f, detail.X, 1e-3f);
            Assert.AreEqual(0, run.Texts.Count(t => t.Slot.StartsWith("song")));
        }

        [Test]
        public void RecordView_ALongSongTitle_IsShrunkToFitTheCard()
        {
            var longTitle = new string('가', SessionCard.MaxSongTitleLength);
            var run = Paint(new ScoreModel(16), new List<RecordCardView> { new RecordCardView(CardWithSong(longTitle), null) });

            var song = run.Texts.First(t => t.Slot == "song0");
            var rect = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, TestContent.LoadShipped().record.cardsShown).CardRect(0);
            Assert.LessOrEqual(song.Height * longTitle.Length, rect.Width, "a Korean syllable is about one em wide: the line is not wider than the card");
            Assert.Greater(song.Height, 0f);
        }

        [Test]
        public void RecordView_TheStripCard_ShowsItsSongToo()
        {
            var run = Paint(new ScoreModel(16), new List<RecordCardView> { new RecordCardView(CardWithSong("밝은 아침", "bright-morning"), null) }, DisplayMode.SingleOverlay);

            Assert.AreEqual("밝은 아침", run.Texts.First(t => t.Slot == "song0").Text);
        }

        [Test]
        public void ThePicturesOfTheCards_FollowTheLayout_NewestFirst()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                var cards = Enumerable.Range(0, 3).Select(_ => new RecordCardView(Card(), texture)).ToList();

                var run = Paint(new ScoreModel(16), cards);

                Assert.AreEqual(3, run.Thumbnails.Count);
                Assert.Less(run.Thumbnails[0].Rect.X, run.Thumbnails[1].Rect.X, "the newest is on the left");
                Assert.Less(run.Thumbnails[1].Rect.X, run.Thumbnails[2].Rect.X);
                Assert.AreEqual(16f / 9f, run.Thumbnails[0].Rect.Width / run.Thumbnails[0].Rect.Height, 0.01f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TheStrip_HasNoTitle_AndLessCards()
        {
            var cards = Enumerable.Range(0, 6).Select(_ => new RecordCardView(Card(), null)).ToList();

            var run = Paint(new ScoreModel(16), cards, DisplayMode.SingleOverlay);

            CollectionAssert.DoesNotContain(run.Texts.Select(t => t.Slot).ToList(), "title");
            Assert.AreEqual(3, run.Texts.Count(t => t.Slot.StartsWith("card")), "three cards fit the strip");
        }

        [Test]
        public void ACardIsTextOnlyWithNumbers_NoPersonalDataAnywhere()
        {
            var run = Paint(new ScoreModel(16), new List<RecordCardView> { new RecordCardView(Card(), null) });

            foreach (var text in run.Texts.Select(t => t.Text)) StringAssert.DoesNotContain("@", text);
            Assert.IsTrue(run.Texts.All(t => t.Text.Length < 60));
        }
    }

    public class FlowerIconTests
    {
        [Test]
        public void TheThreeIcons_AreDrawnWithTheirOwnShapesAndColours_AndAreTransparentAtTheCorners()
        {
            var open = new[] { new Color(0.4f, 0.5f, 1f), new Color(1f, 0.95f, 0.8f), new Color(1f, 0.2f, 0.15f) };
            var center = new[] { new Color(0.95f, 0.93f, 1f), new Color(1f, 0.9f, 0.6f), new Color(0.15f, 0.05f, 0.1f) };
            var texture = FlowerIconPainter.Create(open, center, keepReadable: true);
            try
            {
                var pixels = texture.GetPixels32();
                int Index(int icon, int x, int y) => y * FlowerIconPainter.IconSize * FlowerIconPainter.IconCount + icon * FlowerIconPainter.IconSize + x;
                var size = FlowerIconPainter.IconSize;
                for (var icon = 0; icon < 3; icon++)
                {
                    Assert.AreEqual(0, pixels[Index(icon, 1, 1)].a, $"icon {icon}: the corner is empty");
                    Assert.Greater(pixels[Index(icon, size / 2, size / 2)].a, 200, $"icon {icon}: the heart is solid");
                }
                var poppyPetal = pixels[Index(2, size * 3 / 4, size * 3 / 4)];
                Assert.Greater(poppyPetal.r, poppyPetal.b, "a poppy petal is red, not blue");
                var bellPetal = pixels[Index(0, size / 2, size * 7 / 8)];
                Assert.Greater(bellPetal.b, bellPetal.r, "a bellflower lobe is blue-violet");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void IconUvs_TileTheAtlasWithoutOverlap()
        {
            FlowerIconPainter.Uv(0, out var a0, out var a1);
            FlowerIconPainter.Uv(1, out var b0, out var b1);
            FlowerIconPainter.Uv(2, out var c0, out var c1);

            Assert.AreEqual(a1.x, b0.x, 1e-6f);
            Assert.AreEqual(b1.x, c0.x, 1e-6f);
            Assert.AreEqual(0f, a0.x);
            Assert.AreEqual(1f, c1.x, 1e-6f);
        }
    }

    public class DisplayRouterTests
    {
        sealed class FakeDisplays : IDisplayApi
        {
            public FakeDisplays(int count) { Count = count; }
            public int Count { get; }
            public readonly List<int> Activated = new List<int>();
            public void Activate(int index) => Activated.Add(index);
        }

        static DisplayRouter Router(int count, string contentMode = "auto", int meadowIndex = 0, params string[] args) => Router(count, false, contentMode, meadowIndex, args);

        static DisplayRouter Router(int count, bool isMac, string contentMode = "auto", int meadowIndex = 0, params string[] args)
        {
            var content = TestContent.LoadShipped();
            content.display.mode = contentMode;
            content.display.meadowIndex = meadowIndex;
            return new DisplayRouter(content, LaunchArgs.Parse(args), new FakeDisplays(count), isMac);
        }

        [Test]
        public void TwoScreens_Auto_IsDual_MeadowFirstRecordSecond_AndTheSecondIsSwitchedOn()
        {
            var displays = new FakeDisplays(2);
            var content = TestContent.LoadShipped();
            var router = new DisplayRouter(content, LaunchArgs.Empty, displays);
            var camera = new GameObject("cam").AddComponent<Camera>();
            try
            {
                router.Apply(camera);

                Assert.AreEqual(DisplayMode.Dual, router.Mode);
                Assert.AreEqual(RecordMount.Display, router.Mount);
                Assert.AreEqual(0, camera.targetDisplay);
                Assert.AreEqual(1, router.Assignment.RecordDisplay);
                CollectionAssert.AreEqual(new[] { 1 }, displays.Activated, "screen 0 is always on; screen 1 is switched on");
            }
            finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
        }

        [Test]
        public void TheMeadowCanBeOnTheSecondScreen()
        {
            var displays = new FakeDisplays(2);
            var content = TestContent.LoadShipped();
            content.display.meadowIndex = 1;
            var router = new DisplayRouter(content, LaunchArgs.Empty, displays);
            var camera = new GameObject("cam").AddComponent<Camera>();
            try
            {
                router.Apply(camera);

                Assert.AreEqual(1, camera.targetDisplay);
                Assert.AreEqual(0, router.Assignment.RecordDisplay);
                Assert.IsTrue(displays.Activated.Contains(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
        }

        [Test]
        public void OneScreen_IsAStrip_ActivatesNothing_AndTabTogglesIt()
        {
            var displays = new FakeDisplays(1);
            var router = new DisplayRouter(TestContent.LoadShipped(), LaunchArgs.Empty, displays);
            var camera = new GameObject("cam").AddComponent<Camera>();
            try
            {
                router.Apply(camera);

                Assert.AreEqual(DisplayMode.SingleOverlay, router.Mode);
                Assert.AreEqual(RecordMount.Strip, router.Mount);
                Assert.IsEmpty(displays.Activated);
                Assert.IsTrue(router.StripShown);
                Assert.IsFalse(router.ToggleStrip());
                Assert.IsTrue(router.ToggleStrip());
            }
            finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
        }

        [Test]
        public void TheCommandLineOverridesTheContent_ButOneScreenIsAlwaysAStrip()
        {
            Assert.AreEqual(DisplayMode.SingleOverlay, Router(2, "auto", 0, "--display", "single").Mode);
            Assert.AreEqual(DisplayMode.Dual, Router(2, "single", 0, "--display", "dual").Mode);
            Assert.AreEqual(DisplayMode.SingleOverlay, Router(1, "dual", 0, "--display", "dual").Mode);
            Assert.AreEqual(DisplayMode.SingleOverlay, Router(2, "single").Mode);
        }

        [Test]
        public void OnThreeScreens_TheRecordGoesWhereTheContentSays_AndOnlyThatScreenIsSwitchedOn()
        {
            var displays = new FakeDisplays(3);
            var content = TestContent.LoadShipped();
            content.display.recordIndex = 2;
            var router = new DisplayRouter(content, LaunchArgs.Empty, displays);
            var camera = new GameObject("cam").AddComponent<Camera>();
            try
            {
                router.Apply(camera);

                Assert.AreEqual(2, router.Assignment.RecordDisplay);
                CollectionAssert.AreEqual(new[] { 2 }, displays.Activated, "a screen that is switched on cannot be switched off again: the one in the middle is left alone");
            }
            finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
        }

        [Test]
        public void OnAMac_AutoIsAStrip_EvenWhenUnityReportsThreeScreens_ButDualCanBeAskedFor()
        {
            Assert.AreEqual(DisplayMode.SingleOverlay, Router(3, true).Mode);
            Assert.AreEqual(DisplayMode.Dual, Router(3, true, "auto", 0, "--display", "dual").Mode);
            Assert.AreEqual(DisplayMode.Dual, Router(3, false).Mode);
        }

        [Test]
        public void OnTwoScreens_TabDoesNotHideTheRecord()
        {
            var router = Router(2);

            Assert.IsTrue(router.ToggleStrip());
            Assert.IsTrue(router.StripShown);
        }
    }

    /// <summary>The whole way from a performance to a card, without the audio: notes on the score, a visit that starts and ends, a card on disk and on the screen.</summary>
    public class RecordPipelineTests
    {
        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "downbeat-record-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void AVisit_WritesACard_WithAThumbnail_AndShowsItAmongTodaysCards()
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

                var now = 1000.0;
                machine.Update(now, true, true, 0.1f);          // a hand comes: the visit starts
                for (var i = 0; i < 6; i++)
                {
                    now += 1.0;
                    coordinator.OnNote(new ScheduledNote(i, now, now - 0.1, (Part)(i % 3), i % 10, 0.8f, false));
                    machine.Update(now, true, true, 0.5f);
                    coordinator.Tick(now, now, i < 2 ? 1 : 3, none);
                }
                Assert.AreEqual(6, coordinator.Score.Count);
                Assert.AreEqual(3, coordinator.Score.ActiveLayerCount);

                for (var i = 0; i < 400 && machine.State != ExperienceState.Idle; i++)
                {
                    now += 0.5;
                    machine.Update(now, false, false, 0f);       // the hands leave, the meadow goes quiet: the visit ends
                }

                Assert.AreEqual(ExperienceState.Idle, machine.State);
                Assert.AreEqual(1, coordinator.CardsWritten);
                Assert.AreEqual(1, coordinator.CardsShown);
                var cards = coordinator.Store.LoadDay(DateTime.Today);
                Assert.AreEqual(1, cards.Count);
                Assert.AreEqual(6, cards[0].NoteCount);
                Assert.AreEqual(3, cards[0].MaxLevel);
                Assert.IsNotEmpty(cards[0].ThumbnailFile, "the climax picture of the meadow was taken");
                Assert.IsTrue(File.Exists(coordinator.Store.ThumbnailPath(cards[0])));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void AWalkPast_WritesNoCard()
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

                var now = 50.0;
                machine.Update(now, true, true, 0.1f);
                coordinator.OnNote(new ScheduledNote(0, now + 1.0, now + 0.9, Part.Recorder, 3, 0.8f, false));
                for (var i = 0; i < 400 && machine.State != ExperienceState.Idle; i++)
                {
                    now += 0.5;
                    machine.Update(now, false, false, 0f);
                }

                Assert.AreEqual(0, coordinator.CardsWritten, "one note is somebody walking past");
                Assert.AreEqual(0, coordinator.Store.LoadDay(DateTime.Today).Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void TheScreenOfItsOwn_CanBeDrawnIntoAPicture_WithTheTextAndFlowers()
        {
            var content = TestContent.LoadShipped();
            var cameraObject = new GameObject("meadow camera");
            var camera = cameraObject.AddComponent<Camera>();
            var viewObject = new GameObject("record");
            var view = viewObject.AddComponent<RecordScreenView>();
            Texture2D picture = null;
            try
            {
                var score = new ScoreModel(32);
                view.Configure(content, score, camera, RecordMount.Offscreen, 0);
                for (var i = 0; i < 5; i++) score.Add(new ScoreMark(10.0 - i, (Part)(3 + i % 3), 2 + i, 0.9f));

                picture = view.RenderPicture(640, 360, 10.0);

                Assert.AreEqual(640, picture.width);
                Assert.AreEqual(360, picture.height);
                Assert.Greater(view.FlowerQuadCount, 5, "five notes and the title icons");
                Assert.Greater(view.TextCount, 3, "title, date, the three nature lines");
                var pixels = picture.GetPixels32();
                var distinct = new HashSet<int>();
                foreach (var c in pixels) distinct.Add((c.r >> 4) << 8 | (c.g >> 4) << 4 | (c.b >> 4));
                Assert.Greater(distinct.Count, 12, "the picture is not one flat colour: the panel, the lines, the text and the flowers are in it");
            }
            finally
            {
                if (picture != null) UnityEngine.Object.DestroyImmediate(picture);
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
