using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    public class LatencyMarkerPlayModeTests
    {
        [Test]
        public void Begin_WithoutTheFlag_DoesNothing()
        {
            Assert.IsNull(LatencyMarker.Begin(LaunchArgs.Parse(new[] { "--mute" })));
        }

        [Test]
        public void Begin_WithTheFlag_CountsEveryHeardNote()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"\[latency\] latency mode"));
            var marker = LatencyMarker.Begin(LaunchArgs.Parse(new[] { "--latency-mode" }));
            try
            {
                Assert.IsNotNull(marker);
                marker.NoteHeard();
                marker.NoteHeard();

                Assert.AreEqual(2, marker.Flashes);
            }
            finally
            {
                Object.DestroyImmediate(marker.gameObject);
            }
        }
    }
}
