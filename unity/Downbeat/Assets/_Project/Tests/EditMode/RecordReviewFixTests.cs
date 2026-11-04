using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Record;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Downbeat.Tests
{
    /// <summary>Fixes from the review of the idle motes: a launch option that must not hide the second screen, and a painter that makes no garbage per frame, cards included.</summary>
    public class RecordSourceLaunchTests
    {
        static LaunchArgs Args(params string[] args) => LaunchArgs.Parse(args);

        [Test]
        public void TheRecordScreenIsFilmed_OnlyWhenThereIsARecorder_ThatIsWhenRecordDirIsGiven()
        {
            Assert.IsTrue(RunRecorder.WantsRecordScreen(Args("--record-dir", "somewhere", "--record-source", "record")));
            Assert.IsFalse(RunRecorder.WantsRecordScreen(Args("--record-source", "record")), "without --record-dir there is no recorder: the option must not switch the screen to a picture of its own");
            Assert.IsFalse(RunRecorder.WantsRecordScreen(Args("--record-dir", "somewhere")), "and without the option the meadow is filmed");
            Assert.IsFalse(RunRecorder.WantsRecordScreen(Args("--record-dir", "somewhere", "--record-source", "meadow")));
        }

        [Test]
        public void TheOptionWithoutARecorder_IsReportedAsIgnored_NotSilentlyObeyed()
        {
            Assert.IsTrue(RunRecorder.RecordSourceIgnored(Args("--record-source", "record")));
            Assert.IsFalse(RunRecorder.RecordSourceIgnored(Args("--record-dir", "somewhere", "--record-source", "record")));
            Assert.IsFalse(RunRecorder.RecordSourceIgnored(Args("--record-dir", "somewhere")));
            Assert.IsFalse(RunRecorder.RecordSourceIgnored(Args()));
        }
    }

    public class RecordPainterGarbageTests
    {
        static RecordPalette Palette()
        {
            var p = new RecordPalette { Background = new Color(0.02f, 0.02f, 0.1f), BackgroundBottom = new Color(0.05f, 0.04f, 0.15f), Ink = Color.white, Muted = Color.gray, Staff = Color.blue, Accent = Color.yellow, Card = new Color(0.05f, 0.05f, 0.2f) };
            for (var i = 0; i < Parts.Count; i++) p.FlowerGlow[i] = Color.white;
            for (var i = 0; i < NatureLayers.Count; i++) p.NatureLine[i] = Color.green;
            return p;
        }

        static SessionCard Card(int minute) =>
            new SessionCard($"20261030-15{minute:00}00", new DateTime(2026, 10, 30, 15, minute, 0), 71.6, 12, 3,
                Enumerable.Range(0, 12).Select(i => new ScoreMark(i * 6.0, (Part)(i % Parts.Count), i % 10, 0.8f)).ToList(), "x.png");

        [Test]
        public void AFrameOfTheScreen_WithSixCards_AllocatesNothing()
        {
            var content = TestContent.LoadShipped();
            var painter = new RecordPainter(content.record, Palette(), content.song.pitchSteps);
            var layout = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, content.record.cardsShown);
            var builders = new RecordBuilders();
            var texts = new List<TextCommand>(32);
            var thumbnails = new List<ThumbnailCommand>(12);
            var score = new ScoreModel(32);
            for (var i = 0; i < 6; i++) score.Add(new ScoreMark(99.0 - i, (Part)(i % Parts.Count), 2 + i, 0.8f));
            var cards = Enumerable.Range(0, 6).Select(i => new RecordCardView(Card(10 + i), null)).ToList();
            var today = new DateTime(2026, 10, 30);

            for (var i = 0; i < 300; i++) painter.Paint(layout, 100.0 + i * 0.016, i * 0.016, score, cards, today, 1920f, 1080f, builders, texts, thumbnails, i % 200 < 100);   // warm up: the lists reach their size

            Assert.That(() =>
            {
                for (var i = 0; i < 200; i++) painter.Paint(layout, 105.0 + i * 0.016, 5.0 + i * 0.016, score, cards, today, 1920f, 1080f, builders, texts, thumbnails, i % 100 < 50);
            }, Is.Not.AllocatingGCMemory(), "200 frames of the score, the layers, the motes and six cards with their times");
            Assert.AreEqual(6, texts.Count(t => t.Slot.StartsWith("time")), "the cards were painted, with their times");
        }

        [Test]
        public void AFrameOfTheScreen_WithSixCardsThatNameTheirSong_AllocatesNothing()
        {
            var content = TestContent.LoadShipped();
            var painter = new RecordPainter(content.record, Palette(), content.song.pitchSteps);
            var layout = RecordLayout.Compute(DisplayMode.Dual, 1920f, 1080f, content.record.cardsShown);
            var builders = new RecordBuilders();
            var texts = new List<TextCommand>(32);
            var thumbnails = new List<ThumbnailCommand>(12);
            var score = new ScoreModel(32);
            for (var i = 0; i < 6; i++) score.Add(new ScoreMark(105.0 + i * 0.1, (Part)(i % Parts.Count), 2 + i, 0.8f));   // marks that stay in the window for the whole test
            var cards = Enumerable.Range(0, 6).Select(i => new RecordCardView(CardWithSong(10 + i), null)).ToList();
            var today = new DateTime(2026, 11, 1);

            for (var i = 0; i < 300; i++) painter.Paint(layout, 110.0 + i * 0.016, i * 0.016, score, cards, today, 1920f, 1080f, builders, texts, thumbnails, i % 200 < 100);

            Assert.That(() =>
            {
                for (var i = 0; i < 200; i++) painter.Paint(layout, 110.0 + i * 0.016, 5.0 + i * 0.016, score, cards, today, 1920f, 1080f, builders, texts, thumbnails, i % 100 < 50);
            }, Is.Not.AllocatingGCMemory(), "200 frames of the screen with six cards, each with the title of its song");
            Assert.AreEqual(6, texts.Count(t => t.Slot.StartsWith("song")), "the song lines were painted");
        }

        static SessionCard CardWithSong(int minute) =>
            new SessionCard($"20261101-15{minute:00}00", new DateTime(2026, 11, 1, 15, minute, 0), 60.0, 12, 3,
                Enumerable.Range(0, 12).Select(i => new ScoreMark(i * 5.0, (Part)(i % Parts.Count), i % 10, 0.8f)).ToList(), "x.png", "quiet-meadow", "잔잔한 들판");

        [Test]
        public void TheLabelsOfACard_AreMadeOnce_NotEveryFrame()
        {
            var content = TestContent.LoadShipped();
            var view = new RecordCardView(Card(42), null);

            var first = view.TimeLabel(content.record.text);
            var detail = view.DetailLabel(content.record.text);

            Assert.AreEqual("오후 3:42", first);
            Assert.AreSame(first, view.TimeLabel(content.record.text), "the same string comes back: nothing is formatted again");
            Assert.AreSame(detail, view.DetailLabel(content.record.text));
            StringAssert.Contains("1분 12초", detail);
            StringAssert.Contains("자연 3", detail);
        }

        [Test]
        public void AChangedFormat_GivesAFreshLabel()
        {
            var content = TestContent.LoadShipped();
            var view = new RecordCardView(Card(42), null);
            var first = view.TimeLabel(content.record.text);

            content.record.text.timeFormat = "{1}:{2:00} {0}";

            Assert.AreEqual("3:42 오후", view.TimeLabel(content.record.text));
            Assert.AreNotEqual(first, view.TimeLabel(content.record.text));
        }
    }
}
