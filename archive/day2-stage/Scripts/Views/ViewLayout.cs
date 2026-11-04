using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>
    /// Splits the one canvas into the stage view (left) and the audience view (right) with Camera.rect.
    /// Default split gives the stage a 16:9 area on an 8:3 canvas (stage 2/3, audience 1/3).
    /// </summary>
    public sealed class ViewLayout : IViewLayout
    {
        public const float DefaultStageFraction = 2f / 3f;
        readonly ViewMode _mode;
        readonly float _stageFraction;

        public ViewLayout(ViewMode mode, float stageFraction = DefaultStageFraction)
        {
            _mode = mode;
            _stageFraction = stageFraction;
        }

        public ViewMode Mode => _mode;
        public bool ShowStage => _mode != ViewMode.AudienceOnly;
        public bool ShowAudience => _mode != ViewMode.StageOnly;

        /// <summary>Normalized viewport rectangle for Camera.rect.</summary>
        public Rect StageViewport => _mode == ViewMode.Both ? new Rect(0f, 0f, _stageFraction, 1f) : new Rect(0f, 0f, 1f, 1f);
        public Rect AudienceViewport => _mode == ViewMode.Both ? new Rect(_stageFraction, 0f, 1f - _stageFraction, 1f) : new Rect(0f, 0f, 1f, 1f);

        public RectF StagePixels
        {
            get
            {
                var r = StageViewport;
                return new RectF(r.x * Screen.width, r.y * Screen.height, r.width * Screen.width, r.height * Screen.height);
            }
        }
    }
}
