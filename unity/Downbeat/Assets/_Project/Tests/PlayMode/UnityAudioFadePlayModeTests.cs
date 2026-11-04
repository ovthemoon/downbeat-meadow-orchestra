using System.Collections;
using System.IO;
using Downbeat.Audio;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>The real audio system: a voice that has to give way is faded out and its source is stopped afterwards, with no hard stop of a sounding voice.</summary>
    public class UnityAudioFadePlayModeTests
    {
        const float LoadTimeoutSec = 20f;
        const float SoundingSec = 0.35f;
        const float AfterFadeSec = 0.3f;
        const double FirstNoteLeadSec = 0.1;
        const double SecondNoteLeadSec = 0.6;

        static string SampleUrl
        {
            get
            {
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "audio", "harp_A4.wav"));
                return new System.Uri(path).AbsoluteUri;
            }
        }

        static string AudioUrl(string file) => new System.Uri(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "audio", file))).AbsoluteUri;

        [UnityTest]
        public IEnumerator FadeOutAll_FadesThePlayersVoice_AndLeavesTheNatureVoiceSounding()
        {
            var backend = new UnityAudioBackend();
            var engine = new NativeAudioEngine(backend);
            engine.Configure(0.01f, 4);
            engine.LoadSamples(new[] { new SampleLoadRequest(0, AudioUrl("harp_A4.wav")), new SampleLoadRequest(1, AudioUrl("pad_A3.wav"), isNature: true) });
            var waited = 0f;
            while (!engine.IsRunning && waited < LoadTimeoutSec)
            {
                engine.Tick();
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!engine.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");
            Assert.IsTrue(engine.IsRunning, "the samples did not load");

            var playerVoice = backend.Host.transform.Find("Voices/Voice0").GetComponent<AudioSource>();
            var natureVoice = backend.Host.transform.Find("Voices/Voice1").GetComponent<AudioSource>();
            engine.Schedule(0, engine.Now + FirstNoteLeadSec, 1.0, 1.0); // the player's note (1.3 s) on the first source
            engine.Schedule(1, engine.Now + FirstNoteLeadSec, 1.0, 1.0); // the pad (5.6 s) on the second
            var until = Time.realtimeSinceStartup + SoundingSec;
            while (Time.realtimeSinceStartup < until)
            {
                engine.Tick();
                yield return null;
            }
            Assert.IsTrue(playerVoice.isPlaying && natureVoice.isPlaying, "both are sounding");

            engine.FadeOutAll(0.15);
            until = Time.realtimeSinceStartup + AfterFadeSec + 0.15f;
            while (Time.realtimeSinceStartup < until)
            {
                engine.Tick();
                yield return null;
            }

            Assert.IsFalse(playerVoice.isPlaying, "the player's voice faded out and its source was stopped, long before the note was over");
            Assert.IsTrue(natureVoice.isPlaying, "the nature voice was not touched");
            Object.Destroy(backend.Host);
        }

        [UnityTest]
        public IEnumerator AVoiceThatGivesWay_FadesOutAndItsSourceIsStoppedAfterwards()
        {
            var backend = new UnityAudioBackend();
            var engine = new NativeAudioEngine(backend);
            engine.Configure(0.01f, 1); // one voice may sound at a time (plus the spares)
            engine.LoadSamples(new[] { new SampleLoadRequest(0, SampleUrl) });
            var waited = 0f;
            while (!engine.IsRunning && waited < LoadTimeoutSec)
            {
                engine.Tick();
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!engine.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run)");
            Assert.IsTrue(engine.IsRunning, "the sample did not load");

            var firstVoice = backend.Host.transform.Find("Voices/Voice0").GetComponent<AudioSource>();
            engine.Schedule(0, engine.Now + FirstNoteLeadSec, 1.0, 1.0); // the first note goes to the first source
            var until = Time.realtimeSinceStartup + SoundingSec;
            while (Time.realtimeSinceStartup < until)
            {
                engine.Tick();
                yield return null;
            }
            Assert.IsTrue(firstVoice.isPlaying, "the first note is sounding");

            engine.Schedule(0, engine.Now + SecondNoteLeadSec, 1.0, 1.0); // a second one that is heard while the first still rings: the first gives way

            Assert.AreEqual(1, engine.Stats.Stolen);
            until = Time.realtimeSinceStartup + AfterFadeSec;
            while (Time.realtimeSinceStartup < until)
            {
                engine.Tick();
                yield return null;
            }
            Assert.IsFalse(firstVoice.isPlaying, "the faded voice was stopped once it was silent, so its source can be used again");
            Object.Destroy(backend.Host);
        }
    }
}
