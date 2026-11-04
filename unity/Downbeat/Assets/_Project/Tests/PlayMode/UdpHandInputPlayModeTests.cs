using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>The real scene with the default hand input: a datagram sent to 127.0.0.1 by "the tracker" shows up as a hand, and the port is given back.</summary>
    public class UdpHandInputPlayModeTests
    {
        const int MainSceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float HandWaitSec = 2f;
        const int TrackerPort = HandLinkSettings.DefaultPort;

        static string FixtureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "tests", "fixtures", "hand-udp-v1"));

        static IEnumerator LoadAndWaitUntilRunning(System.Action<DownbeatLifetimeScope, DownbeatApp> done)
        {
            yield return SceneManager.LoadSceneAsync(MainSceneBuildIndex, LoadSceneMode.Single);
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            done(scope, app);
        }

        static void SendToTracker(byte[] packet)
        {
            using (var client = new UdpClient())
            {
                client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, TrackerPort));
            }
        }

        [UnityTest]
        public IEnumerator A_datagram_from_the_tracker_shows_up_as_hands_in_the_motion_view()
        {
            DownbeatLifetimeScope scope = null;
            yield return LoadAndWaitUntilRunning((s, a) => scope = s);
            var motion = scope.Container.Resolve<IHandMotionView>();
            var source = scope.Container.Resolve<IHandSource>();
            Assert.IsInstanceOf<UdpHandSource>(source, "the default input mode listens to the hand tracker");

            SendToTracker(File.ReadAllBytes(Path.Combine(FixtureDir, "two-hands.bin")));
            var waited = 0f;
            while (motion.Count == 0 && waited < HandWaitSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(2, motion.Count, "both hands of the datagram are in view");
            Assert.AreEqual(TrackerState.Running, source.State);
        }

        [UnityTest]
        public IEnumerator The_udp_port_is_released_when_the_scene_goes_away()
        {
            DownbeatLifetimeScope scope = null;
            yield return LoadAndWaitUntilRunning((s, a) => scope = s);

            Object.Destroy(scope.gameObject);
            yield return null;
            yield return null;

            using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                Assert.DoesNotThrow(() => probe.Bind(new IPEndPoint(IPAddress.Loopback, TrackerPort)), "the hand source must give the port back");
            }
        }
    }
}
