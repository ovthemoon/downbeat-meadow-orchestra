using System.Collections.Generic;
using System.Globalization;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The JSON of a native clock run has to be readable by tools/analyze_s1.mjs: these are the keys and shapes that script reads.</summary>
    public class ClockRunMetaTests
    {
        static ClockRunData Sample() => new ClockRunData
        {
            Label = "native-scheduled__calm",
            Path = "native-scheduled",
            RunDir = "native-clock/run1",
            RawFile = "native-clock/run1/raw-audio/native-scheduled__calm.ctx0-native.f32",
            Bpm = 96, SlotsPerBeat = 4, DurationSec = 60, LookaheadSec = 0.1, StartDelaySec = 0.6,
            StartedPerfMs = 1000, EndedPerfMs = 62000,
            SampleRate = 48000, FirstFrame = 4800000, Frames = 2900000, Peak = 0.58, Gaps = 0, OutputLatencySec = 0.085, BaseLatencySec = 0.0213,
            StartMarkPerfMs = 1100, StartMarkDsp = 100.5, EndMarkPerfMs = 61000, EndMarkDsp = 160.5,
            TotalSlots = 384, Issued = 384, SkippedLate = 0, OriginDsp = 101.1, OriginPerfMs = 1700, FrameCount = 3600, MaxDspStallSec = 0.03,
            FrameTimes = new FrameSummary(3600, 16.7, 16.6, 17.5, 18, 55, 3, 1), MinFrameMs = 15.9,
            Records = new List<ClockScheduleRecord> { new ClockScheduleRecord(0, 101.1, 101.0, 1650.5) },
            Blocks = new List<ClockBlockRecord> { new ClockBlockRecord(3000, 50.2) },
            Runtime = new Dictionary<string, string> { { "unity", "6000.3.9f1" }, { "quote\"key", "va\\lue" } },
        };

        [Test]
        public void Json_HasTheTopLevelKeysAnalyzeS1Reads()
        {
            var json = ClockRunMeta.ToJson(Sample());

            foreach (var key in new[] { "\"config\":", "\"startedPerfMs\":", "\"contexts\":", "\"marks\":", "\"pageEvents\":", "\"blocks\":", "\"rafIntervalsMs\":", "\"summary\":", "\"unityEvents\":" })
            {
                StringAssert.Contains(key, json);
            }
        }

        [Test]
        public void Json_HasTheContextAndMarkFieldsThatMapOnsetsToTime()
        {
            var json = ClockRunMeta.ToJson(Sample());

            StringAssert.Contains("\"rawFile\":\"native-clock/run1/raw-audio/native-scheduled__calm.ctx0-native.f32\"", json);
            StringAssert.Contains("\"firstFrame\":4800000", json);
            StringAssert.Contains("\"sampleRate\":48000", json);
            StringAssert.Contains("\"label\":\"beat-start:native-scheduled__calm:native-scheduled\",\"perfMs\":1100,\"ctxTimes\":{\"0\":100.5}", json);
            StringAssert.Contains("\"label\":\"beat-end:native-scheduled__calm:native-scheduled\"", json);
        }

        [Test]
        public void Json_HasTheSchedulerSummaryCounts()
        {
            var json = ClockRunMeta.ToJson(Sample());

            StringAssert.Contains("\"totalSlots\":384", json);
            StringAssert.Contains("\"issued\":384", json);
            StringAssert.Contains("\"skippedLate\":0", json);
            StringAssert.Contains("\"maxDspStallSec\":0.03", json);
            StringAssert.Contains("{\"slot\":0,\"targetDsp\":101.1,\"dspAtCall\":101,\"perfMsAtCall\":1650.5}", json);
        }

        [Test]
        public void Json_EscapesQuotesAndBackslashesInStrings()
        {
            var json = ClockRunMeta.ToJson(Sample());

            StringAssert.Contains("\"quote\\\"key\":\"va\\\\lue\"", json);
        }

        [Test]
        public void Json_UsesADotAsDecimalSeparator_EvenWhenTheCultureUsesAComma()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var json = ClockRunMeta.ToJson(Sample());

                StringAssert.Contains("\"lookaheadSec\":0.1", json);
                StringAssert.DoesNotContain("0,1", json);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void Json_WritesNullForNonFiniteNumbers()
        {
            var data = Sample();
            data.Peak = double.NaN;

            var json = ClockRunMeta.ToJson(data);

            StringAssert.Contains("\"peak\":null", json);
        }
    }
}
