using Downbeat.Content;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The far picture and the light that goes with it. Three plates (blue hour, playing dusk, sunset) are cross-faded by <see cref="SetSunset"/> along the
    /// sky arc; the sun colour and strength, the sky light and the fog colour of the 3D ground follow the same three stops, so the grass in front never
    /// disagrees with the sky behind it. Everything is handed to the shaders as globals (MeadowCommon.hlsl).
    /// </summary>
    public sealed class BackdropView : MonoBehaviour
    {
        const int StopCount = MeadowContentValidator.StopCount;
        const float Degrees = Mathf.Deg2Rad;

        [SerializeField] MeshRenderer quad;
        [SerializeField] Light sun;

        Mesh _mesh;
        Texture2D _cloudTile;
        MeadowCloudsDto _clouds;
        Color[] _lightColor;
        float[] _lightIntensity;
        float[] _sunElevation;
        Color[] _fogLinear;
        Color[] _ambientLinear;
        float[] _ambientIntensity;
        Color[] _cloudLitLinear;
        Color[] _cloudShadeLinear;
        float _groundY;
        float _plateAspect = 16f / 9f;
        float _sunAzimuth;
        float _sunsetT;
        bool _configured;

        /// <summary>Where the sky is on its arc, 0..1 (see <see cref="SkyArc"/>).</summary>
        public float SunsetT => _sunsetT;

        /// <param name="groundY">The height of the ground plane that the far plate is mapped onto (for the gust ripple and the cloud shadows on it).</param>
        public void Configure(MeadowSkyDto sky, MeadowHazeDto haze, ContentBundle bundle, float groundY)
        {
            _groundY = groundY;
            _clouds = sky.clouds;
            _cloudLitLinear = new Color[StopCount];
            _cloudShadeLinear = new Color[StopCount];
            _lightColor = new Color[StopCount];
            _lightIntensity = new float[StopCount];
            _sunElevation = new float[StopCount];
            _fogLinear = new Color[StopCount];
            _ambientLinear = new Color[StopCount];
            _ambientIntensity = new float[StopCount];
            var plateIds = new[] { MeadowShaderIds.Plate0, MeadowShaderIds.Plate1, MeadowShaderIds.Plate2 };
            for (var i = 0; i < StopCount; i++)
            {
                var stop = sky.stops[i];
                var plate = bundle.Texture(stop.backdrop);
                plate.wrapMode = TextureWrapMode.Clamp;
                plate.filterMode = FilterMode.Bilinear;
                Shader.SetGlobalTexture(plateIds[i], plate);
                if (i == 0) _plateAspect = (float)plate.width / plate.height;
                if (plate.mipmapCount <= 1) Debug.LogWarning($"[BackdropView] plate {stop.backdrop} has no mip chain: the haze cannot be blurred");
                _lightColor[i] = MeadowColors.Srgb(stop.lightColor);
                _lightIntensity[i] = stop.lightIntensity;
                _sunElevation[i] = stop.sunElevationDeg;
                _fogLinear[i] = MeadowColors.Linear(stop.fogColor);
                _ambientLinear[i] = MeadowColors.Linear(stop.ambientColor);
                _ambientIntensity[i] = stop.ambientIntensity;
                _cloudLitLinear[i] = MeadowColors.Linear(stop.cloudLit);
                _cloudShadeLinear[i] = MeadowColors.Linear(stop.cloudShade);
            }
            _sunAzimuth = sky.sunAzimuthDeg;
            Shader.SetGlobalVector(MeadowShaderIds.Haze, new Vector4(haze.fromRow, haze.toRow, haze.plateFrom, haze.curve));
            Shader.SetGlobalVector(MeadowShaderIds.Haze2, new Vector4(haze.blurLod, haze.sharpFrom, 0f, 0f));

            EnsureCloudTile();
            if (_mesh != null) DestroyMesh(); // built again (F5 reloads the content): the quad of the last build must not stay in memory
            _mesh = CreateQuadMesh();
            quad.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _configured = true;
            SetHillPulse(0f, 0f, 1f);
            SetSunset(_sunsetT);
        }

        /// <summary>Moves the sky along its arc: 0 the quiet blue hour, 0.5 the playing dusk, 1 the climax sunset.</summary>
        public void SetSunset(float t01)
        {
            _sunsetT = Mathf.Clamp01(float.IsNaN(t01) ? 0f : t01);
            if (!_configured) return;
            var (w0, w1, w2) = SkyArc.Weights(_sunsetT);
            Shader.SetGlobalVector(MeadowShaderIds.SkyWeights, new Vector4(w0, w1, w2, 0f));

            var lightColor = Mix(_lightColor, w0, w1, w2);
            var intensity = SkyArc.Mix(_sunsetT, _lightIntensity[0], _lightIntensity[1], _lightIntensity[2]);
            var elevation = SkyArc.Mix(_sunsetT, _sunElevation[0], _sunElevation[1], _sunElevation[2]);
            sun.color = lightColor;
            sun.intensity = intensity;
            // the sun is behind the far hills, a little to the right as in the plates: the light travels towards the viewer (-z), a little left, and, above the horizon, downwards
            var radians = elevation * Degrees;
            var azimuth = _sunAzimuth * Degrees;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(azimuth) * Mathf.Cos(radians), -Mathf.Sin(radians), -Mathf.Cos(azimuth) * Mathf.Cos(radians)));

            Shader.SetGlobalVector(MeadowShaderIds.FogColor, MeadowColors.AsVector(Mix(_fogLinear, w0, w1, w2)));
            Shader.SetGlobalVector(MeadowShaderIds.AmbientColor, MeadowColors.AsVector(Mix(_ambientLinear, w0, w1, w2) * SkyArc.Mix(_sunsetT, _ambientIntensity[0], _ambientIntensity[1], _ambientIntensity[2])));
            Shader.SetGlobalVector(MeadowShaderIds.SunColor, MeadowColors.AsVector(lightColor.linear * intensity));
            Shader.SetGlobalVector(MeadowShaderIds.CloudLitTint, Tint(Mix(_cloudLitLinear, w0, w1, w2), _clouds.litBrightness));
            Shader.SetGlobalVector(MeadowShaderIds.CloudShadeTint, Tint(Mix(_cloudShadeLinear, w0, w1, w2), _clouds.shadeBrightness));
        }

        /// <summary>
        /// Lights up the haze over the far hills (the drum of the nature layers): <paramref name="strength"/> 0 is off; the glow is strongest at the picture row
        /// <paramref name="centreRow"/> (0 top, 1 bottom) and fades to nothing <paramref name="halfHeight"/> rows above and below it. <paramref name="follow"/> 0 is a flat band, 1 follows the haze that is painted in the plate (BackdropBlend.shader).
        /// </summary>
        public void SetHillPulse(float strength, float centreRow, float halfHeight, float follow = 0f)
        {
            Shader.SetGlobalVector(MeadowShaderIds.HillPulse, new Vector4(Mathf.Max(0f, strength), centreRow, halfHeight, Mathf.Clamp01(follow)));
        }

        /// <summary>
        /// The picture of the cloud noise that the weak quality tier reads instead of working the noise out for every pixel (CloudTile, BackdropBlend.shader). It does not depend on the content (the noise is fixed),
        /// so it is made once and kept when the content is reloaded; it is made at the start whatever the tier is, because the automatic quality may go down to the weak tier at any time and must not stop to make it.
        /// </summary>
        void EnsureCloudTile()
        {
            if (_cloudTile == null)
            {
                _cloudTile = new Texture2D(CloudTile.Size, CloudTile.Size, TextureFormat.RG16, false, true)
                {
                    name = "MeadowCloudTile",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                };
                _cloudTile.SetPixelData(CloudTile.Bake(), 0);
                _cloudTile.Apply(false, true);
            }
            Shader.SetGlobalTexture(MeadowShaderIds.CloudTile, _cloudTile);
        }

        void OnDestroy()
        {
            if (_mesh != null) DestroyMesh();
            if (_cloudTile == null) return;
            if (Application.isPlaying) Destroy(_cloudTile);
            else DestroyImmediate(_cloudTile);
        }

        void DestroyMesh()
        {
            quad.GetComponent<MeshFilter>().sharedMesh = null;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        /// <summary>The plate fills whatever size the camera renders (a window, a capture): the longer side of the screen is cropped, never stretched.</summary>
        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_configured || camera.pixelHeight == 0) return;
            var screenAspect = (float)camera.pixelWidth / camera.pixelHeight;
            var cover = screenAspect > _plateAspect ? new Vector2(1f, _plateAspect / screenAspect) : new Vector2(screenAspect / _plateAspect, 1f);
            Shader.SetGlobalVector(MeadowShaderIds.PlateCover, new Vector4(cover.x, cover.y, 0f, 0f));

            // where this camera looks, so that a picture row of the far plate can be mapped to a stretch of ground (and a sky direction): the drifting clouds, the gusts and the shadows
            var projection = camera.projectionMatrix;
            var eye = camera.transform;
            var position = eye.position;
            Shader.SetGlobalVector(MeadowShaderIds.CamPos, new Vector4(position.x, position.y, position.z, _groundY));
            Shader.SetGlobalVector(MeadowShaderIds.CamRight, eye.right / projection.m00);
            Shader.SetGlobalVector(MeadowShaderIds.CamUp, eye.up / projection.m11);
            Shader.SetGlobalVector(MeadowShaderIds.CamForward, eye.forward);
        }

        static Color Mix(Color[] stops, float w0, float w1, float w2) => stops[0] * w0 + stops[1] * w1 + stops[2] * w2;

        /// <summary>What the sky behind a cloud is multiplied by on the side of the cloud that has this colour and brightness (see <see cref="CloudTint"/>).</summary>
        Vector4 Tint(Color side, float brightness)
        {
            var (r, g, b) = CloudTint.Of(side.r, side.g, side.b, _clouds.hueShare, brightness);
            return new Vector4(r, g, b, 0f);
        }

        static Mesh CreateQuadMesh()
        {
            // two triangles covering clip space; the shader places them itself, so the mesh must never be culled
            var mesh = new Mesh { name = "BackdropQuad" };
            mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }
    }
}
