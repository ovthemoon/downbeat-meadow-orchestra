using System;
using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>S-2: cost of the JS bridge per call, managed allocation, and whether JS writes into a C# array are visible.</summary>
    public static class BridgeBench
    {
        const double MsToUs = 1000.0;
        const int GrowthProbeBytes = 48 * 1024 * 1024;

        public static BridgeSummary Run(int calls)
        {
            if (calls < 1) throw new ArgumentOutOfRangeException(nameof(calls));
            var buffer = new double[Day0Bridge.HandBufferLength];
            var result = new BridgeSummary { calls = calls };

            // Bare call overhead: a function that only returns performance.now().
            double sink = 0;
            var t0 = Day0Bridge.NowMs();
            for (var i = 0; i < calls; i++) sink += Day0Bridge.NowMs();
            result.nowMsLoopMs = Day0Bridge.NowMs() - t0;
            result.nowMsUsPerCall = result.nowMsLoopMs * MsToUs / calls;

            // The real pull: copy 135 doubles from JS into the managed array.
            var mem0 = GC.GetTotalMemory(false);
            t0 = Day0Bridge.NowMs();
            var seqSum = 0L;
            for (var i = 0; i < calls; i++) seqSum += Day0Bridge.ReadHands(buffer);
            result.readHandsLoopMs = Day0Bridge.NowMs() - t0;
            result.readHandsUsPerCall = result.readHandsLoopMs * MsToUs / calls;
            result.managedBytesDuringReadHands = GC.GetTotalMemory(false) - mem0;

            // Write-back visibility: JS fills a known pattern, C# verifies it, before and after a memory growth.
            result.alignmentError = Day0Bridge.FillTest(buffer) < 0 ? 1 : 0;
            result.writeBackOk = PatternOk(buffer);
            var big = new byte[GrowthProbeBytes];
            big[big.Length - 1] = 1;
            Array.Clear(buffer, 0, buffer.Length);
            Day0Bridge.FillTest(buffer);
            result.writeBackAfterGrowthOk = PatternOk(buffer) && big[big.Length - 1] == 1;

            // The rejected alternative: pass the frame as a JSON string (allocation per call).
            var jsonCalls = Math.Max(1, calls / 10);
            var mem1 = GC.GetTotalMemory(false);
            t0 = Day0Bridge.NowMs();
            var length = 0;
            for (var i = 0; i < jsonCalls; i++) length = Day0Bridge.ReadHandsJson().Length;
            result.jsonLoopMs = Day0Bridge.NowMs() - t0;
            result.jsonUsPerCall = result.jsonLoopMs * MsToUs / jsonCalls;
            result.managedBytesDuringJson = GC.GetTotalMemory(false) - mem1;
            result.jsonLength = length;

            if (sink < 0 || seqSum == long.MinValue) Debug.Log("unreachable"); // keep the loops observable
            return result;
        }

        static bool PatternOk(double[] buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (Math.Abs(buffer[i] - (i * 1.5 + 0.25)) > 1e-12) return false;
            }
            return true;
        }
    }
}
