using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// The song session as the app wires it (<see cref="PerformanceRig"/>): the visit's start and end open and close a song, the notes count towards the card, a tick per frame ends the piece at the end of the
    /// last bar, and the sky asks it for the chord of a bar. The hands are real pictures of hands; the songs are the three of the shipped content.
    /// </summary>
    public class SongSessionTests
    {
        const int Fps = 30;
        const int FastFps = 60;
        const float PalmSize = 0.15f;
        const double BarSec = ConductRig.SlotsPerBar * 60.0 / ConductRig.Bpm / ConductRig.SlotsPerBeat;
        const int ShippedBars = 24;
        const double SongSec = ShippedBars * BarSec;
        const double VisitSec = 8.0;
        const double GoneSec = 7.0;

        static readonly string[] Order = { "quiet-meadow", "bright-morning", "auld-lang-syne" };

        static readonly PosedHand[] Nobody = new PosedHand[0];

        static PosedHand Hand(int id, double x, double y, HandPose pose) => PoseHand(id, (float)x, (float)y, PalmSize, pose);

        /// <summary>One open hand going round a circle of a twentieth of the picture (about three palms a second) in the middle of the harp's cell.</summary>
        static PosedHand[] HarpHand(double t)
        {
            var (x, y) = ConductRig.CentreOf(Part.Harp);
            const double Radius = 0.05;
            var omega = 3.0 * PalmSize / Radius;
            return new[] { Hand(0, x + Radius * Math.Cos(omega * t), y + Radius * Math.Sin(omega * t), HandPose.Open) };
        }

        static PosedHand[] NoHands(double t) => Nobody;

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>The harp hand plays until <paramref name="playUntil"/>, then both hands are fists a palm and a third apart, a second later pulled apart to six palms in 0.4 s: the two-fists ending.</summary>
        static Func<double, PosedHand[]> HarpThenFists(double playUntil) => t =>
        {
            if (t < playUntil) return HarpHand(t);
            var spreadAt = playUntil + 1.0;
            var apart = Ramp(t, 1.3, 6.0, spreadAt, spreadAt + 0.4) * PalmSize / 2.0;
            return new[] { Hand(0, 0.5 - apart, 0.6, HandPose.Fist), Hand(1, 0.5 + apart, 0.6, HandPose.Fist) };
        };

        static PerformanceRig Rig(bool cards = false, bool enabled = true)
        {
            var rig = new PerformanceRig();
            if (cards) rig.UseRecorder();   // before the songs, as the recorder is built before them in the app
            rig.UseSongs(enabled);
            return rig;
        }

        static BeatGrid Grid() => new BeatGrid(ConductRig.GridOrigin, ConductRig.Bpm, ConductRig.SlotsPerBeat);

        /// <summary>Seconds into the run of a time on the audio clock.</summary>
        static double RigSec(double audioTime) => audioTime - (ConductRig.GridOrigin - 1.0);

        static long StartSlotOf(PerformanceRig rig, int visit = 0) => Grid().FirstSlotAtOrAfter(rig.SongStarts[visit].audioTime);

        static long BarOf(long slot) => slot / ConductRig.SlotsPerBar;

        /// <summary>Nobody is there until the visit has ended by itself.</summary>
        static void Leave(PerformanceRig rig)
        {
            Assert.IsTrue(rig.RunUntil(Fps, NoHands, () => rig.Experience.State == ExperienceState.Idle, 120.0), "the visit ends when the hands have gone");
        }

        // ---- which song a visit plays -------------------------------------------------------------------------------------------------

        [Test]
        public void Visits_PlaySongsInTurn()
        {
            var rig = Rig(cards: true);
            var expected = new[] { Order[0], Order[1], Order[2], Order[0] };

            for (var i = 0; i < expected.Length; i++)
            {
                rig.Run(VisitSec, Fps, HarpHand);
                Assert.AreEqual(i + 1, rig.SongStarts.Count, $"visit {i + 1} begins a song");
                Assert.AreEqual(expected[i], rig.SongStarts[i].song.Id, $"visit {i + 1}");
                Assert.AreEqual(expected[i], rig.Songs.VisitSong.Id, $"visit {i + 1}: the song of the visit that is going on");
                Leave(rig);
                Assert.AreEqual(i + 1, rig.Cards.Count, $"visit {i + 1} made a card");
                Assert.AreEqual(expected[i], rig.Cards[i].SongId, $"and the card names the song");
                Assert.AreEqual(expected[i], rig.Songs.VisitSong.Id, "the song of the visit that has just ended stays until the next visit begins");
            }
        }

        [Test]
        public void ASongBeginsOnABarLine_AtMostOneBarAfterTheVisitBegins()
        {
            var rig = Rig();

            rig.Run(VisitSec, Fps, HarpHand);

            var barOne = RigSec(rig.SongStarts[0].audioTime);
            Assert.AreEqual(1, rig.Started.Count);
            Assert.That(barOne, Is.GreaterThan(rig.Started[0]), "bar 1 is after the visit began");
            Assert.That(barOne, Is.LessThanOrEqualTo(rig.Started[0] + BarSec + 0.5), "within a bar, give or take the lead of the notes");
            Assert.AreEqual(0, StartSlotOf(rig) % ConductRig.SlotsPerBar, "on a bar line of the grid");
        }

        [Test]
        public void APasserBy_DoesNotUseUpASong()
        {
            var rig = Rig();
            rig.Run(1.0, Fps, NoHands);   // the clock goes on, nobody is there
            var songs = rig.Songs;

            songs.OnSessionStarted(rig.Audio.Now);
            Assert.AreEqual(Order[0], songs.VisitSong.Id);
            songs.OnNote();
            songs.OnNote();
            songs.OnSessionEnded(rig.Audio.Now + 4.0);

            songs.OnSessionStarted(rig.Audio.Now + 10.0);
            Assert.AreEqual(Order[0], songs.VisitSong.Id, "two notes make no card, so the next guest hears the same song from its first bar");
        }

        [TestCase(2, 0)]
        [TestCase(3, 1)]
        public void TheCursorMovesOn_OnlyWithEnoughNotesForACard(int notes, int expectedIndex)
        {
            var rig = Rig();
            rig.Run(1.0, Fps, NoHands);
            var songs = rig.Songs;
            var minNotes = TestContent.LoadShipped().record.minNotes;
            Assert.AreEqual(3, minNotes, "the shipped content asks for three notes");

            songs.OnSessionStarted(rig.Audio.Now);
            for (var i = 0; i < notes; i++) songs.OnNote();
            songs.OnSessionEnded(rig.Audio.Now + 4.0);
            songs.OnSessionStarted(rig.Audio.Now + 10.0);

            Assert.AreEqual(Order[expectedIndex], songs.VisitSong.Id);
        }

        [Test]
        public void NotesOutsideAVisit_AreNotCounted_AndAVisitIsEndedOnce()
        {
            var rig = Rig();
            rig.Run(1.0, Fps, NoHands);
            var songs = rig.Songs;

            songs.OnNote();
            songs.OnNote();
            songs.OnNote();   // before the visit began
            songs.OnSessionStarted(rig.Audio.Now);
            songs.OnNote();
            songs.OnSessionEnded(rig.Audio.Now + 4.0);
            songs.OnNote();
            songs.OnNote();
            songs.OnNote();   // after it ended
            songs.OnSessionEnded(rig.Audio.Now + 5.0);   // a second end of the same visit does nothing
            songs.OnSessionStarted(rig.Audio.Now + 10.0);

            Assert.AreEqual(Order[0], songs.VisitSong.Id, "one note is not a card");
        }

        [Test]
        public void TheCursorWrapsAroundTheBook()
        {
            var rig = Rig();
            rig.Run(1.0, Fps, NoHands);
            var songs = rig.Songs;
            var seen = new List<string>();

            for (var visit = 0; visit < 4; visit++)
            {
                songs.OnSessionStarted(rig.Audio.Now + visit * 100.0);
                seen.Add(songs.VisitSong.Id);
                songs.OnNote(); songs.OnNote(); songs.OnNote();
                songs.OnSessionEnded(rig.Audio.Now + visit * 100.0 + 50.0);
            }

            CollectionAssert.AreEqual(new[] { Order[0], Order[1], Order[2], Order[0] }, seen);
        }

        [Test]
        public void TheLowerRow_PlaysTheChordOfBarOne_BeforeTheSongHasBegun()
        {
            var rig = Rig();
            Func<double, PosedHand[]> marimbaHand = t =>
            {
                var (x, y) = ConductRig.CentreOf(Part.Marimba);
                return new[] { Hand(0, x, y, HandPose.Open) };
            };

            rig.Run(6.0, Fps, marimbaHand);

            var marimba = rig.Played.Where(n => n.Part == Part.Marimba).ToList();
            Assert.IsNotEmpty(marimba);
            var firstNoteAt = rig.NoteTimes[rig.Played.FindIndex(n => n.Part == Part.Marimba)];
            Assert.That(firstNoteAt, Is.LessThan(RigSec(rig.SongStarts[0].audioTime)), "the first note comes before bar 1 (the visit begins up to a bar before it)");
            Assert.IsTrue(marimba.All(n => n.HasMidi), "and it is already a note of the song");
            Assert.That(marimba[0].Midi, Is.EqualTo(69).Or.EqualTo(72).Or.EqualTo(76), "the first note: a tone of the chord of bar 1, A minor (the marimba plays tones 1 to 3 of the chord)");
        }

        [Test]
        public void HandsBackWhileSettling_KeepTheSameSong()
        {
            var rig = Rig(cards: true);
            rig.Run(VisitSec, Fps, HarpHand);
            var song = rig.Songs.VisitSong;

            rig.Run(GoneSec, Fps, NoHands);
            Assert.AreEqual(ExperienceState.Settling, rig.Experience.State, "the hands have been gone long enough to settle (the notes still ring for two seconds, then three more without a hand), not long enough to go idle");
            Assert.IsEmpty(rig.Ended);

            var before = rig.Played.Count;
            rig.Run(4.0, Fps, HarpHand);
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State, "the hand came back to the same visit");
            Assert.AreEqual(1, rig.Started.Count, "no new visit");
            Assert.AreEqual(1, rig.SongStarts.Count, "no new song");
            Assert.AreSame(song, rig.Songs.VisitSong);
            Assert.IsTrue(rig.Played.Skip(before).Any(n => n.HasMidi), "and the melody goes on with the notes of the song");

            Leave(rig);
            Assert.AreEqual(1, rig.Cards.Count, "one visit, one card");
            Assert.AreEqual(Order[0], rig.Cards[0].SongId);
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.AreEqual(Order[1], rig.Songs.VisitSong.Id, "the song was used up once, not twice");
        }

        [Test]
        public void TheHandsLeaveMidSong_TheCardNamesTheSong_AndTheNextVisitBeginsTheNextSongFromItsFirstBar()
        {
            var rig = Rig(cards: true);
            rig.Run(VisitSec, Fps, HarpHand);
            Leave(rig);
            var firstEndsAt = rig.Ended[0];

            rig.Run(VisitSec, Fps, HarpHand);

            Assert.AreEqual(Order[0], rig.Cards[0].SongId);
            Assert.AreEqual(Order[1], rig.SongStarts[1].song.Id);
            Assert.That(RigSec(rig.SongStarts[1].audioTime), Is.GreaterThan(firstEndsAt), "a new clock for the new song");
            Assert.IsEmpty(rig.Cuts, "no ending by a song");
        }

        // ---- the end of a song -------------------------------------------------------------------------------------------------------------

        [Test]
        public void SongEnd_EndsThePieceOnce_AndTheCardNamesTheSong()
        {
            var rig = Rig(cards: true);
            rig.Run(VisitSec, Fps, HarpHand);
            var barOne = rig.SongStarts[0].audioTime;
            var endAt = Grid().SlotTime(StartSlotOf(rig) + (long)ShippedBars * ConductRig.SlotsPerBar);
            Assert.AreEqual(barOne + SongSec, endAt, 1e-6, "24 bars of 2.5 s after bar 1");
            Assert.IsEmpty(rig.Cuts, "not before the last bar is over");

            Assert.IsTrue(rig.RunUntil(Fps, HarpHand, () => rig.Cuts.Count > 0, SongSec + 5.0), "the song ends the piece");
            rig.Run(0.5, Fps, HarpHand);   // the open hand stays up, but a new visit waits for the restart lock

            Assert.AreEqual(1, rig.Cuts.Count, "the piece was ended once");
            Assert.That(rig.CutAudioTimes[0], Is.InRange(endAt, endAt + PerformanceRig.FrameSec + 1e-6), "in the frame in which the last bar was over");
            Assert.AreEqual(PieceEndReason.SongEnd, rig.Director.LastEndReason);
            Assert.AreEqual(0, rig.Director.CutCount, "a song's end is not a cut with the hands");
            Assert.AreEqual(1, rig.Ended.Count, "the visit ended");
            Assert.AreEqual(rig.Cuts[0], rig.Ended[0], 1e-9, "in the same frame");
            Assert.AreEqual(new[] { rig.Settings.FadeSec }, rig.Audio.FadeOutAllCalls, "the voices fade out once");
            Assert.AreEqual(0f, rig.Energy.Value, 1e-6f);
            Assert.AreEqual(1, rig.Cards.Count, "one card");
            Assert.AreEqual("잔잔한 들판", rig.Cards[0].SongTitle);
            Assert.AreEqual(Order[0], rig.Cards[0].SongId);
            Assert.IsEmpty(rig.NoteTimes.Where(t => t > rig.Cuts[0] - 1e-9), "not a note after the end");
            Assert.AreEqual(1, rig.Started.Count, "no second visit yet");
        }

        [Test]
        public void SongEnd_EndsThePiece_WhateverTheOrderOfTheRecorderAndTheSession()
        {
            var rig = new PerformanceRig();
            rig.UseSongs();
            rig.UseRecorder();   // the recorder hears about the end after the session this time
            rig.Run(VisitSec, Fps, HarpHand);

            Assert.IsTrue(rig.RunUntil(Fps, HarpHand, () => rig.Cuts.Count > 0, SongSec + 5.0));

            Assert.AreEqual(1, rig.Cards.Count);
            Assert.AreEqual(Order[0], rig.Cards[0].SongId, "the song of the visit is on the card although the session moved on to the next one");
        }

        [Test]
        public void AfterTheSongEnd_AnOpenHandStartsTheNextSong()
        {
            var rig = Rig(cards: true);
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.IsTrue(rig.RunUntil(Fps, HarpHand, () => rig.Cuts.Count > 0, SongSec + 5.0));
            var endedAt = rig.Cuts[0];

            Assert.IsTrue(rig.RunUntil(Fps, HarpHand, () => rig.Started.Count == 2, 10.0), "the open hand starts a new visit after the restart lock");
            rig.Run(VisitSec, Fps, HarpHand);

            Assert.That(rig.Started[1], Is.GreaterThanOrEqualTo(endedAt + rig.Settings.RestartLockSec - 1e-6));
            Assert.AreEqual(Order[1], rig.SongStarts[1].song.Id, "the song that was played to its end is used up");
            Assert.AreEqual(1, rig.Cuts.Count, "and the new song does not end at once");
            Assert.IsTrue(rig.Played.Where((n, i) => rig.NoteTimes[i] > rig.Started[1]).Any(n => n.HasMidi), "it plays");
        }

        [Test]
        public void SongEnd_AndTwoFists_SameFrame_EndOnce()
        {
            // how long the fists take, from the time the hand leaves the harp, to end a piece (measured far from the end of the song)
            const double ProbePlayUntil = 20.0;
            var probe = Rig();
            probe.Run(ProbePlayUntil + 8.0, FastFps, HarpThenFists(ProbePlayUntil));
            Assert.AreEqual(1, probe.Cuts.Count, "the fists end the piece");
            Assert.AreEqual(PieceEndReason.Hands, probe.Director.LastEndReason);
            var delay = probe.Cuts[0] - ProbePlayUntil;

            // then the fists are timed to end the piece around the frame in which the last bar is over, a few frames either side
            var sameFrame = 0;
            for (var shift = -4; shift <= 4; shift++)
            {
                var rig = Rig(cards: true);
                var playUntil = double.PositiveInfinity;
                Func<double, PosedHand[]> script = t => HarpThenFists(playUntil)(t);
                rig.Run(3.0, FastFps, script);
                var endSlot = StartSlotOf(rig) + (long)ShippedBars * ConductRig.SlotsPerBar;
                var endAt = Grid().SlotTime(endSlot);
                playUntil = Math.Round((RigSec(endAt) - delay) * FastFps) / FastFps + shift / (double)FastFps;
                rig.Run(RigSec(endAt) + 6.0 - rig.Elapsed, FastFps, script);

                var label = $"fists {shift} frames from the plan";
                Assert.AreEqual(1, rig.Started.Count, label);
                Assert.AreEqual(1, rig.Ended.Count, label + ": the visit ended once");
                Assert.AreEqual(1, rig.Cards.Count, label + ": one card");
                Assert.AreEqual(Order[0], rig.Cards[0].SongId, label);
                Assert.AreEqual(rig.Cuts.Count, rig.Audio.FadeOutAllCalls.Count, label + ": every end fades the voices once");
                if (rig.Cuts.Count == 1 && rig.Director.LastEndReason == PieceEndReason.Hands && rig.CutAudioTimes[0] >= endAt)
                {
                    sameFrame++;
                    Assert.That(rig.CutAudioTimes[0], Is.LessThan(endAt + PerformanceRig.FrameSec + 1e-6), label + ": the frame of the last bar's end");
                    Assert.AreEqual(1, rig.Director.CutCount, label);
                }
            }
            Assert.AreEqual(1, sameFrame, "exactly one of the timings put the two ends into one frame, and that one ended the piece once");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SongEnd_AndTheHands_AskInEitherOrder_OneEnd(bool handsFirst)
        {
            var rig = Rig(cards: true);
            rig.Run(VisitSec, Fps, HarpHand);
            var endAt = Grid().SlotTime(StartSlotOf(rig) + (long)ShippedBars * ConductRig.SlotsPerBar);
            rig.Run(RigSec(endAt) - 0.5 - rig.Elapsed, Fps, HarpHand);
            var now = endAt + 0.001;

            var first = handsFirst ? rig.Director.EndPiece(now, PieceEndReason.Hands) : rig.Songs.Tick(now);
            var second = handsFirst ? rig.Songs.Tick(now) : rig.Director.EndPiece(now, PieceEndReason.Hands);

            Assert.IsTrue(first, "the first to ask ends the piece");
            Assert.IsFalse(second, "the second finds it ended");
            Assert.AreEqual(1, rig.Cuts.Count);
            Assert.AreEqual(1, rig.Ended.Count);
            Assert.AreEqual(1, rig.Audio.FadeOutAllCalls.Count);
            Assert.AreEqual(1, rig.Cards.Count);
            Assert.AreEqual(handsFirst ? PieceEndReason.Hands : PieceEndReason.SongEnd, rig.Director.LastEndReason);
            Assert.IsFalse(rig.Songs.Tick(now + 1.0), "and it does not end again later");
        }

        [Test]
        public void TheTick_DoesNothing_UntilTheLastBarIsOver_OrWithoutASong()
        {
            var rig = Rig();
            Assert.IsFalse(rig.Songs.Tick(1e9), "no visit, no song");

            rig.Run(VisitSec, Fps, HarpHand);
            var endAt = Grid().SlotTime(StartSlotOf(rig) + (long)ShippedBars * ConductRig.SlotsPerBar);

            Assert.IsFalse(rig.Songs.Tick(endAt - 0.001), "just before");
            Assert.IsEmpty(rig.Cuts);
            Assert.IsTrue(rig.Songs.Tick(endAt), "on the line");
        }

        [Test]
        public void TheHandsEnd_BeforeTheSongEnd_TheSongDoesNotEndAgain()
        {
            var rig = Rig(cards: true);
            const double PlayUntil = 20.0;
            var script = HarpThenFists(PlayUntil);
            rig.Run(SongSec + 10.0, FastFps, script);

            Assert.AreEqual(1, rig.Cuts.Count, "the fists ended the piece, the end of the song found no visit");
            Assert.AreEqual(PieceEndReason.Hands, rig.Director.LastEndReason);
            Assert.AreEqual(1, rig.Cards.Count);
            Assert.AreEqual(Order[0], rig.Cards[0].SongId);
        }

        // ---- the switch ------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Switch_AppliesFromTheNextVisit()
        {
            var rig = Rig(cards: true);
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.IsTrue(rig.Player.HasSong);

            rig.Songs.SetEnabled(false);   // off in the middle of the song
            Assert.IsFalse(rig.Songs.Enabled);
            Assert.IsTrue(rig.Player.HasSong, "the song that is playing is played on");
            Assert.AreEqual(Order[0], rig.Songs.VisitSong.Id);
            Assert.IsTrue(rig.RunUntil(Fps, HarpHand, () => rig.Cuts.Count > 0, SongSec + 5.0), "to its end");
            Assert.AreEqual(PieceEndReason.SongEnd, rig.Director.LastEndReason);
            Assert.AreEqual(Order[0], rig.Cards.Single().SongId);

            // the next visit is free play
            Assert.IsTrue(rig.RunUntil(Fps, HarpHand, () => rig.Started.Count == 2, 10.0));
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.AreEqual(1, rig.SongStarts.Count, "no song begins");
            Assert.IsNull(rig.Songs.VisitSong);
            Assert.IsTrue(rig.Songs.VisitLabel.IsEmpty);
            Assert.IsFalse(rig.Player.HasSong);
            Assert.IsTrue(rig.Played.Where((n, i) => rig.NoteTimes[i] > rig.Started[1]).All(n => !n.HasMidi), "free play: every note sounds by its step");
            Leave(rig);
            Assert.AreEqual(2, rig.Cards.Count);
            Assert.IsTrue(string.IsNullOrEmpty(rig.Cards[1].SongId), "a card of free play names no song");
            Assert.IsTrue(string.IsNullOrEmpty(rig.Cards[1].SongTitle));

            // on again while nobody is there: the song is ready at once, and the next visit plays it
            rig.Songs.SetEnabled(true);
            Assert.IsTrue(rig.Player.HasSong, "prepared at once, the lower row already has the chord of bar 1");
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.AreEqual(2, rig.SongStarts.Count);
            Assert.AreEqual(Order[1], rig.SongStarts[1].song.Id, "the song that was played to its end is used up, free play uses none");
        }

        [Test]
        public void Switch_OnInTheMiddleOfAFreePlayVisit_WaitsForTheNextVisit()
        {
            var rig = Rig(enabled: false);
            Assert.IsFalse(rig.Player.HasSong);
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.IsEmpty(rig.SongStarts);

            rig.Songs.SetEnabled(true);

            Assert.IsFalse(rig.Player.HasSong, "a visit is going on: it stays free play");
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.IsEmpty(rig.SongStarts);
            Leave(rig);
            Assert.IsTrue(rig.Player.HasSong, "the visit is over: the song for the next one is ready");
            rig.Run(VisitSec, Fps, HarpHand);
            Assert.AreEqual(Order[0], rig.SongStarts.Single().song.Id);
        }

        [Test]
        public void Switch_OffWhileNobodyIsThere_TakesTheSongBack()
        {
            var rig = Rig();
            Assert.IsTrue(rig.Player.HasSong, "a song is ready from the start");

            rig.Songs.SetEnabled(false);

            Assert.IsFalse(rig.Player.HasSong);
        }

        [Test]
        public void SongsOff_OrNoSongs_LeaveFreePlayAsItWas()
        {
            var plain = new PerformanceRig();
            var off = Rig(enabled: false);
            var empty = new PerformanceRig();
            empty.UseSongs(true, new SongScore[0]);
            Func<double, PosedHand[]> hands = t => HarpHand(t).Concat(new[] { Hand(1, 0.5, 0.75, HandPose.Open) }).ToArray();

            foreach (var rig in new[] { plain, off, empty }) rig.Run(25.0, Fps, hands);

            Assert.IsNotEmpty(plain.Played);
            AssertSameNotes(plain.Played, off.Played, "songs switched off");
            AssertSameNotes(plain.Played, empty.Played, "no songs at all");
            foreach (var rig in new[] { off, empty })
            {
                Assert.IsEmpty(rig.SongStarts);
                Assert.IsNull(rig.Songs.VisitSong);
                Assert.IsFalse(rig.Player.HasSong);
                Assert.IsEmpty(rig.Cuts);
                Assert.IsNull(rig.Songs.SkyStepsForBar(5));
            }
        }

        static void AssertSameNotes(IReadOnlyList<NoteIntent> expected, IReadOnlyList<NoteIntent> actual, string label)
        {
            Assert.AreEqual(expected.Count, actual.Count, label + ": as many notes");
            for (var i = 0; i < expected.Count; i++)
            {
                var a = expected[i];
                var b = actual[i];
                var same = a.Part == b.Part && a.PitchStep == b.PitchStep && a.Velocity == b.Velocity && a.RequestedAt.Equals(b.RequestedAt) && a.Midi == b.Midi && a.EnergyWeight == b.EnergyWeight;
                Assert.IsTrue(same, $"{label}: note {i} is the same");
            }
        }

        [Test]
        public void StrikeStyle_HasNoSong()
        {
            var rig = new PerformanceRig("strike");
            rig.UseSongs();
            Assert.IsFalse(rig.Player.IsConducting);
            Assert.IsFalse(rig.Player.HasSong, "nothing is prepared for the strikes");
            rig.Run(1.0, Fps, NoHands);

            rig.Songs.OnSessionStarted(rig.Audio.Now);
            rig.Run(SongSec + 10.0, Fps, NoHands);

            Assert.IsNull(rig.Songs.VisitSong);
            Assert.IsTrue(rig.Songs.VisitLabel.IsEmpty);
            Assert.IsEmpty(rig.SongStarts);
            Assert.IsFalse(rig.Player.HasSong);
            Assert.IsNull(rig.Songs.SkyStepsForBar(3), "the sky keeps its own chords");
            Assert.IsEmpty(rig.Cuts, "a latency measurement is never cut by the end of a song");
            Assert.IsEmpty(rig.Audio.FadeOutAllCalls);
            Assert.IsTrue(rig.Songs.Enabled, "the switch itself is as it was set");

            rig.Songs.SetEnabled(false);
            rig.Songs.SetEnabled(true);
            Assert.IsFalse(rig.Player.HasSong, "and turning it on does not change that");
        }

        // ---- the sky -------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void SkyChord_FollowsTheSong()
        {
            var rig = Rig();
            Assert.IsNull(rig.Songs.SkyStepsForBar(7), "no visit: the sky plays its own chords");
            rig.Run(VisitSec, Fps, HarpHand);
            var firstBar = BarOf(StartSlotOf(rig));

            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, rig.Songs.SkyStepsForBar(firstBar), "bar 1, A minor: la do mi");
            CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Songs.SkyStepsForBar(firstBar + 1), "bar 2, F: only la and do are steps of the scale");
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, rig.Songs.SkyStepsForBar(firstBar + 2), "bar 3, C: do mi sol");
            CollectionAssert.AreEqual(new[] { 2, 4 }, rig.Songs.SkyStepsForBar(firstBar + 3), "bar 4, G: re and sol (si is not in the scale)");
            CollectionAssert.AreEqual(rig.Songs.SkyStepsForBar(firstBar), rig.Songs.SkyStepsForBar(firstBar - 1), "the bar before bar 1 is asked for bar 1's chord");
            CollectionAssert.AreEqual(rig.Songs.SkyStepsForBar(firstBar), rig.Songs.SkyStepsForBar(firstBar - 1000));
            var last = rig.Songs.SkyStepsForBar(firstBar + ShippedBars - 1);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, last, "the last bar is the tonic");
            CollectionAssert.AreEqual(last, rig.Songs.SkyStepsForBar(firstBar + ShippedBars), "the bar after the end uses the last bar's chord");
            CollectionAssert.AreEqual(last, rig.Songs.SkyStepsForBar(firstBar + 100_000));
            CollectionAssert.AreEqual(last, rig.Songs.SkyStepsForBar(long.MaxValue), "never throws");
            CollectionAssert.AreEqual(rig.Songs.SkyStepsForBar(firstBar), rig.Songs.SkyStepsForBar(long.MinValue), "never throws");

            Leave(rig);
            Assert.IsNull(rig.Songs.SkyStepsForBar(firstBar + 1), "the visit is over: the sky is back to its own chords");
        }

        [Test]
        public void SkyChord_FarBeforeAndFarAfter_AreTheFirstAndTheLastBar()
        {
            // a song whose first chord (A minor: la do mi) differs from its last (F: la do), unlike the shipped songs which all end where they began
            var book = SongBook.Build(SongFixtures.ContentWith(SongFixtures.ValidSong("two-bars"))).Songs;
            Assert.AreEqual(1, book.Count);
            var rig = new PerformanceRig();
            rig.UseSongs(true, book);
            rig.Run(2.0, Fps, HarpHand);
            var firstBar = BarOf(StartSlotOf(rig));

            foreach (var bar in new[] { long.MinValue, firstBar - 1000, firstBar - 1, firstBar })
                CollectionAssert.AreEqual(new[] { 0, 1, 3 }, rig.Songs.SkyStepsForBar(bar), $"bar {bar}");
            foreach (var bar in new[] { firstBar + 1, firstBar + 2, firstBar + 1000, long.MaxValue })
                CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Songs.SkyStepsForBar(bar), $"bar {bar}");
        }

        [Test]
        public void SkyChord_ACallerCannotChangeWhatTheNextOneGets()
        {
            var rig = Rig();
            rig.Run(VisitSec, Fps, HarpHand);
            var bar = BarOf(StartSlotOf(rig));

            rig.Songs.SkyStepsForBar(bar)[0] = 99;

            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, rig.Songs.SkyStepsForBar(bar));
        }

        [Test]
        public void SkyChord_InTheConductor_PlaysTheChordOfTheBarOfTheSong()
        {
            var rig = Rig();
            rig.Run(VisitSec, Fps, HarpHand);
            var content = TestContent.LoadShipped();
            var sky = new NatureLayerConductor(NatureSettingsFactory.Rules(content.nature.rules, content.song.slotsPerBeat), ConductRig.SlotsPerBar, content.song.pitchSteps, content.nature.seed);
            sky.SetChordSource(rig.Songs.SkyStepsForBar);
            var firstSlot = StartSlotOf(rig);

            var notes = new List<NatureNote>();
            sky.Plan(firstSlot, firstSlot + 4L * ConductRig.SlotsPerBar, 2, notes);

            int[] SkyAt(int bar) => notes.Where(n => n.Layer == NatureLayer.Sky && n.Slot == firstSlot + bar * (long)ConductRig.SlotsPerBar).Select(n => n.PitchStep).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, SkyAt(0));
            CollectionAssert.AreEqual(new[] { 0, 1 }, SkyAt(1), "F, where the rules would have played C E G");
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, SkyAt(2));
            CollectionAssert.AreEqual(new[] { 2, 4 }, SkyAt(3));
        }

        // ---- the surface ---------------------------------------------------------------------------------------------------------------------

        [Test]
        public void ThePlayerKnowsItsBar()
        {
            Assert.AreEqual(ConductRig.SlotsPerBar, new PerformanceRig().Player.SlotsPerBar);
            Assert.AreEqual(0, new ConductPlayer(() => 0.0).SlotsPerBar, "before it is configured");
        }

        [Test]
        public void Constructor_RejectsNonsense()
        {
            var rig = new PerformanceRig();
            var scale = TestContent.AMinorPentatonic();
            var songs = SongBook.Build(TestContent.LoadShipped()).Songs;

            Assert.Throws<ArgumentNullException>(() => new SongSession(null, rig.Player, rig.Director, scale, 69, 3, true));
            Assert.Throws<ArgumentNullException>(() => new SongSession(songs, null, rig.Director, scale, 69, 3, true));
            Assert.Throws<ArgumentNullException>(() => new SongSession(songs, rig.Player, null, scale, 69, 3, true));
            Assert.Throws<ArgumentNullException>(() => new SongSession(songs, rig.Player, rig.Director, null, 69, 3, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SongSession(songs, rig.Player, rig.Director, scale, 69, 0, true));
            Assert.Throws<ArgumentException>(() => new SongSession(new SongScore[] { songs[0], null }, rig.Player, rig.Director, scale, 69, 3, true));
        }

        [Test]
        public void TheSongsAreCopied_SoTheListCannotChangeUnderTheSession()
        {
            var rig = new PerformanceRig();
            var list = new List<SongScore>(SongBook.Build(TestContent.LoadShipped()).Songs);
            var songs = rig.UseSongs(true, list);
            list.Clear();
            rig.Run(1.0, Fps, NoHands);

            songs.OnSessionStarted(rig.Audio.Now);

            Assert.AreEqual(Order[0], songs.VisitSong.Id);
        }
    }
}
