using System.Runtime.InteropServices;
using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>
    /// Thin wrapper over Day0Bridge.jslib. Outside a WebGL player (Editor, EditMode tests) it falls back to harmless stubs.
    /// </summary>
    public static class Day0Bridge
    {
        /// <summary>Length of the hand buffer: 5 header values + 2 hands x 65 (plan section 5.2).</summary>
        public const int HandBufferLength = 135;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern double Day0_NowMs();
        [DllImport("__Internal")] static extern double Day0_CtxTime();
        [DllImport("__Internal")] static extern void Day0_Emit(string kind, string json);
        [DllImport("__Internal")] static extern void Day0_Mark(string label);
        [DllImport("__Internal")] static extern int Day0_ReadHands(double[] dst, int length);
        [DllImport("__Internal")] static extern int Day0_FillTest(double[] dst, int length);
        [DllImport("__Internal")] static extern string Day0_ReadHandsJson();

        public static double NowMs() => Day0_NowMs();
        public static double CtxTime() => Day0_CtxTime();
        public static void Emit(string kind, string json) => Day0_Emit(kind, json);
        public static void Mark(string label) => Day0_Mark(label);
        public static int ReadHands(double[] dst) => Day0_ReadHands(dst, dst.Length);
        public static int FillTest(double[] dst) => Day0_FillTest(dst, dst.Length);
        public static string ReadHandsJson() => Day0_ReadHandsJson();
#else
        public static double NowMs() => Time.realtimeSinceStartupAsDouble * 1000.0;
        public static double CtxTime() => -1;
        public static void Emit(string kind, string json) => Debug.Log($"[day0:{kind}] {json}");
        public static void Mark(string label) => Debug.Log($"[day0:mark] {label}");
        public static int ReadHands(double[] dst) => -1;
        public static int FillTest(double[] dst) => -1;
        public static string ReadHandsJson() => "[]";
#endif
    }
}
