using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// A place in front of the meadow camera to draw things on the picture itself (the effects of the upper row, the trail of notes, the names of the cells, the follow-along guide): an object that is a
    /// child of the camera, so everything on it stays where it is put whatever the window's size or the render scale. A point of the picture is given as a viewport point (<see cref="LocalPoint"/>): 0..1
    /// across, 0..1 up from the bottom, on the plane one metre in front of the camera. Whatever is drawn here sorts from <see cref="SortingOrderBase"/> up, over the record strip that hangs in front of the same
    /// camera. The meadow stage makes one at run time (<see cref="MeadowStage.Overlay"/>); nothing of it is in the scene.
    /// </summary>
    public sealed class MeadowOverlayLayer : MonoBehaviour
    {
        /// <summary>How far in front of the camera the plane of the picture is (a child put at <see cref="LocalPoint"/> sits on it).</summary>
        public const float DistanceFromCamera = 1f;
        public const int DefaultSortingOrderBase = 100;
        public const float DefaultRowSplit = 0.5f;

        Camera _viewer;

        /// <summary>The object whose children are the things drawn over the picture: at the camera's own place and turn, so their local place is the camera's.</summary>
        public Transform Root => transform;
        /// <summary>The camera whose picture this is.</summary>
        public Camera Viewer => _viewer;
        /// <summary>Renderers on the layer use this sorting order or one above it (the record strip's text is far below it).</summary>
        public int SortingOrderBase { get; } = DefaultSortingOrderBase;

        /// <summary>The share of the picture (from the bottom) that the record strip covers: <c>display.overlayFraction</c> while the strip is shown on the meadow's own screen, else 0.</summary>
        public float StripFraction { get; set; }

        /// <summary>The hand height (0 top .. 1 bottom) where the two rows of the hand meet (<c>cells.rowSplit</c>).</summary>
        public float RowSplit { get; set; } = DefaultRowSplit;

        /// <summary>
        /// The viewport y of the horizon, where the top row of the hand (the sky) meets the bottom row (the ground): the far edge of the playing field as the meadow camera sees it, set by the meadow stage when it is built.
        /// NaN (before that, and in a test that has no meadow) is half way between the strip and the top, which draws the hand as one straight line.
        /// </summary>
        public float HorizonViewport { get; set; } = float.NaN;

        /// <summary>The height of the picture, in the units of <see cref="LocalPoint"/> (the camera's own metres at distance <see cref="DistanceFromCamera"/>).</summary>
        public float PictureHeight => 2f * Mathf.Tan(_viewer.fieldOfView * 0.5f * Mathf.Deg2Rad) * DistanceFromCamera;
        /// <summary>The shape of the picture the camera is drawing now (a window, a resized window, a capture).</summary>
        public float Aspect => _viewer.pixelHeight > 0 ? (float)_viewer.pixelWidth / _viewer.pixelHeight : _viewer.aspect;

        /// <summary>Makes the layer as a child of <paramref name="viewer"/>.</summary>
        public static MeadowOverlayLayer Create(Camera viewer)
        {
            var layer = new GameObject("MeadowOverlay").AddComponent<MeadowOverlayLayer>();
            layer._viewer = viewer;
            layer.transform.SetParent(viewer.transform, false);
            return layer;
        }

        /// <summary>The place, in the local space of <see cref="Root"/>, of the point (<paramref name="vx"/>, <paramref name="vy"/>) of the picture: both 0..1, y counted up from the bottom like a viewport.</summary>
        public Vector3 LocalPoint(float vx, float vy)
        {
            var height = PictureHeight;
            return new Vector3((vx - 0.5f) * height * Aspect, (vy - 0.5f) * height, DistanceFromCamera);
        }

        /// <summary>The viewport point where a hand at (<paramref name="x01"/>, <paramref name="y01"/>) of the tracker's picture is drawn (<see cref="HandScreenMap"/>): the top row over the sky down to the horizon, the bottom row from the horizon down to the record strip.</summary>
        public (float vx, float vy) ViewportOfHand(float x01, float y01) => HandScreenMap.Viewport(x01, y01, StripFraction, RowSplit, HorizonViewport);

        /// <summary>The place of a hand at (<paramref name="x01"/>, <paramref name="y01"/>) of the tracker's picture, in the local space of <see cref="Root"/>.</summary>
        public Vector3 LocalPointOfHand(float x01, float y01)
        {
            var (vx, vy) = ViewportOfHand(x01, y01);
            return LocalPoint(vx, vy);
        }
    }
}
