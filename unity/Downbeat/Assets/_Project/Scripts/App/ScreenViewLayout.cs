using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>The meadow fills the whole window: the mouse maps over the full screen (left, middle, right third = the three instruments, height = pitch).</summary>
    public sealed class ScreenViewLayout : IViewLayout
    {
        public RectF StagePixels => new RectF(0f, 0f, Screen.width, Screen.height);
    }
}
