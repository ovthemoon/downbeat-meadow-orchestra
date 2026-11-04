using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Downbeat.App
{
    /// <summary>
    /// The working set of this process, for the <c>working_set_mb</c> column of the health log. <c>Process.WorkingSet64</c> gives 0 under Unity's Mono on Windows (every Windows health log so far), so a Windows
    /// player asks Windows itself (<c>K32GetProcessMemoryInfo</c> in kernel32, the call the Task Manager's number comes from); everywhere else <c>Process</c> is right and stays.
    /// </summary>
    public static class ProcessMemory
    {
        /// <summary>
        /// <c>PROCESS_MEMORY_COUNTERS</c> of the Windows API. It is public and defined on every platform only so that a test on the Mac can check its size (<c>cb</c> must be exactly the size of the struct, or the call
        /// fails and the column would stay at 0 on Windows only).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Counters
        {
            public uint Cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }

        /// <summary>The working set in bytes. False (with 0 and the reason) when it cannot be read or the answer is 0, which no running process has: a 0 must not pass for a measurement.</summary>
        public static bool TryGetWorkingSetBytes(out long bytes, out string problem) => TryGetWorkingSetBytes(ReadForThisPlatform, out bytes, out problem);

        /// <summary>The same with the reader passed in (tests).</summary>
        public static bool TryGetWorkingSetBytes(Func<long> read, out long bytes, out string problem)
        {
            bytes = 0L;
            try
            {
                var value = read();
                if (value > 0L)
                {
                    bytes = value;
                    problem = null;
                    return true;
                }
                problem = "the operating system answered 0";
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is MarshalDirectiveException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is NotSupportedException || e is PlatformNotSupportedException)
            {
                problem = $"{e.GetType().Name}: {e.Message}";
            }
            return false;
        }

        static long ReadForThisPlatform()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var counters = new Counters { Cb = (uint)Marshal.SizeOf(typeof(Counters)) };   // by ref: the call reads cb to know which version of the struct it is given
            if (!Kernel32.K32GetProcessMemoryInfo(Kernel32.GetCurrentProcess(), ref counters, counters.Cb)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return (long)counters.WorkingSetSize.ToUInt64();
#else
            using (var process = Process.GetCurrentProcess()) return process.WorkingSet64;
#endif
        }
    }
}
