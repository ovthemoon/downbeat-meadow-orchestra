using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>How wide the view gets with depth, for the things that must cover the screen edge to edge (ground mesh, grass bands, height map).</summary>
    static class MeadowFrustum
    {
        /// <summary>The widest window the ground is built for: 21:9 and a little more. A wider window would show bare edges.</summary>
        public const float WidestAspect = 2.4f;

        /// <summary>Half the width of the widest view per metre of depth.</summary>
        public static float HalfWidthPerDepth(float verticalFovDeg) => Mathf.Tan(verticalFovDeg * 0.5f * Mathf.Deg2Rad) * WidestAspect;
    }
}
