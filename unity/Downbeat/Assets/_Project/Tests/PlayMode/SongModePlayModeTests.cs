using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>
    /// Song mode in the real scene, with the recorded demo hands as the camera: the upper row plays the score of the song on its slots, the title shows when the first bar begins, the last bar ends the piece with the
    /// word and a card that names the song, the next visit takes the next song, and with the switch off (or the strike style) it is the free play of before. These tests name <c>--songs</c> themselves:
    /// the PlayMode assembly runs with <c>--songs off</c> unless a test says otherwise (<c>PlayModeDefaults</c>).
    /// </summary>
    public class SongModePlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float PlaySec = 12f;
        const double SlotSec = 60.0 / 96.0 / 4.0;
        const int SlotsPerBar = 16;
        const int UpperBars = 8;
        const double SongEndWithinSec = 70.0;   // from the moment the app runs: the visit begins within about 3.5 s, the song one bar line later and lasts 60 s
        const string FirstSongId = "quiet-meadow";
        const string FirstSongTitle = "잔잔한 들판";
        const string SecondSongId = "bright-morning";

        string _dir;
        string _cardsDir;
        string _settingsFile;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "downbeat-songs-" + Guid.NewGuid().ToString("N"));
            _cardsDir = Path.Combine(_dir, "cards");
            _settingsFile = Path.Combine(_dir, "settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static ContentDto ShippedContent() =>
            JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        IEnumerator Start(string[] options, Action<DownbeatApp, IAudioEngine> ready)
        {
            var args = new List<string> { "Downbeat", "--input", "replay", "--mute", "--cards-dir", _cardsDir, "--settings-file", _settingsFile };
            args.AddRange(options);
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var scope = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            ready(app, scope.Container.Resolve<IAudioEngine>());
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(condition(), "timed out waiting for " + what);
        }

        /// <summary>What the app logged while a test ran (<c>Debug.Log</c> of the app; the errors among them are the ones that must not be there).</summary>
        sealed class Collected
        {
            public readonly List<string> Logs = new List<string>();
            public readonly List<string> Errors = new List<string>();

            public Application.LogCallback Callback => (message, trace, type) =>
            {
                Logs.Add(message);
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(type + ": " + message);
            };
        }

        static bool IsUpperRow(Part part) => Parts.IsSky(part);

        [UnityTest]
        public IEnumerator SongMode_UpperHand_PlaysTheScoreSlots()
        {
            var content = ShippedContent();
            var book = SongBook.Build(content);
            var collected = new Collected();
            var onLog = collected.Callback;
            Application.logMessageReceived += onLog;
            try
            {
                DownbeatApp app = null;
                IAudioEngine audio = null;
                yield return Start(new[] { "--songs", "on" }, (a, e) => { app = a; audio = e; });
                Assert.IsNotNull(app.Songs, "the shipped content has songs");
                var notes = new List<(ScheduledNote Note, SampleResolution Sound)>();
                app.NoteScheduled += (note, sound) => notes.Add((note, sound));
                var starts = new List<(SongScore Song, double BarOne)>();
                app.Songs.SongStarted += (song, barOne) => starts.Add((song, barOne));
                var titleSeen = new List<(string Text, double Heard)>();
                var titleShownAt = float.NaN;
                var titleGoneAt = float.NaN;

                yield return WaitFor(() => starts.Count == 1, 15f, "the song to begin with the visit");
                var score = starts[0].Song;
                var barOne = starts[0].BarOne;
                Assert.AreEqual(FirstSongId, score.Id, "an app that was just started begins with the first song of the book");
                Assert.AreEqual(book.Songs[0].Id, score.Id);
                var songEnd = barOne + UpperBars * SlotsPerBar * SlotSec;
                while (audio.Now < songEnd + 0.5)
                {
                    var showing = app.SongTitle != null && app.SongTitle.IsShowing;
                    if (showing && titleSeen.Count == 0)
                    {
                        titleSeen.Add((app.SongTitle.Text, audio.VisualNow));
                        titleShownAt = Time.realtimeSinceStartup;
                    }
                    if (!showing && titleSeen.Count == 1 && float.IsNaN(titleGoneAt)) titleGoneAt = Time.realtimeSinceStartup;
                    yield return null;
                }

                Assert.AreEqual(1, titleSeen.Count, "the title of the song showed");
                Assert.AreEqual(FirstSongTitle, titleSeen[0].Text);
                Assert.GreaterOrEqual(titleSeen[0].Heard, barOne - 0.05, "and not before the first bar is heard");
                Assert.LessOrEqual(titleSeen[0].Heard, barOne + 0.3, "but when it is");
                var shownFor = titleGoneAt - titleShownAt;
                Assert.AreEqual(content.experience.songTitleSec, shownFor, 0.6, "for the time of the content (experience.songTitleSec), and then it is gone");
                var overlay = UnityEngine.Object.FindFirstObjectByType<Downbeat.Meadow.MeadowStage>().Overlay;
                Assert.AreSame(overlay, app.SongTitle.Layer, "on the overlay layer of the meadow");
                Assert.AreEqual(overlay.SortingOrderBase + Downbeat.Meadow.SongTitleView.SortingOrderAboveLayerBase + 1, app.SongTitle.SortingOrder, "just over the word of the end");

                var upper = notes.Where(n => IsUpperRow(n.Note.Part)).ToList();
                Assert.GreaterOrEqual(upper.Count, 8, $"the demo hands keep a hand in the upper row: {upper.Count} upper notes in the first {UpperBars} bars");
                var samples = content.instruments.ToDictionary(i => i.part, i => i.samples);
                foreach (var (note, sound) in upper)
                {
                    Assert.GreaterOrEqual(note.AudioTime, barOne - SlotSec * 0.5, $"{note.Part} slot {note.Slot}: the upper row is silent until the first bar");
                    if (note.AudioTime >= songEnd) continue;

                    var bar = (int)Math.Floor((note.AudioTime - barOne) / (SlotsPerBar * SlotSec));
                    var slotInBar = (int)(note.Slot % SlotsPerBar);
                    var where = $"{note.Part} slot {note.Slot} (bar {bar + 1}, slot {slotInBar}, midi {note.Midi})";
                    Assert.IsFalse(note.WasPushed, where + ": a note of the score is never moved to another slot");
                    Assert.IsTrue(score.Bars[bar].TryMelodyAt(slotInBar, out var midi), where + ": the score has a note on this slot");
                    Assert.AreEqual(midi + score.OctaveFor(note.Part), note.Midi, where + ": the pitch is the score's, plus the octave of the instrument");
                    Assert.AreEqual(note.Midi, sound.TargetMidi, where + ": the sound is made for that MIDI, not for the step of the scale the picture uses");
                    var sample = samples[Parts.Name(note.Part)].First(s => s.id == sound.SampleId);
                    Assert.AreEqual(note.Midi, sample.soundingMidi + sound.Semitones, where + ": the sample is shifted from its own pitch to it");
                }
                Assert.IsEmpty(collected.Errors, "no error was logged: " + string.Join(" | ", collected.Errors));
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator SongMode_EndsWithTheWordAndACardNamingTheSong()
        {
            var collected = new Collected();
            var onLog = collected.Callback;
            Application.logMessageReceived += onLog;
            try
            {
                DownbeatApp app = null;
                yield return Start(new[] { "--songs", "on" }, (a, e) => app = a);
                var runningSince = Time.realtimeSinceStartup;
                var starts = new List<(SongScore Song, double BarOne)>();
                app.Songs.SongStarted += (song, barOne) => starts.Add((song, barOne));
                var cuts = new List<float>();
                var ended = new List<float>();
                var atTheEnd = default((ExperienceState State, int Cards, bool Word, string WordText, int CutCount));
                app.Experience.SessionEnded += _ => ended.Add(Time.realtimeSinceStartup);
                app.PerformanceCutoff += _ =>
                {
                    cuts.Add(Time.realtimeSinceStartup);
                    atTheEnd = (app.Experience.State, app.Record.CardsWritten, app.EndWord != null && app.EndWord.IsShowing, app.EndWord?.Text, app.CutoffCount);
                };

                yield return WaitFor(() => cuts.Count == 1, 80f, "the last bar of the song to end the piece");
                var tookSec = cuts[0] - runningSince;
                Assert.LessOrEqual(tookSec, SongEndWithinSec, $"the piece ended {tookSec:0.0} s after the app ran: one song of 60 s");
                Assert.AreEqual(FirstSongId, starts[0].Song.Id);
                Assert.AreEqual(1, ended.Count, "the visit ended in the frame of the end");
                Assert.AreEqual(ExperienceState.Idle, atTheEnd.State);
                Assert.AreEqual(1, atTheEnd.Cards, "the card was written at once");
                Assert.IsTrue(atTheEnd.Word, "the word of the end is up");
                Assert.AreEqual("끝", atTheEnd.WordText);
                Assert.AreEqual(0, atTheEnd.CutCount, "the end of a song is not counted among the endings with the hands");
                CollectionAssert.Contains(collected.Logs, $"[songs] end {FirstSongId}", "the song end is logged as such");
                Assert.IsFalse(collected.Logs.Any(l => l.StartsWith("[cutoff]")), "and not as two fists");

                var cards = Directory.GetFiles(_cardsDir, "*.json", SearchOption.AllDirectories);
                Assert.AreEqual(1, cards.Length, "one card");
                var json = File.ReadAllText(cards[0]);
                StringAssert.Contains("\"songTitle\":\"" + FirstSongTitle + "\"", json, "the card names the song");
                StringAssert.Contains("\"songId\":\"" + FirstSongId + "\"", json);

                // the visit made a card, so the next one takes the next song, from its first bar, as soon as an open hand starts it after the lock
                yield return WaitFor(() => starts.Count == 2, 20f, "an open hand to start the next visit");
                Assert.AreEqual(SecondSongId, starts[1].Song.Id, "the song moved on: the visit had notes for a card");
                Assert.AreEqual(1, cuts.Count, "and nothing ended twice");
                Assert.AreEqual(1, Directory.GetFiles(_cardsDir, "*.json", SearchOption.AllDirectories).Length, "one card still");
                Assert.IsEmpty(collected.Errors, "no error was logged: " + string.Join(" | ", collected.Errors));
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [UnityTest]
        public IEnumerator SongMode_Off_IsFreePlay()
        {
            var collected = new Collected();
            var onLog = collected.Callback;
            Application.logMessageReceived += onLog;
            try
            {
                DownbeatApp app = null;
                yield return Start(new[] { "--songs", "off" }, (a, e) => app = a);
                var notes = new List<(ScheduledNote Note, SampleResolution Sound)>();
                app.NoteScheduled += (note, sound) => notes.Add((note, sound));
                var titleShown = false;

                var until = Time.realtimeSinceStartup + PlaySec;
                while (Time.realtimeSinceStartup < until)
                {
                    titleShown |= app.SongTitle != null && app.SongTitle.IsShowing;
                    yield return null;
                }

                Assert.IsFalse(app.Songs.Enabled, "the switch is off");
                Assert.IsNull(app.Songs.VisitSong, "the visit has no song");
                Assert.IsFalse(titleShown, "no title");
                Assert.IsFalse(collected.Logs.Any(l => l.StartsWith("[songs] start")), "and no song began");
                var seconds = PlaySec - 0.5f;
                Assert.GreaterOrEqual(app.HandNotes, 12, $"{app.HandNotes} hand notes in {seconds:0} s of the demo hands: a hand that is raised keeps playing, as before");
                Assert.LessOrEqual(app.HandNotes / seconds, 14f, "and it is music, not a machine gun");
                Assert.GreaterOrEqual(app.Energy.Level, 1, "the meadow wakes up");
                var content = ShippedContent();
                var scale = new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps);
                var roots = content.instruments.ToDictionary(i => i.part, i => i.rootMidi);
                Assert.IsTrue(notes.All(n => n.Note.Midi == -1), "free play sounds by the steps of the scale: no note carries a MIDI");
                Assert.IsTrue(notes.All(n => n.Sound.TargetMidi == scale.MidiForStep(roots[Parts.Name(n.Note.Part)], n.Note.PitchStep)), "and each is made for the note of its step, as before");
                Assert.IsEmpty(collected.Errors, "no error was logged: " + string.Join(" | ", collected.Errors));
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [UnityTest]
        public IEnumerator SongMode_Strike_HasNoSong()
        {
            var collected = new Collected();
            var onLog = collected.Callback;
            Application.logMessageReceived += onLog;
            try
            {
                DownbeatApp app = null;
                yield return Start(new[] { "--songs", "on", "--play-mode", "strike" }, (a, e) => app = a);

                yield return new WaitForSecondsRealtime(PlaySec);

                Assert.IsTrue(app.Songs.Enabled, "the switch is on, but a hand that strikes has no song to conduct");
                Assert.IsNull(app.Songs.VisitSong);
                Assert.IsFalse(app.SongTitle != null && app.SongTitle.IsShowing);
                Assert.IsFalse(collected.Logs.Any(l => l.StartsWith("[songs] start")));
                Assert.GreaterOrEqual(app.HandNotes, 1, "the strikes of the demo hands make notes");
                Assert.IsEmpty(collected.Errors, "no error was logged: " + string.Join(" | ", collected.Errors));
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [UnityTest]
        public IEnumerator SongMode_TheSwitch_StartsAsResolved_AndTheTabPanelTakesItOff()
        {
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "on" }, (a, e) => app = a);

            Assert.IsTrue(app.Songs.Enabled);
            Assert.IsTrue(app.SettingsPanel.SongModeOn, "the panel shows what the run starts with");

            app.SettingsPanel.SetSongMode(false);

            Assert.IsFalse(app.Songs.Enabled, "the next visit is free play");
            Assert.AreEqual("{\"songMode\":false}", File.ReadAllText(_settingsFile));
            app.SettingsPanel.SetSongMode(true);
            Assert.IsTrue(app.Songs.Enabled, "and on again");
        }

        [UnityTest]
        public IEnumerator SongMode_LaunchOptionOff_ShowsInThePanel_AndBeatsASavedOn()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_settingsFile, SongModeSetting.Encode(true));
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "off" }, (a, e) => app = a);

            Assert.IsFalse(app.Songs.Enabled, "the command line beats the saved choice");
            Assert.IsFalse(app.SettingsPanel.SongModeOn);
        }

        [UnityTest]
        public IEnumerator SongMode_ATestThatSaysNothingAboutSongs_RunsWithSongsOff()
        {
            Assert.AreEqual("off", LaunchArgs.Current.Get("songs"), "the PlayMode assembly starts every test with --songs off (PlayModeDefaults)");
            DownbeatApp app = null;
            yield return Start(new string[0], (a, e) => app = a);

            Assert.IsFalse(app.Songs.Enabled, "the shipped content has songs, but a test that does not ask for them plays freely");
            Assert.IsFalse(app.SettingsPanel.SongModeOn);
        }

        [UnityTest]
        public IEnumerator SongMode_TheTitleView_ShowsOnTheOverlay_AndHidesAtOnce()
        {
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "off" }, (a, e) => app = a);
            var title = app.SongTitle;
            Assert.IsNotNull(title, "made when the app was built, not in the frame of the first song");
            Assert.IsFalse(title.IsShowing);
            Assert.IsFalse(title.gameObject.activeSelf, "and hidden until a song begins");

            title.SetTitle("올드 랭 사인");
            title.Show(Time.realtimeSinceStartupAsDouble, 3.0);
            yield return null;

            Assert.IsTrue(title.IsShowing);
            Assert.AreEqual("올드 랭 사인", title.Text);
            Assert.IsTrue(title.gameObject.activeSelf);
            Assert.Greater(title.Alpha, 0f);
            title.Hide();
            Assert.IsFalse(title.IsShowing, "a visit that ends takes the title with it");
            Assert.IsFalse(title.gameObject.activeSelf);

            title.SetTitle(string.Empty);
            title.Show(Time.realtimeSinceStartupAsDouble, 3.0);
            Assert.IsFalse(title.IsShowing, "no title, nothing to show");
        }

        [UnityTest]
        public IEnumerator SongMode_LatencyMode_HasNoSong_EvenWhenTheHandsConduct()
        {
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "on", "--play-mode", "conduct", "--latency-mode" }, (a, e) => app = a);

            yield return new WaitForSecondsRealtime(4f);

            Assert.IsNull(app.Songs, "a measurement of the latency is not stopped by the end of a song");
            Assert.GreaterOrEqual(app.HandNotes, 1, "the hands play, freely");
        }

        [UnityTest]
        public IEnumerator SongMode_WithoutASession_ThePanelSaysThereIsNoSong_AndSavesNothing()
        {
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "on", "--latency-mode" }, (a, e) => app = a);
            var panel = app.SettingsPanel;
            var raised = new List<bool>();
            panel.SongModeChanged += raised.Add;

            Assert.IsNull(app.Songs, "--latency-mode: no session");
            Assert.IsFalse(panel.HasSongs);
            Assert.AreEqual("곡 모드: 곡 없음", panel.SongLabel, "not '곡 모드 켜짐': that switch would change nothing");
            panel.SetSongMode(false);
            panel.SetSongMode(true);

            Assert.IsEmpty(raised, "a click on a row without songs tells nobody");
            Assert.IsTrue(panel.SongModeOn, "and changes nothing");
            Assert.IsFalse(File.Exists(_settingsFile), "and saves nothing");
        }

        [UnityTest]
        public IEnumerator SongMode_WithASession_ThePanelKeepsItsTwoLabels()
        {
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "on" }, (a, e) => app = a);
            var panel = app.SettingsPanel;

            Assert.IsTrue(panel.HasSongs);
            Assert.AreEqual(SettingsPanelView.SongOnLabel, panel.SongLabel);
            LogAssert.Expect(LogType.Log, new Regex(@"^\[songs\] switch off \(tab\) at "));
            panel.SetSongMode(false);
            Assert.AreEqual(SettingsPanelView.SongOffLabel, panel.SongLabel);
        }

        [UnityTest]
        public IEnumerator SongMode_AnUnknownSongsValue_IsWarnedAboutOnce_AndTheContentValueIsUsed()
        {
            var collected = new Collected();
            var onLog = collected.Callback;
            Application.logMessageReceived += onLog;
            try
            {
                DownbeatApp app = null;
                yield return Start(new[] { "--songs", "true" }, (a, e) => app = a);

                var warnings = collected.Logs.Where(l => l.Contains("--songs 'true'")).ToList();
                Assert.AreEqual(new[] { "[songs] --songs 'true' is not on or off; the content's choice (on) is used" }, warnings.ToArray());
                Assert.IsTrue(app.Songs.Enabled, "the content says on, and nothing else was chosen");
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [UnityTest]
        public IEnumerator SongMode_WithoutTheRecordScreen_StillPlaysTheSong()
        {
            var collected = new Collected();
            var onLog = collected.Callback;
            Application.logMessageReceived += onLog;
            try
            {
                DownbeatApp app = null;
                yield return Start(new[] { "--songs", "on", "--no-record" }, (a, e) => app = a);
                var starts = new List<SongScore>();
                app.Songs.SongStarted += (song, barOne) => starts.Add(song);

                yield return WaitFor(() => starts.Count == 1, 15f, "the song to begin");
                yield return new WaitForSecondsRealtime(3f);

                Assert.IsNull(app.Record, "there is no record screen");
                Assert.AreEqual(FirstSongId, app.Songs.VisitSong.Id);
                Assert.GreaterOrEqual(app.HandNotes, 1);
                Assert.IsEmpty(collected.Errors, "no error was logged: " + string.Join(" | ", collected.Errors));
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [UnityTest]
        public IEnumerator SongMode_TheSky_PlaysTheChordOfTheBarOfTheSong()
        {
            var content = ShippedContent();
            var scale = new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps);
            var skyRoot = content.nature.instruments.First(i => i.layer == "sky").rootMidi;
            DownbeatApp app = null;
            IAudioEngine audio = null;
            yield return Start(new[] { "--songs", "on", "--hold-energy", "0.8" }, (a, e) => { app = a; audio = e; });
            var chords = new List<NatureEvent>();
            app.NatureScheduled += e => { if (e.Note.Layer == NatureLayer.Sky) chords.Add(e); };
            var starts = new List<(SongScore Song, double BarOne)>();
            app.Songs.SongStarted += (song, barOne) => starts.Add((song, barOne));

            yield return WaitFor(() => starts.Count == 1, 15f, "the song to begin");
            var (score, bar1) = starts[0];
            const int Checked = 6;
            var barSec = SlotsPerBar * SlotSec;
            while (audio.Now < bar1 + (Checked + 1) * barSec) yield return null;

            // bar 1 is left out: its chord may have been asked for (one bar ahead) before the visit began, which is the free play chord
            for (var bar = 1; bar <= Checked; bar++)
            {
                var at = bar1 + bar * barSec;
                var played = chords.Where(c => Math.Abs(c.AudioTime - at) < SlotSec * 0.5).Select(c => c.Note.PitchStep).OrderBy(step => step).ToArray();
                var wanted = SongChords.SkyStepsFor(score.Bars[bar].Chord, scale, skyRoot);
                CollectionAssert.AreEqual(wanted, played, $"bar {bar + 1}: the sky plays the chord of the bar of the song");
            }
        }

        [UnityTest]
        public IEnumerator Handle_ANoteWithMidi_SoundsThatMidi()
        {
            var content = ShippedContent();
            var scale = new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps);
            var instruments = content.instruments.ToDictionary(i => i.part);
            DownbeatApp app = null;
            yield return Start(new[] { "--songs", "on" }, (a, e) => app = a);
            var notes = new List<(ScheduledNote Note, SampleResolution Sound)>();
            app.NoteScheduled += (note, sound) => notes.Add((note, sound));

            yield return WaitFor(() => notes.Count(n => n.Note.Midi >= 0) >= 20, 40f, "twenty notes of the song");

            var withMidi = notes.Where(n => n.Note.Midi >= 0).ToList();
            var differsFromItsStep = 0;
            foreach (var (note, sound) in withMidi)
            {
                var instrument = instruments[Parts.Name(note.Part)];
                var where = $"{note.Part} midi {note.Midi}";
                Assert.AreEqual(note.Midi, sound.TargetMidi, where + ": the sound is made for the MIDI of the note");
                var sample = instrument.samples.First(x => x.id == sound.SampleId);
                Assert.AreEqual(note.Midi, sample.soundingMidi + sound.Semitones, where + ": the sample is shifted from its own pitch to it");
                var nearest = instrument.samples.Min(x => Math.Abs(x.soundingMidi - note.Midi));
                Assert.AreEqual(nearest, Math.Abs(sample.soundingMidi - note.Midi), where + ": it is the sample that sounds nearest");
                if (scale.MidiForStep(instrument.rootMidi, note.PitchStep) != note.Midi) differsFromItsStep++;
            }
            Assert.Greater(differsFromItsStep, 0, "some notes of the songs lie off the notes of their step (the step is kept inside the scale of the instrument): a path by the step would have sounded another note");
        }
    }
}
