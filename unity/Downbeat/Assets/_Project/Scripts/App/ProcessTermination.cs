using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Downbeat.App
{
    /// <summary>
    /// Ends this process at once, from any thread, with an exit code, without asking anything of the main thread. <c>Application.Quit</c> would wait for the main thread, which is the one that is frozen. Windows:
    /// <c>TerminateProcess</c>; Mac and Linux: libc <c>_exit</c> (so that a supervisor sees the code there too, 71 or 72, and not a signal). Neither runs the shutdown of Unity or any destructor: the files that matter
    /// were written before. If the native call cannot be made, <c>Process.Kill</c> ends it instead (a non-zero status, but not the code). It never ends the editor: in the editor this does nothing.
    /// </summary>
    public static class ProcessTermination
    {
        /// <summary>Does not return in a player.</summary>
        public static void Terminate(int exitCode)
        {
#if UNITY_EDITOR
            // a game that ends the editor it runs in would lose the developer's work; the guard that calls this is not made in the editor either
            return;
#else
            try
            {
#if UNITY_STANDALONE_WIN
                Kernel32.TerminateProcess(Kernel32.GetCurrentProcess(), (uint)exitCode);   // does not return when it works
#else
                LibC.Exit(exitCode);   // does not return
#endif
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is MarshalDirectiveException)
            {
                // the native call is not there: the portable way below still ends the process
            }
            using (var self = Process.GetCurrentProcess()) self.Kill();   // when the native call did not end it
#endif
        }

#if !UNITY_EDITOR && !UNITY_STANDALONE_WIN
        static class LibC
        {
            [DllImport("libc", EntryPoint = "_exit")]
            public static extern void Exit(int status);
        }
#endif
    }
}
