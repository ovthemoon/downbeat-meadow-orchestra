using System;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// Gives plain C# classes a place to run coroutines (WebGL has no threads; loading is coroutine based), and the two Unity messages that a plain class cannot receive and that tell it the app is
    /// going away: <see cref="ApplicationQuitting"/> (the window was closed or <c>Application.Quit</c> was called) and <see cref="Destroyed"/> (this object is torn down).
    /// </summary>
    public sealed class CoroutineRunner : MonoBehaviour
    {
        /// <summary>Raised from <c>OnApplicationQuit</c>, before anything is destroyed: the last moment at which a recorder can still write what it has.</summary>
        public event Action ApplicationQuitting;

        /// <summary>Raised from <c>OnDestroy</c>: a scene teardown, or, on quit, the second of the two messages (the first is <see cref="ApplicationQuitting"/>).</summary>
        public event Action Destroyed;

        void OnApplicationQuit() => ApplicationQuitting?.Invoke();

        void OnDestroy() => Destroyed?.Invoke();
    }
}
