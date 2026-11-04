using System;

namespace Downbeat.Core
{
    /// <summary>
    /// The white square of <c>--latency-mode</c>: lit for exactly one frame whenever a played note is heard, so that a phone video that sees the hand, the screen and the speaker shows the moment of the
    /// sound as a picture of the video (docs/latency-method.md). This is the state and the geometry; the app draws it.
    /// </summary>
    public sealed class LatencyFlash
    {
        public const int SquareSize = 120;

        int _litFrame = int.MinValue;

        /// <summary>Notes that lit the square so far.</summary>
        public int Flashes { get; private set; }

        /// <summary>A note is heard in this frame: the square is lit in it (and only in it).</summary>
        public void Arm(int frame)
        {
            _litFrame = frame;
            Flashes++;
        }

        public bool IsLit(int frame) => frame == _litFrame;

        /// <summary>The square in the top right corner of a window, in pixels from its top left corner; smaller than 120 only when the window is.</summary>
        public static (int x, int y, int size) Square(int windowWidth, int windowHeight)
        {
            var size = Math.Max(1, Math.Min(SquareSize, Math.Min(windowWidth, windowHeight)));
            return (windowWidth - size, 0, size);
        }
    }
}
