using System.Collections.Generic;
using System.Linq;
using Downbeat.Audio;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>Unity's audio system with a clock the test moves by hand and a record of what was played.</summary>
    sealed class FakeAudioBackend : IAudioBackend
    {
        public readonly struct PlayCall
        {
            public int Voice { get; }
            public int Sample { get; }
            public double Start { get; }
            public double Rate { get; }
            public float Volume { get; }

            public PlayCall(int voice, int sample, double start, double rate, float volume)
            {
                Voice = voice; Sample = sample; Start = start; Rate = rate; Volume = volume;
            }
        }

        public readonly List<PlayCall> Plays = new List<PlayCall>();
        public readonly List<int> Stops = new List<int>();
        public readonly List<(int Voice, double Seconds)> Fades = new List<(int Voice, double Seconds)>();
        /// <summary>For every voice that is fading: the audio time until which its source is kept alive (fade + the margin before it is stopped). A new note must not be played on it before that.</summary>
        public readonly Dictionary<int, double> FadingUntil = new Dictionary<int, double>();
        /// <summary>Plays on a source that was still fading and not stopped first: the click of a cut fade.</summary>
        public readonly List<int> PlaysInsideAFade = new List<int>();
        public double FadeReleaseMarginSec { get; set; } = 0.03;
        public readonly Dictionary<int, double> SampleLengths = new Dictionary<int, double>();
        public int Voices;

        public double DspTime { get; set; } = 10.0;
        public double OutputLatencySec { get; set; } = 0.08;
        public int LoadedCount { get; set; }
        public int FailedCount { get; set; }

        public void CreateVoices(int count) => Voices = count;
        public void BeginLoading(IReadOnlyList<SampleLoadRequest> samples) { }
        public void Poll() { }
        public bool HasSample(int index) => SampleLengths.ContainsKey(index);
        public double SampleSeconds(int index) => SampleLengths[index];
        public void Play(int voice, int sample, double startDsp, double rate, float volume)
        {
            if (FadingUntil.TryGetValue(voice, out var until) && DspTime < until && !_stoppedSinceFade.Contains(voice)) PlaysInsideAFade.Add(voice);
            FadingUntil.Remove(voice);
            _stoppedSinceFade.Remove(voice);
            Plays.Add(new PlayCall(voice, sample, startDsp, rate, volume));
        }

        public void Stop(int voice)
        {
            Stops.Add(voice);
            _stoppedSinceFade.Add(voice);
        }

        public void FadeOut(int voice, double seconds)
        {
            Fades.Add((voice, seconds));
            FadingUntil[voice] = DspTime + seconds + FadeReleaseMarginSec;
            _stoppedSinceFade.Remove(voice);
        }

        readonly HashSet<int> _stoppedSinceFade = new HashSet<int>();
    }

    public class NativeAudioEngineTests
    {
        const double Now = 10.0;
        const double SampleSec = 1.0;
        const double MinLeadSec = 0.005;
        const double MinFadeSec = 0.005;
        const double MaxFadeSec = 0.010;

        static (NativeAudioEngine engine, FakeAudioBackend backend) CreateEngine(int voices = 4, float masterGain = 1f)
        {
            var backend = new FakeAudioBackend { DspTime = Now };
            backend.SampleLengths[0] = SampleSec;
            var engine = new NativeAudioEngine(backend);
            engine.Configure(masterGain, voices);
            engine.Tick();
            return (engine, backend);
        }

        [Test]
        public void Schedule_ReturnsRequestedTime_WhenInFuture()
        {
            var (engine, backend) = CreateEngine();

            var start = engine.Schedule(0, Now + 0.25, 1.0, 1.0);

            Assert.AreEqual(Now + 0.25, start, 1e-12);
            Assert.AreEqual(1, backend.Plays.Count);
            Assert.AreEqual(Now + 0.25, backend.Plays[0].Start, 1e-12);
            Assert.AreEqual(0, engine.Stats.Late);
            Assert.AreEqual(1, engine.Stats.Scheduled);
        }

        [Test]
        public void Schedule_CountsLate_WhenTimeHasPassed()
        {
            var (engine, backend) = CreateEngine();

            var start = engine.Schedule(0, Now - 0.5, 1.0, 1.0);

            Assert.AreEqual(1, engine.Stats.Late);
            Assert.AreEqual(Now + MinLeadSec, start, 1e-12, "a late note starts as soon as the audio system can, and the caller is told when");
            Assert.AreEqual(start, backend.Plays[0].Start, 1e-12);
        }

        [Test]
        public void Schedule_StealsTheOldestPendingVoice_ByCancellingIt_WhenTheLimitIsReached()
        {
            var (engine, backend) = CreateEngine(voices: 2);

            engine.Schedule(0, Now + 0.20, 1.0, 1.0); // voice A, the oldest, still waiting to start
            engine.Schedule(0, Now + 0.30, 1.0, 1.0); // voice B
            engine.Schedule(0, Now + 0.40, 1.0, 1.0); // a third sound at once: only two may sound together

            Assert.AreEqual(1, engine.Stats.Stolen);
            Assert.AreEqual(1, backend.Stops.Count, "A has not made a sound yet: cancelling it costs nothing");
            Assert.AreEqual(backend.Plays[0].Voice, backend.Stops[0], "the voice that was due first is the one cancelled");
            Assert.AreEqual(0, backend.Fades.Count);
        }

        [Test]
        public void Schedule_ReusesAFinishedVoiceWithoutStealing()
        {
            var (engine, backend) = CreateEngine(voices: 1);

            engine.Schedule(0, Now + 0.20, 1.0, 1.0); // sounds from Now + 0.2 until Now + 1.2
            backend.DspTime = Now + 0.25;
            engine.Tick(); // it has started
            engine.Schedule(0, Now + 1.50, 1.0, 1.0);

            Assert.AreEqual(0, engine.Stats.Stolen);
            Assert.AreEqual(0, backend.Stops.Count);
        }

        [Test]
        public void Schedule_AccountsForPlaybackRate_WhenDecidingIfAVoiceIsFree()
        {
            var (engine, _) = CreateEngine(voices: 1);

            engine.Schedule(0, Now + 0.20, 0.5, 1.0); // half speed: sounds for 2 s, until Now + 2.2
            engine.Schedule(0, Now + 1.50, 1.0, 1.0);

            Assert.AreEqual(1, engine.Stats.Stolen);
        }

        [Test]
        public void Schedule_CountsMissingSample_AndPlaysNothing()
        {
            var (engine, backend) = CreateEngine();

            var start = engine.Schedule(7, Now + 0.25, 1.0, 1.0);

            Assert.AreEqual(Now + 0.25, start, 1e-12);
            Assert.AreEqual(1, engine.Stats.MissingSample);
            Assert.AreEqual(0, backend.Plays.Count);
            Assert.AreEqual(0, engine.Stats.Scheduled);
        }

        [Test]
        public void Schedule_AppliesMasterGainAndPlaybackRate()
        {
            var (engine, backend) = CreateEngine(masterGain: 0.5f);

            engine.Schedule(0, Now + 0.25, 1.5, 0.8);

            Assert.AreEqual(0.4f, backend.Plays[0].Volume, 1e-6f);
            Assert.AreEqual(1.5, backend.Plays[0].Rate, 1e-12);
        }

        [Test]
        public void Schedule_PassesTheClampedPlaybackRateToTheBackend()
        {
            var (engine, backend) = CreateEngine();

            engine.Schedule(0, Now + 0.25, 0.0, 1.0);
            engine.Schedule(0, Now + 0.30, -2.0, 1.0);

            Assert.AreEqual(0.01, backend.Plays[0].Rate, 1e-12, "the length of the voice was worked out with the clamped rate, so the sound must play at it too");
            Assert.AreEqual(0.01, backend.Plays[1].Rate, 1e-12);
        }

        [Test]
        public void Schedule_DoesNotReuseAVoiceWhoseSoundHasNotStartedYet()
        {
            var (engine, backend) = CreateEngine(voices: 2);
            backend.SampleLengths[1] = 0.2;
            engine.Schedule(0, Now + 0.01, 1.0, 1.0); // P: sounds until Now + 1.01
            engine.Schedule(1, Now + 0.30, 1.0, 1.0); // A: waiting to start; stopping it now would cancel its note
            backend.DspTime = Now + 0.10;
            engine.Tick(); // P is sounding, A has not begun

            engine.Schedule(0, Now + 1.20, 1.0, 1.0); // C: starts after both are over

            Assert.AreNotEqual(backend.Plays[1].Voice, backend.Plays[2].Voice, "A's note is still to come, C must not take its voice");
            Assert.AreEqual(0, engine.Stats.Stolen);
            Assert.AreEqual(0, backend.Stops.Count);
            Assert.AreEqual(0, backend.Fades.Count);
        }

        [Test]
        public void Schedule_NeedsNoSteal_WhenTheOnlyVoiceWillBeOverBeforeTheNewSoundStarts()
        {
            var (engine, backend) = CreateEngine(voices: 1);
            engine.Schedule(0, Now + 0.50, 1.0, 1.0); // waits for Now + 0.5, over at Now + 1.5

            engine.Schedule(0, Now + 3.00, 1.0, 1.0); // when this starts the first is long over: two sounds are never heard together

            Assert.AreEqual(0, engine.Stats.Stolen, "the limit counts what sounds at the same time");
            Assert.AreEqual(0, backend.Stops.Count);
            Assert.AreNotEqual(backend.Plays[0].Voice, backend.Plays[1].Voice, "the first sound is still waiting for its start: its voice is left alone");
        }

        [Test]
        public void Schedule_FadesASoundingVoiceInsteadOfStoppingIt_WhenItMustBeCut()
        {
            var (engine, backend) = CreateEngine(voices: 2);
            engine.Schedule(0, Now + 0.20, 1.0, 1.0); // A, the oldest
            engine.Schedule(0, Now + 0.25, 1.0, 1.0); // B
            backend.DspTime = Now + 0.30;
            engine.Tick(); // both are sounding

            engine.Schedule(0, Now + 0.40, 1.0, 1.0); // a third at the same time: A has to give way

            Assert.AreEqual(1, engine.Stats.Stolen);
            Assert.AreEqual(0, backend.Stops.Count, "no hard stop of a sounding voice");
            Assert.AreEqual(1, backend.Fades.Count);
            Assert.AreEqual(backend.Plays[0].Voice, backend.Fades[0].Voice, "the oldest sound fades");
            Assert.That(backend.Fades[0].Seconds, Is.InRange(MinFadeSec, MaxFadeSec), "a fade of 5 to 10 ms: short, but no click");
            Assert.AreNotEqual(backend.Plays[0].Voice, backend.Plays[2].Voice, "the new note does not wait for the fade: it plays on another source");
        }

        [Test]
        public void Schedule_StealsTheOldestSoundingVoice_BeforeAVoiceThatIsStillWaiting()
        {
            var (engine, backend) = CreateEngine(voices: 2);
            engine.Schedule(0, Now + 0.20, 1.0, 1.0); // A
            engine.Schedule(0, Now + 0.25, 1.0, 1.0); // A2
            engine.Schedule(0, Now + 5.00, 1.0, 1.0); // D: far in the future, waiting
            backend.DspTime = Now + 0.30;
            engine.Tick();

            engine.Schedule(0, Now + 0.40, 1.0, 1.0); // C: with A and A2 sounding, one of them gives way; D is not sounding then

            Assert.AreEqual(1, engine.Stats.Stolen);
            Assert.AreEqual(backend.Plays[0].Voice, backend.Fades[0].Voice, "A, not D, whose note is still to come");
            Assert.AreEqual(0, backend.Stops.Count);
        }

        [Test]
        public void Schedule_NeverPlaysOnASourceThatIsStillFading_UnderFastRepeatedNotes()
        {
            // one voice may sound at a time: every new note makes the previous one fade (8 ms), and the source is kept for 30 ms more before it is stopped.
            // At 5 ms between notes the four spare sources are all busy fading when the sixth note comes: it must not take one of them.
            var (engine, backend) = CreateEngine(voices: 1);
            backend.SampleLengths[0] = 5.0;
            const double Step = 0.005;
            for (var i = 0; i < 14; i++)
            {
                backend.DspTime = Now + i * Step;
                engine.Tick();
                engine.Schedule(0, backend.DspTime + 0.005, 1.0, 1.0); // starts as early as the engine allows: the one before (5 ms earlier) is already sounding
            }

            CollectionAssert.IsEmpty(backend.PlaysInsideAFade, "a source whose fade has not finished (plus the stop margin) was reused: the fade is cut and clicks");
            Assert.AreEqual(14, backend.Plays.Count);
            Assert.GreaterOrEqual(backend.Fades.Count, 4, "the scenario really makes voices fade");
        }

        [Test]
        public void Schedule_FallsBackToTakingTheEarliestSource_WhenEverySourceIsWaitingForItsNote()
        {
            var (engine, backend) = CreateEngine(voices: 1);
            backend.SampleLengths[0] = 0.5;
            var sources = backend.Voices;
            for (var i = 0; i < sources; i++) engine.Schedule(0, Now + 1.0 + i, 1.0, 1.0); // one note after the other, each on its own source, all still to come

            engine.Schedule(0, Now + 20.0, 1.0, 1.0); // not a single source is idle

            Assert.AreEqual(1, engine.Stats.Stolen);
            Assert.AreEqual(1, backend.Stops.Count);
            Assert.AreEqual(backend.Plays[0].Voice, backend.Stops[0], "the note that was due first is given up");
            Assert.AreEqual(backend.Stops[0], backend.Plays[sources].Voice, "its source plays the new note");
        }

        const int PlayerSample = 0;
        const int NatureSample = 1;

        /// <summary>An engine that knows sample 1 as a nature sound (what the app tells it) and sample 0 as a player's.</summary>
        static (NativeAudioEngine engine, FakeAudioBackend backend) CreateEngineWithNature(int voices = 6)
        {
            var (engine, backend) = CreateEngine(voices);
            backend.SampleLengths[NatureSample] = SampleSec;
            engine.LoadSamples(new[] { new SampleLoadRequest(PlayerSample, "player.wav"), new SampleLoadRequest(NatureSample, "nature.wav", isNature: true) });
            return (engine, backend);
        }

        [Test]
        public void FadeOutAll_SilencesPlayerVoices_NotNature()
        {
            var (engine, backend) = CreateEngineWithNature();
            engine.Schedule(PlayerSample, Now + 0.10, 1.0, 1.0);  // 0: a player note that is sounding
            engine.Schedule(NatureSample, Now + 0.12, 1.0, 1.0);  // 1: a nature note that is sounding
            engine.Schedule(PlayerSample, Now + 0.14, 1.0, 1.0);  // 2: another player note that is sounding
            engine.Schedule(PlayerSample, Now + 0.60, 1.0, 1.0);  // 3: a player note that has not started
            engine.Schedule(NatureSample, Now + 0.70, 1.0, 1.0);  // 4: a nature note that has not started
            backend.DspTime = Now + 0.30;
            engine.Tick();

            engine.FadeOutAll(0.25);

            var fadedVoices = backend.Fades.Select(f => f.Voice).ToList();
            CollectionAssert.AreEquivalent(new[] { backend.Plays[0].Voice, backend.Plays[2].Voice }, fadedVoices, "the player notes that sound fade");
            Assert.IsTrue(backend.Fades.All(f => f.Seconds == 0.25), "over the time asked for");
            CollectionAssert.AreEqual(new[] { backend.Plays[3].Voice }, backend.Stops, "the player note that has not started is cancelled, nothing of it was heard");
            CollectionAssert.DoesNotContain(fadedVoices, backend.Plays[1].Voice, "the nature note goes on");
            CollectionAssert.DoesNotContain(backend.Stops, backend.Plays[1].Voice);
            CollectionAssert.DoesNotContain(backend.Stops, backend.Plays[4].Voice, "and the nature note to come is not cancelled");
        }

        [Test]
        public void FadeOutAll_KeepsTheFadingSourcesApart_FromTheNotesThatFollow()
        {
            var (engine, backend) = CreateEngineWithNature();
            engine.Schedule(PlayerSample, Now + 0.10, 1.0, 1.0);
            backend.DspTime = Now + 0.30;
            engine.Tick();
            engine.FadeOutAll(0.25);

            // the piece is over, a new note comes while the old one fades (0.25 s and the margin): it must not take the source that is fading
            backend.DspTime = Now + 0.40;
            engine.Tick();
            engine.Schedule(PlayerSample, Now + 0.45, 1.0, 1.0);

            Assert.AreNotEqual(backend.Plays[0].Voice, backend.Plays[1].Voice);
            CollectionAssert.IsEmpty(backend.PlaysInsideAFade, "the fade is not cut by a new note");
        }

        [Test]
        public void FadeOutAll_FreesTheCancelledSources_AtOnce()
        {
            var (engine, backend) = CreateEngineWithNature(voices: 1);
            backend.SampleLengths[PlayerSample] = 0.5;
            var sources = backend.Voices;
            for (var i = 0; i < sources; i++) engine.Schedule(PlayerSample, Now + 1.0 + i, 1.0, 1.0); // every source waits for a note that has not started
            engine.FadeOutAll(0.25);
            Assert.AreEqual(sources, backend.Stops.Count, "all of them are cancelled");

            engine.Schedule(PlayerSample, Now + 0.50, 1.0, 1.0);

            Assert.AreEqual(0, engine.Stats.Stolen, "the cancelled notes are gone: no source has to be taken by force");
            Assert.AreEqual(sources, backend.Stops.Count);
        }

        [Test]
        public void FadeOutAll_LeavesVoicesAlone_ThatHaveFinishedOrNeverPlayed()
        {
            var (engine, backend) = CreateEngineWithNature();
            engine.FadeOutAll(0.25);
            Assert.AreEqual(0, backend.Fades.Count + backend.Stops.Count, "nothing has played");

            engine.Schedule(PlayerSample, Now + 0.10, 1.0, 1.0); // sounds from Now + 0.1 to Now + 1.1
            backend.DspTime = Now + 2.0;
            engine.Tick();
            engine.FadeOutAll(0.25);

            Assert.AreEqual(0, backend.Fades.Count + backend.Stops.Count, "it was over by itself");
        }

        [Test]
        public void FadeOutAll_NeverFadesFasterThanAVoiceThatGivesWay()
        {
            var (engine, backend) = CreateEngineWithNature();
            engine.Schedule(PlayerSample, Now + 0.10, 1.0, 1.0);
            backend.DspTime = Now + 0.30;
            engine.Tick();

            engine.FadeOutAll(0.0);

            Assert.AreEqual(1, backend.Fades.Count);
            Assert.GreaterOrEqual(backend.Fades[0].Seconds, MinFadeSec, "a fade of no length is a click");
        }

        [Test]
        public void FadeOutAll_BeforeConfigure_IsHarmless()
        {
            var engine = new NativeAudioEngine(new FakeAudioBackend { DspTime = Now });

            Assert.DoesNotThrow(() => engine.FadeOutAll(0.25));
        }

        [Test]
        public void FadeOutAll_TreatsASampleAsAPlayersUnlessTheAppSaidItIsNature()
        {
            var (engine, backend) = CreateEngine(voices: 4);
            engine.LoadSamples(new[] { new SampleLoadRequest(0, "a.wav") }); // the defaults of the clock measurement and the older callers
            engine.Schedule(0, Now + 0.10, 1.0, 1.0);
            backend.DspTime = Now + 0.30;
            engine.Tick();

            engine.FadeOutAll(0.25);

            Assert.AreEqual(1, backend.Fades.Count);
        }

        [Test]
        public void SampleLoadRequest_IsNotNatureByDefault()
        {
            Assert.IsFalse(new SampleLoadRequest(3, "x.wav").IsNature);
            Assert.IsTrue(new SampleLoadRequest(3, "x.wav", isNature: true).IsNature);
            Assert.AreEqual(3, new SampleLoadRequest(3, "x.wav", true).Index);
        }

        [Test]
        public void Configure_CreatesTheRequestedNumberOfVoicesAndSomeSparesForFades()
        {
            var (_, backend) = CreateEngine(voices: 6);

            Assert.AreEqual(6 + NativeAudioEngine.SpareVoices, backend.Voices, "the spares let a stolen voice fade out while the new note plays elsewhere");
        }

        [Test]
        public void Tick_UpdatesNowAndHeardNow_WithTheOutputLatencyEstimate()
        {
            var (engine, backend) = CreateEngine();
            backend.DspTime = 42.0;
            backend.OutputLatencySec = 0.085;

            engine.Tick();

            Assert.AreEqual(42.0, engine.Now, 1e-12);
            Assert.AreEqual(42.0 - 0.085, engine.HeardNow, 1e-12);
        }

        [Test]
        public void VisualNow_FollowsTheWallClockBetweenTheStepsOfTheAudioClock()
        {
            var backend = new FakeAudioBackend { DspTime = 10.0, OutputLatencySec = 0.08 };
            var wall = 0.0;
            var engine = new NativeAudioEngine(backend, () => wall);
            engine.Configure(1f, 4);
            const double Block = 0.0213;
            for (var i = 0; i < 60; i++)
            {
                wall = i * 0.01;
                backend.DspTime = 10.0 + System.Math.Floor(wall / Block) * Block; // the audio clock steps once per block
                engine.Tick();
            }

            var lastStep = 10.0 + System.Math.Floor(wall / Block) * Block;
            Assert.AreEqual(lastStep, engine.Now, 1e-9);
            Assert.AreEqual(10.0 + wall - 0.08, engine.VisualNow, 0.003, "the eyes see the clock where it is now, not where it last stepped");
            Assert.Greater(engine.VisualNow, engine.HeardNow + 0.001, "ahead of the exact reading by the part of a block that has passed");
            Assert.AreEqual(lastStep - 0.08, engine.HeardNow, 1e-9, "HeardNow stays the exact audio time (for measurements)");
        }

        [Test]
        public void IsRunning_NeedsEverySampleLoadedAndAClockThatHasMoved()
        {
            var backend = new FakeAudioBackend { DspTime = 0.0 };
            var engine = new NativeAudioEngine(backend);
            engine.Configure(1f, 4);
            engine.LoadSamples(new[] { new SampleLoadRequest(0, "file:///a.wav"), new SampleLoadRequest(1, "file:///b.wav") });

            backend.LoadedCount = 1;
            engine.Tick();
            Assert.IsFalse(engine.SamplesReady);
            Assert.IsFalse(engine.IsRunning);

            backend.LoadedCount = 2;
            engine.Tick();
            Assert.IsTrue(engine.SamplesReady);
            Assert.IsFalse(engine.IsRunning, "the audio clock has not moved yet");

            backend.DspTime = 1.0;
            engine.Tick();
            Assert.IsTrue(engine.IsRunning);
        }

        [Test]
        public void SamplesFailed_ReportsTheBackendFailures()
        {
            var backend = new FakeAudioBackend();
            var engine = new NativeAudioEngine(backend);
            engine.Configure(1f, 4);
            engine.LoadSamples(new[] { new SampleLoadRequest(0, "file:///missing.wav") });
            backend.FailedCount = 1;

            engine.Tick();

            Assert.AreEqual(1, engine.SamplesFailed);
            Assert.IsFalse(engine.SamplesReady);
        }
    }
}
