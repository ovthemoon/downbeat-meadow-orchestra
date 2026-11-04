using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Downbeat.Meadow
{
    /// <summary>The standing viewer: eye height above the ground, looking a little down at the field, with the lens of content <c>meadow.camera</c>.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MeadowCameraRig : MonoBehaviour
    {
        const float NearClip = 0.1f;
        const float FarClip = 150f;

        Camera _camera;

        public Camera Camera => _camera != null ? _camera : (_camera = GetComponent<Camera>());

        /// <summary><paramref name="groundHeightAtViewer"/> is the ground height at x 0, z 0: the eyes are that far above it plus the eye height.</summary>
        public void Configure(MeadowCameraDto settings, float groundHeightAtViewer)
        {
            transform.position = new Vector3(0f, groundHeightAtViewer + settings.height, 0f);
            transform.rotation = Quaternion.Euler(-settings.pitchDeg, 0f, 0f); // content: negative = looking down; Unity: a positive angle about x tips the view down
            var camera = Camera;
            camera.fieldOfView = settings.fovDeg;
            camera.nearClipPlane = NearClip;
            camera.farClipPlane = FarClip;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // the quality tier's MSAA does the edges
        }
    }
}
