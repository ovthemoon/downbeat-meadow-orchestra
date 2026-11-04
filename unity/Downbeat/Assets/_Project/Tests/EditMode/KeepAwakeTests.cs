using System;
using System.Collections.Generic;
using Downbeat.App;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class KeepAwakeTests
    {
        const uint Continuous = 0x80000000u;
        const uint SystemRequired = 0x00000001u;
        const uint DisplayRequired = 0x00000002u;

        sealed class FakeNative
        {
            public readonly List<uint> Calls = new List<uint>();
            public uint Result = Continuous;      // what the real call returns: the previous state, 0 when it failed
            public bool Throws;

            public uint Call(uint flags)
            {
                Calls.Add(flags);
                if (Throws) throw new EntryPointNotFoundException("SetThreadExecutionState");
                return Result;
            }
        }

        [Test]
        public void OnTheWindowsPlayer_Enter_AsksForSystemAndDisplayToStayOn_Continuously()
        {
            var native = new FakeNative();
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);

            var entered = awake.Enter();

            Assert.IsTrue(entered);
            Assert.IsTrue(awake.IsActive);
            CollectionAssert.AreEqual(new[] { Continuous | SystemRequired | DisplayRequired }, native.Calls);
        }

        [Test]
        public void Release_GivesTheRequestBack_WithContinuousAlone()
        {
            var native = new FakeNative();
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);
            awake.Enter();

            awake.Release();

            Assert.IsFalse(awake.IsActive);
            Assert.AreEqual(Continuous, native.Calls[native.Calls.Count - 1], "ES_CONTINUOUS alone clears the request");
        }

        [Test]
        public void Refresh_AsksAgain_WhileActive_AndNeverWhenNot()
        {
            var native = new FakeNative();
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);

            awake.Refresh();
            Assert.IsEmpty(native.Calls, "nothing was requested yet");
            awake.Enter();
            awake.Refresh();
            awake.Refresh();
            Assert.AreEqual(3, native.Calls.Count);
            Assert.AreEqual(native.Calls[0], native.Calls[2]);
            awake.Release();
            awake.Refresh();
            Assert.AreEqual(4, native.Calls.Count, "only the release came after Release");
        }

        [Test]
        public void Release_WithoutEnter_DoesNothing_AndEnterTwiceIsOneRequest()
        {
            var native = new FakeNative();
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);

            awake.Release();
            Assert.IsEmpty(native.Calls);
            awake.Enter();
            awake.Enter();
            Assert.AreEqual(2, native.Calls.Count, "the second Enter just asks again");
            Assert.IsTrue(awake.IsActive);
        }

        [TestCase(RuntimePlatform.OSXPlayer)]
        [TestCase(RuntimePlatform.OSXEditor)]
        [TestCase(RuntimePlatform.WindowsEditor)]
        [TestCase(RuntimePlatform.LinuxPlayer)]
        [TestCase(RuntimePlatform.WebGLPlayer)]
        public void OnAnyOtherPlatform_NothingIsEverCalled(RuntimePlatform platform)
        {
            var native = new FakeNative();
            var awake = new KeepAwake(platform, native.Call);

            Assert.IsFalse(awake.Enter());
            awake.Refresh();
            awake.Release();

            Assert.IsFalse(awake.IsActive);
            Assert.IsEmpty(native.Calls, $"{platform}: the machine's power state is not touched");
            Assert.IsFalse(KeepAwake.IsSupported(platform));
        }

        [Test]
        public void OnlyTheWindowsPlayerIsSupported()
        {
            Assert.IsTrue(KeepAwake.IsSupported(RuntimePlatform.WindowsPlayer));
            Assert.IsFalse(KeepAwake.IsSupported(RuntimePlatform.WindowsEditor), "the editor never changes the developer's power state");
        }

        [Test]
        public void ARefusedRequest_IsReportedNotThrown()
        {
            var native = new FakeNative { Result = 0u };
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);

            Assert.IsFalse(awake.Enter());
            Assert.IsFalse(awake.IsActive);
            Assert.IsNotEmpty(awake.LastProblem);
        }

        [Test]
        public void ARefusedFirstEnter_IsAskedAgainByRefresh_UntilWindowsSaysYes()
        {
            var native = new FakeNative { Result = 0u };
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);
            Assert.IsFalse(awake.Enter());

            awake.Refresh();
            Assert.AreEqual(2, native.Calls.Count, "the request is not given up after one refusal");
            Assert.IsFalse(awake.IsActive);

            native.Result = KeepAwake.EsContinuous;   // Windows accepts now
            awake.Refresh();
            Assert.IsTrue(awake.IsActive);
            Assert.IsEmpty(awake.LastProblem);
        }

        [Test]
        public void AfterRelease_RefreshNeverAsksAgain_EvenIfTheFirstEnterWasRefused()
        {
            var native = new FakeNative { Result = 0u };
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);
            awake.Enter();
            awake.Release();
            var calls = native.Calls.Count;

            awake.Refresh();

            Assert.AreEqual(calls, native.Calls.Count);
            Assert.IsFalse(awake.IsWanted);
        }

        [Test]
        public void AMissingNativeFunction_IsReportedNotThrown()
        {
            var native = new FakeNative { Throws = true };
            var awake = new KeepAwake(RuntimePlatform.WindowsPlayer, native.Call);

            Assert.DoesNotThrow(() => awake.Enter());
            Assert.DoesNotThrow(() => awake.Refresh());
            Assert.DoesNotThrow(() => awake.Release());
            Assert.IsFalse(awake.IsActive);
            StringAssert.Contains("SetThreadExecutionState", awake.LastProblem);
        }

        [Test]
        public void Constructor_RejectsNoNativeFunction()
        {
            Assert.Throws<ArgumentNullException>(() => new KeepAwake(RuntimePlatform.WindowsPlayer, null));
        }
    }
}
