using System;
using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>
    /// Entry points for the Day 0 test pages. Methods are called with SendMessage("Day0", name, jsonString).
    /// Results go back through Day0Bridge.Emit(kind, json). Test code only; to be discarded after Day 0.
    /// </summary>
    public sealed class Day0Controller : MonoBehaviour
    {
        [SerializeField] AudioClip clickClip;

        BeatTestRunner _beat;
        AheadTest _ahead;
        FrameWindowRunner _frames;
        int _sinkCalls;
        bool _sinkParse;

        void Awake()
        {
            Day0Bridge.Emit("ready", "{\"unity\":\"" + Application.unityVersion + "\",\"clip\":" + (clickClip != null ? "true" : "false") + "}");
        }

        void Update()
        {
            _beat?.Tick();
            _ahead?.Tick();
            _frames?.Tick(Time.unscaledDeltaTime);

            if (_beat != null && _beat.IsFinished && Time.realtimeSinceStartupAsDouble > _beatEndAt) FinishBeat();
            if (_ahead != null && _ahead.IsFinished) FinishAhead();
            if (_frames != null && _frames.IsFinished) FinishFrames();
        }

        double _beatEndAt;

        // ---- SendMessage entry points -------------------------------------------------------------

        public void Ping(string _) => Day0Bridge.Emit("pong", "{\"frame\":" + Time.frameCount + ",\"dsp\":" + AudioSettings.dspTime.ToString("R") + "}");

        public void BeatStart(string json)
        {
            if (!Guard(_beat == null, "beat already running")) return;
            var args = Parse<BeatArgs>(json);
            if (args == null) return;
            try
            {
                _beat = new BeatTestRunner(gameObject, clickClip, args);
                _beatEndAt = Time.realtimeSinceStartupAsDouble + args.startDelaySec + args.durationSec + 0.5;
            }
            catch (Exception e)
            {
                Fail("BeatStart", e);
            }
        }

        public void BeatStop(string _) => FinishBeat();

        /// <summary>Application.runInBackground. The Unity template ships with it OFF, which pauses the player whenever the page loses focus.</summary>
        public void SetRunInBackground(string json)
        {
            var args = Parse<RunInBackgroundArgs>(json);
            if (args == null) return;
            Application.runInBackground = args.value;
            Day0Bridge.Emit("runInBackground", "{\"value\":" + (Application.runInBackground ? "true" : "false") + "}");
        }

        /// <summary>QualitySettings.vSyncCount: 1 = every display refresh, 2 = every second refresh (60 fps on a 120 Hz display).</summary>
        public void SetVsync(string json)
        {
            var args = Parse<VsyncArgs>(json);
            if (args == null) return;
            QualitySettings.vSyncCount = args.count;
            Day0Bridge.Emit("vsync", "{\"count\":" + QualitySettings.vSyncCount + "}");
        }

        public void AheadStart(string json)
        {
            if (!Guard(_ahead == null, "ahead test already running")) return;
            var args = Parse<AheadArgs>(json);
            if (args == null) return;
            try { _ahead = new AheadTest(gameObject, clickClip, args); }
            catch (Exception e) { Fail("AheadStart", e); }
        }

        public void FramesStart(string json)
        {
            if (!Guard(_frames == null, "frame window already running")) return;
            var args = Parse<FrameWindowArgs>(json);
            if (args == null) return;
            _frames = new FrameWindowRunner(args);
        }

        public void BridgeBenchStart(string json)
        {
            var args = Parse<BridgeArgs>(json);
            if (args == null) return;
            try { Day0Bridge.Emit("bridge", JsonUtility.ToJson(BridgeBench.Run(args.calls))); }
            catch (Exception e) { Fail("BridgeBench", e); }
        }

        public void SinkConfig(string json)
        {
            var args = Parse<SinkArgs>(json);
            if (args == null) return;
            _sinkParse = args.parse;
            _sinkCalls = 0;
        }

        /// <summary>Target for the SendMessage cost test (JS pushes a JSON frame per call).</summary>
        public void SinkFrame(string json)
        {
            _sinkCalls++;
            if (_sinkParse) JsonUtility.FromJson<HandFrameJson>(json);
        }

        public void SinkReport(string _) => Day0Bridge.Emit("sink", "{\"calls\":" + _sinkCalls + ",\"parse\":" + (_sinkParse ? "true" : "false") + "}");

        // ---- helpers ------------------------------------------------------------------------------

        void FinishBeat()
        {
            if (_beat == null) return;
            var summary = _beat.Finish();
            _beat = null;
            Day0Bridge.Emit("beat", JsonUtility.ToJson(summary));
        }

        void FinishAhead()
        {
            var summary = _ahead.Finish();
            _ahead = null;
            Day0Bridge.Emit("ahead", JsonUtility.ToJson(summary));
        }

        void FinishFrames()
        {
            var summary = _frames.Finish();
            _frames = null;
            Day0Bridge.Emit("frames", JsonUtility.ToJson(summary));
        }

        static T Parse<T>(string json) where T : class
        {
            try
            {
                return string.IsNullOrEmpty(json) ? Activator.CreateInstance<T>() : JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Day0Bridge.Emit("error", "{\"where\":\"Parse " + typeof(T).Name + "\",\"message\":\"" + Escape(e.Message) + "\"}");
                return null;
            }
        }

        static bool Guard(bool ok, string message)
        {
            if (!ok) Day0Bridge.Emit("error", "{\"where\":\"guard\",\"message\":\"" + message + "\"}");
            return ok;
        }

        static void Fail(string where, Exception e) =>
            Day0Bridge.Emit("error", "{\"where\":\"" + where + "\",\"message\":\"" + Escape(e.Message) + "\"}");

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }
}
