using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Audio;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Downbeat.Tests
{
    /// <summary>
    /// The app's own recorder (<c>--record-dir</c>) when the app is closed before the recording time is up (the demo take of the Windows laptop): the sound, the frame times and recording.json
    /// must be written with what was recorded until then, the same files as when the time runs out. The quit is the same message the player sends (<c>OnApplicationQuit</c>), or the runner is destroyed.
    /// </summary>
    public class RunRecorderEarlyEndPlayModeTests
    {
        const string LongRecordingSec = "600";
        const float RecordedSec = 1.2f;
        const float ToneHz = 440f;
        const float ToneGain = 0.5f;
        const int PictureWidth = 64;
        const int PictureHeight = 36;
        const double NoAudioDeviceDspSec = 0.2;
        const double ClockToleranceSec = 5.0;
        const double FakeOutputLatencySec = 0.0427;
        const double MillisecondToleranceSec = 0.0015;

        /// <summary>Stamps the pictures with the DSP clock minus an output latency, which is what the app's engine does for its pictures.</summary>
        sealed class DspClockEngine : IAudioEngine
        {
            public bool IsRunning => true;
            public bool SamplesReady => true;
            public int SamplesFailed => 0;
            public double Now => AudioSettings.dspTime;
            public double HeardNow => Now - FakeOutputLatencySec;
            public double VisualNow => HeardNow;
            public AudioStats Stats => new AudioStats(0, 0, 0, 0);
            public void Configure(float masterGainLinear, int maxVoices) { }
            public void LoadSamples(System.Collections.Generic.IReadOnlyList<SampleLoadRequest> samples) { }
            public void Tick() { }
            public double Schedule(int sampleIndex, double when, double playbackRate, double gain) => when;
            public void FadeOutAll(double seconds) { }
            public void LogFlash(double when, double heardNow) { }
        }

        string _dir;
        GameObject _runnerObject;
        GameObject _listenerObject;
        GameObject _cameraObject;
        GameObject _toneObject;
        float _listenerVolume;
        int _quitCalls;

        [SetUp]
        public void SetUp()
        {
            _listenerVolume = AudioListener.volume; // other tests (--mute) leave the listener's volume where they like
            AudioListener.volume = 1f;
            _dir = Path.Combine(Path.GetTempPath(), "downbeat-recorder-" + Guid.NewGuid().ToString("N"));
            _quitCalls = 0;
            _runnerObject = new GameObject("recorder-runner");
            if (Object.FindFirstObjectByType<AudioListener>() == null) _listenerObject = new GameObject("recorder-listener", typeof(AudioListener));
            _cameraObject = new GameObject("recorder-camera") { tag = "MainCamera" };
            _cameraObject.AddComponent<Camera>();
            _toneObject = new GameObject("recorder-tone");
            var source = _toneObject.AddComponent<AudioSource>();
            source.clip = Tone();
            source.loop = true;
            source.Play();
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.volume = _listenerVolume;
            foreach (var made in new[] { _runnerObject, _listenerObject, _cameraObject, _toneObject })
            {
                if (made != null) Object.DestroyImmediate(made); // at once: the next test must not find this one's listener
            }
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static AudioClip Tone()
        {
            var rate = AudioSettings.outputSampleRate;
            var samples = new float[rate];
            for (var i = 0; i < samples.Length; i++) samples[i] = ToneGain * Mathf.Sin(2f * Mathf.PI * ToneHz * i / rate);
            var clip = AudioClip.Create("recorder-tone", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        RunRecorder Begin(string seconds)
        {
            var args = LaunchArgs.Parse(new[] { "--record-dir", _dir, "--record-seconds", seconds, "--record-fps", "30", "--record-size", $"{PictureWidth}x{PictureHeight}" });
            var recorder = new RunRecorder(new DspClockEngine(), _runnerObject.AddComponent<CoroutineRunner>(), args, nextPicture: () => null, quit: () => _quitCalls++); // the editor's batch mode never reaches the end of a frame; Application.Quit would do nothing in the editor, so the calls are counted
            recorder.Start();
            return recorder;
        }

        static DateTime ParseUtc(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        static bool HasNoAudioDevice() => AudioSettings.dspTime < NoAudioDeviceDspSec;

        RunRecordingDto ReadJson() => JsonUtility.FromJson<RunRecordingDto>(File.ReadAllText(Path.Combine(_dir, "recording.json")));

        void AssertTheThreeFilesAreConsistent(RunRecordingDto dto)
        {
            var wav = File.ReadAllBytes(Path.Combine(_dir, "audio.wav"));
            Assert.AreEqual(44 + 2 * dto.audioFrames, wav.Length, "audio.wav holds exactly the audio frames that recording.json counts");
            Assert.Greater(dto.audioFrames, 0, "some sound was recorded");
            Assert.Greater(dto.audioPeak, ToneGain * 0.5f, "and it is the tone that played, not silence");

            var lines = File.ReadAllLines(Path.Combine(_dir, "frames.csv"));
            Assert.AreEqual("index,visualNow", lines[0]);
            Assert.Greater(dto.frames, 0, "some pictures were taken");
            Assert.AreEqual(dto.frames, lines.Length - 1, "frames.csv has one row for every picture");
            Assert.AreEqual(dto.frames, Directory.GetFiles(Path.Combine(_dir, "frames"), "f*.jpg").Length, "and every picture is a file");
        }

        void AssertVideoZero(RunRecordingDto dto, DateTime started)
        {
            Assert.AreEqual(FakeOutputLatencySec, dto.outputLatencySec, 1e-6, "the output latency of the engine (Now minus HeardNow) is written");
            Assert.That(dto.videoZeroUtc, Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$"), "videoZeroUtc is ISO 8601 UTC with milliseconds too");
            Assert.AreEqual(FakeOutputLatencySec, (ParseUtc(dto.videoZeroUtc) - started).TotalSeconds, MillisecondToleranceSec, "second 0 of the video is the start plus the latency (to the millisecond that both are written in)");
        }

        [UnityTest]
        public IEnumerator ClosingTheApp_BeforeTheTimeIsUp_KeepsTheSoundTheFrameTimesAndMarksTheJson()
        {
            // Arrange
            var before = DateTime.UtcNow;
            var recorder = Begin(LongRecordingSec);
            yield return new WaitForSecondsRealtime(RecordedSec);
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");

            // Act: the same message the player sends when the window is closed
            _runnerObject.SendMessage("OnApplicationQuit");

            // Assert
            Assert.IsTrue(recorder.IsFinished);
            var dto = ReadJson();
            Assert.IsTrue(dto.endedEarly, "recording.json says the recording was cut short");
            Assert.That(dto.recordedSeconds, Is.InRange(RecordedSec * 0.5f, RecordedSec * 10f), "and how long it really was");
            AssertTheThreeFilesAreConsistent(dto);
            Assert.Less(dto.audioFrames / (double)dto.audioSampleRate, dto.recordedSeconds + 0.5, "the sound is no longer than the time that passed");

            var started = ParseUtc(dto.startedUtc);
            Assert.That(dto.startedUtc, Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$"), "ISO 8601 UTC with milliseconds");
            Assert.That((started - before).TotalSeconds, Is.InRange(-0.1, ClockToleranceSec), "the start of the recording is the wall clock of now, not of some other day");
            AssertVideoZero(dto, started);
            Assert.AreEqual(0, _quitCalls, "the app is already closing: the recorder does not quit it again");
        }

        [UnityTest]
        public IEnumerator DestroyingTheRunner_BeforeTheTimeIsUp_FinishesTheSameWay()
        {
            var recorder = Begin(LongRecordingSec);
            yield return new WaitForSecondsRealtime(RecordedSec);
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");

            Object.Destroy(_runnerObject);
            yield return null;

            Assert.IsTrue(recorder.IsFinished);
            var dto = ReadJson();
            Assert.IsTrue(dto.endedEarly);
            AssertTheThreeFilesAreConsistent(dto);
        }

        [UnityTest]
        public IEnumerator TheRecording_IsFinishedOnlyOnce_WhateverComesAfterTheQuit()
        {
            var recorder = Begin(LongRecordingSec);
            yield return new WaitForSecondsRealtime(RecordedSec);
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");
            _runnerObject.SendMessage("OnApplicationQuit");
            var jsonAtQuit = File.ReadAllBytes(Path.Combine(_dir, "recording.json"));
            var wavAtQuit = File.ReadAllBytes(Path.Combine(_dir, "audio.wav"));

            // after the quit: more frames pass, then the runner is destroyed (OnDestroy follows OnApplicationQuit in the player)
            yield return new WaitForSecondsRealtime(0.3f);
            recorder.FinishEarly();
            Object.Destroy(_runnerObject);
            yield return null;

            CollectionAssert.AreEqual(jsonAtQuit, File.ReadAllBytes(Path.Combine(_dir, "recording.json")), "recording.json is not written a second time");
            CollectionAssert.AreEqual(wavAtQuit, File.ReadAllBytes(Path.Combine(_dir, "audio.wav")), "nor is audio.wav");
            var framesOnDisk = Directory.GetFiles(Path.Combine(_dir, "frames"), "f*.jpg").Length;
            Assert.AreEqual(ReadJson().frames, framesOnDisk, "no picture is taken after the recording ended");
        }

        [UnityTest]
        public IEnumerator WhenTheTimeRunsOut_TheRecordingEndsByItself_AndIsNotMarkedEarly()
        {
            var recorder = Begin("1");
            var waited = 0f;
            while (!recorder.IsFinished && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");

            Assert.IsTrue(recorder.IsFinished, "the recording ended by itself");
            var dto = ReadJson();
            Assert.IsFalse(dto.endedEarly, "it was not cut short");
            Assert.That(dto.recordedSeconds, Is.InRange(1f, 5f));
            AssertTheThreeFilesAreConsistent(dto);
            AssertVideoZero(dto, ParseUtc(dto.startedUtc));
            Assert.AreEqual(1, _quitCalls, "when the time is up the app is asked to quit, once");
        }

        [UnityTest]
        public IEnumerator AFileThatCannotBeWritten_IsLogged_AndTheOtherTwoAreStillWritten_WhenTheAppIsClosed()
        {
            var recorder = Begin(LongRecordingSec);
            Directory.CreateDirectory(Path.Combine(_dir, "audio.wav")); // a folder where the sound should go
            yield return new WaitForSecondsRealtime(RecordedSec);
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");
            LogAssert.Expect(LogType.Error, new Regex(@"\[RunRecorder\] could not write .*audio\.wav"));

            _runnerObject.SendMessage("OnApplicationQuit"); // an exception here would be logged by Unity and fail the test

            Assert.IsTrue(recorder.IsFinished);
            Assert.IsTrue(Directory.Exists(Path.Combine(_dir, "audio.wav")), "the folder is still what it was");
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "frames.csv")), "frames.csv is written");
            var dto = ReadJson();
            Assert.IsTrue(dto.endedEarly, "and so is recording.json");
            Assert.Greater(dto.frames, 0);
            Assert.AreEqual(0, _quitCalls);
        }

        [UnityTest]
        public IEnumerator AFileThatCannotBeWritten_DoesNotKeepTheAppOpen_WhenTheTimeIsUp()
        {
            var recorder = Begin("1");
            Directory.CreateDirectory(Path.Combine(_dir, "audio.wav"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[RunRecorder\] could not write .*audio\.wav"));
            var waited = 0f;
            while (!recorder.IsFinished && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");

            Assert.IsTrue(recorder.IsFinished);
            Assert.AreEqual(1, _quitCalls, "the app is still asked to quit");
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "frames.csv")));
            Assert.IsFalse(ReadJson().endedEarly);
        }

        [UnityTest]
        public IEnumerator PicturesThatCannotBeWritten_AreCounted_AndTheRecordingStillEnds()
        {
            var recorder = Begin("3"); // about 90 pictures: more than the 64 the writer's queue holds, so a writer that gave up would block the main thread
            Directory.Delete(Path.Combine(_dir, "frames")); // the folder of the pictures is gone (still empty: the first picture is taken a frame later)
            LogAssert.Expect(LogType.Error, new Regex(@"\[RunRecorder\] could not write a picture"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[RunRecorder\] \d+ of \d+ pictures could not be written"));
            var waited = 0f;
            while (!recorder.IsFinished && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (HasNoAudioDevice()) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");

            Assert.IsTrue(recorder.IsFinished, "the recording ended: the main thread never got stuck behind a writer that gave up");
            Assert.AreEqual(1, _quitCalls);
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "audio.wav")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "recording.json")));
        }
    }
}
