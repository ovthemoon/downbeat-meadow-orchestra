using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// Keeps a Windows PC from going to sleep or turning its display off while the app runs, without touching any power setting: the app asks Windows
    /// (<c>SetThreadExecutionState</c>) to stay awake for as long as it runs, and gives the request back when it quits (or the process ends, which also ends it). Only the Windows
    /// player ever asks; every other platform, including the Windows editor, is left alone. The native call is passed in so that this logic is tested without Windows.
    /// </summary>
    public sealed class KeepAwake
    {
        public const uint EsContinuous = 0x80000000u;
        public const uint EsSystemRequired = 0x00000001u;
        public const uint EsDisplayRequired = 0x00000002u;

        const uint StayAwake = EsContinuous | EsSystemRequired | EsDisplayRequired;

        readonly bool _supported;
        readonly Func<uint, uint> _setThreadExecutionState;

        /// <param name="setThreadExecutionState">Takes the flags, returns the previous state, or 0 when Windows refused.</param>
        public KeepAwake(RuntimePlatform platform, Func<uint, uint> setThreadExecutionState)
        {
            _setThreadExecutionState = setThreadExecutionState ?? throw new ArgumentNullException(nameof(setThreadExecutionState));
            _supported = IsSupported(platform);
        }

        public static bool IsSupported(RuntimePlatform platform) => platform == RuntimePlatform.WindowsPlayer;

        public bool IsActive { get; private set; }
        /// <summary>Whether the app wants the PC awake: from <see cref="Enter"/> until <see cref="Release"/>, even while Windows has refused it so far.</summary>
        public bool IsWanted { get; private set; }
        /// <summary>What went wrong with the last request (empty when nothing did).</summary>
        public string LastProblem { get; private set; } = string.Empty;

        /// <summary>Asks Windows to stay awake. False (and <see cref="LastProblem"/>) when this platform does not do that or Windows said no.</summary>
        public bool Enter()
        {
            if (!_supported) return false;
            IsWanted = true;
            IsActive = Request(StayAwake);
            return IsActive;
        }

        /// <summary>
        /// Asks again while the app wants the PC awake: it keeps the request fresh after a driver or session change, and a request that Windows refused the first time is tried again
        /// (and goes active when Windows says yes), instead of being given up for the rest of the night.
        /// </summary>
        public void Refresh()
        {
            if (!IsWanted) return;
            IsActive = Request(StayAwake);
        }

        /// <summary>Gives the request back: Windows may sleep again.</summary>
        public void Release()
        {
            var wasActive = IsActive;
            IsWanted = false;
            IsActive = false;
            if (wasActive) Request(EsContinuous);
        }

        bool Request(uint flags)
        {
            try
            {
                if (_setThreadExecutionState(flags) != 0u) { LastProblem = string.Empty; return true; }
                LastProblem = "SetThreadExecutionState was refused by Windows";
            }
            catch (Exception e) when (e is EntryPointNotFoundException || e is DllNotFoundException || e is ExternalException || e is MarshalDirectiveException)
            {
                LastProblem = "SetThreadExecutionState is not available: " + e.Message;
            }
            return false;
        }
    }

    /// <summary>The Windows function behind <see cref="KeepAwake"/>. It exists only in a Windows player; anywhere else the call returns 0 without doing anything.</summary>
    public static class NativePower
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint esFlags);

        public static uint SetState(uint flags) => SetThreadExecutionState(flags);
#else
        public static uint SetState(uint flags) => 0u;
#endif
    }
}
