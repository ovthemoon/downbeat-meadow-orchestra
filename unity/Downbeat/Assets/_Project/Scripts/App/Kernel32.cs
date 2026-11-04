#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace Downbeat.App
{
    /// <summary>The kernel32 functions that <see cref="ProcessTermination"/> and <see cref="ProcessMemory"/> call, declared once. (The power request of <see cref="KeepAwake"/> has its own.) Only in a Windows player.</summary>
    static class Kernel32
    {
        /// <summary>A pseudo handle for this process; it needs no closing.</summary>
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool K32GetProcessMemoryInfo(IntPtr process, ref ProcessMemory.Counters counters, uint size);
    }
}
#endif
