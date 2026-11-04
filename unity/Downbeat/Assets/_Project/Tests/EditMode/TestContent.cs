using System.IO;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>Shared fixtures: the shipped content.json, and a minimal valid content object built in code.</summary>
    static class TestContent
    {
        public static string RepoContentPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"));

        public static string RepoReplayPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "replay", "demo-hands.json"));

        public static ContentDto LoadShipped() => JsonUtility.FromJson<ContentDto>(File.ReadAllText(RepoContentPath));

        public static InstrumentDto Instrument(string id, string part, int rootMidi, params SampleDto[] samples) =>
            new InstrumentDto { id = id, part = part, gainDb = 0f, rootMidi = rootMidi, maxPitchShift = 12, samples = samples };

        public static SampleDto Sample(string id, int soundingMidi, float tuneCents = 0f, float onsetMs = 0f) =>
            new SampleDto { id = id, file = id + ".wav", soundingMidi = soundingMidi, tuneCents = tuneCents, perceivedOnsetOffsetMs = onsetMs };

        public static ScaleSpec AMinorPentatonic() => new ScaleSpec(new[] { 0, 3, 5, 7, 10 }, 10);
    }
}
