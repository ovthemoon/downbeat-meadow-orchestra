using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--latency-mode</c>: a white 120 x 120 pixel square in the top right corner of the meadow window, lit for one frame whenever a note that a hand played is heard (the picture event that the app
    /// already times for the sound: <c>NoteVisualQueue</c>). The sound is not changed. A phone video in slow motion that sees the hand, the screen and the speaker then shows how long it took from
    /// the strike to the sound and to the picture (docs/latency-method.md, tools/latency_from_marks.py).
    /// </summary>
    public sealed class LatencyMarker : MonoBehaviour
    {
        readonly LatencyFlash _flash = new LatencyFlash();

        /// <summary>Starts the marker when <c>--latency-mode</c> was given; null otherwise.</summary>
        public static LatencyMarker Begin(LaunchArgs args)
        {
            if (!args.Has("latency-mode")) return null;
            var marker = new GameObject("LatencyMarker").AddComponent<LatencyMarker>();
            DontDestroyOnLoad(marker.gameObject);
            Debug.Log("[latency] latency mode: a white square flashes at the top right of the window for every played note");
            Application.quitting += () => Debug.Log($"[latency] the square flashed for {marker._flash.Flashes} played notes");
            return marker;
        }

        public int Flashes => _flash.Flashes;

        /// <summary>A played note is heard now: the square is lit in this frame.</summary>
        public void NoteHeard() => _flash.Arm(Time.frameCount);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !_flash.IsLit(Time.frameCount)) return;
            var square = LatencyFlash.Square(Screen.width, Screen.height);
            GUI.DrawTexture(new Rect(square.x, square.y, square.size, square.size), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }
    }
}
