using System.Runtime.InteropServices;

namespace Downbeat.App
{
    /// <summary>What Unity tells the hosting page (start screen) and what it reads from the page URL. No-ops outside a Web build.</summary>
    public static class PagePort
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void Downbeat_Page_Ready();
        [DllImport("__Internal")] static extern void Downbeat_Page_Status(string message);
        [DllImport("__Internal")] static extern string Downbeat_Page_Query(string key);
        [DllImport("__Internal")] static extern void Downbeat_Page_InputMode(string label);
        [DllImport("__Internal")] static extern double Downbeat_Page_NowMs();
        [DllImport("__Internal")] static extern void Downbeat_Page_FrameTick();
        [DllImport("__Internal")] static extern int Downbeat_Hands_Read(double[] destination, int length);
        [DllImport("__Internal")] static extern void Downbeat_Page_LogNote(double part, double step, double velocity, double slot, double requestedAt, double when, double audioNow,
            double captureMs, double inferEndMs, double receivedMs, double decidedMs, double triggerCaptureMs);

        public static void Ready() => Downbeat_Page_Ready();
        public static void Status(string message) => Downbeat_Page_Status(message);
        public static string Query(string key) => Downbeat_Page_Query(key);
        public static void InputMode(string label) => Downbeat_Page_InputMode(label);
        public static double NowMs() => Downbeat_Page_NowMs();
        /// <summary>Once per Unity frame; the page records the time only while a measurement script has switched it on (page.frameTimes).</summary>
        public static void FrameTick() => Downbeat_Page_FrameTick();
        /// <summary>Copies the latest hand frame into <paramref name="destination"/>; returns its sequence number, or -1 when the page has no tracker.</summary>
        public static int ReadHands(double[] destination) => Downbeat_Hands_Read(destination, destination.Length);
        public static void LogNote(double part, double step, double velocity, double slot, double requestedAt, double when, double audioNow,
            double captureMs, double inferEndMs, double receivedMs, double decidedMs, double triggerCaptureMs) =>
            Downbeat_Page_LogNote(part, step, velocity, slot, requestedAt, when, audioNow, captureMs, inferEndMs, receivedMs, decidedMs, triggerCaptureMs);
#else
        public static void Ready() { }
        public static void Status(string message) { UnityEngine.Debug.Log("[page] " + message); }
        public static string Query(string key) => string.Empty;
        public static void InputMode(string label) { UnityEngine.Debug.Log("[page] input: " + label); }
        public static double NowMs() => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0;
        public static void FrameTick() { }
        public static int ReadHands(double[] destination) => -1;
        public static void LogNote(double part, double step, double velocity, double slot, double requestedAt, double when, double audioNow,
            double captureMs, double inferEndMs, double receivedMs, double decidedMs, double triggerCaptureMs) { }
#endif
    }
}
