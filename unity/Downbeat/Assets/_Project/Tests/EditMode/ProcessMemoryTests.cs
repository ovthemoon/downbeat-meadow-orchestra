using System.Runtime.InteropServices;
using Downbeat.App;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class ProcessMemoryTests
    {
        const long OneMegabyte = 1024L * 1024L;
        const long OneTerabyte = 1024L * 1024L * 1024L * 1024L;

        [Test]
        public void TheCountersStruct_HasTheSizeWindowsExpects_On64Bit()
        {
            // PROCESS_MEMORY_COUNTERS: two DWORDs and eight SIZE_T. The call refuses a size that is not the one of the struct, so a wrong layout would show as a working set of 0 on Windows only.
            Assume.That(System.IntPtr.Size, Is.EqualTo(8), "the Windows player is 64 bit");

            Assert.AreEqual(72, Marshal.SizeOf(typeof(ProcessMemory.Counters)));
        }

        [Test]
        public void OnThisMachine_TheWorkingSetOfTheEditor_IsAPlausibleNumber()
        {
            Assume.That(Application.platform, Is.EqualTo(RuntimePlatform.OSXEditor).Or.EqualTo(RuntimePlatform.LinuxEditor), "the Windows editor's Mono reports 0 (that is the bug this class works around in the player only)");

            var ok = ProcessMemory.TryGetWorkingSetBytes(out var bytes, out var problem);

            Assert.IsTrue(ok, problem);
            Assert.Greater(bytes, 50 * OneMegabyte, "an editor holds hundreds of megabytes");
            Assert.Less(bytes, OneTerabyte);
        }

        [Test]
        public void ARefusedCall_GivesZero_AndSaysWhy_InsteadOfThrowing()
        {
            var ok = ProcessMemory.TryGetWorkingSetBytes(() => throw new System.EntryPointNotFoundException("K32GetProcessMemoryInfo"), out var bytes, out var problem);

            Assert.IsFalse(ok);
            Assert.AreEqual(0L, bytes);
            StringAssert.Contains("K32GetProcessMemoryInfo", problem);
        }

        [Test]
        public void AnAnswerOfZero_IsReportedAsAProblem_NotAsAMeasurement()
        {
            var ok = ProcessMemory.TryGetWorkingSetBytes(() => 0L, out var bytes, out var problem);

            Assert.IsFalse(ok, "0 is what the old call gave on Windows all night: it must not look like a number");
            Assert.AreEqual(0L, bytes);
            Assert.IsNotEmpty(problem);
        }

        [Test]
        public void ASensibleAnswer_PassesThrough()
        {
            var ok = ProcessMemory.TryGetWorkingSetBytes(() => 380 * OneMegabyte, out var bytes, out var problem);

            Assert.IsTrue(ok);
            Assert.AreEqual(380 * OneMegabyte, bytes);
            Assert.IsNull(problem);
        }
    }
}
