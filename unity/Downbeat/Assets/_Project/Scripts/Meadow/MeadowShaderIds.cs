using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>Names of the shader globals that every meadow shader reads (see MeadowCommon.hlsl).</summary>
    static class MeadowShaderIds
    {
        public static readonly int Plate0 = Shader.PropertyToID("_MeadowPlate0");
        public static readonly int Plate1 = Shader.PropertyToID("_MeadowPlate1");
        public static readonly int Plate2 = Shader.PropertyToID("_MeadowPlate2");
        public static readonly int SkyWeights = Shader.PropertyToID("_MeadowSkyWeights");
        public static readonly int PlateCover = Shader.PropertyToID("_MeadowPlateCover");
        public static readonly int Haze = Shader.PropertyToID("_MeadowHaze");
        public static readonly int Haze2 = Shader.PropertyToID("_MeadowHaze2");
        public static readonly int FogColor = Shader.PropertyToID("_MeadowFogColor");
        public static readonly int HillPulse = Shader.PropertyToID("_MeadowHillPulse");
        public static readonly int AmbientColor = Shader.PropertyToID("_MeadowAmbientColor");
        public static readonly int SunColor = Shader.PropertyToID("_MeadowSunColor");
        public static readonly int CamPos = Shader.PropertyToID("_MeadowCamPos");
        public static readonly int CamRight = Shader.PropertyToID("_MeadowCamRight");
        public static readonly int CamUp = Shader.PropertyToID("_MeadowCamUp");
        public static readonly int CamForward = Shader.PropertyToID("_MeadowCamForward");
        public static readonly int CloudLitTint = Shader.PropertyToID("_CloudLitTint");
        public static readonly int CloudShadeTint = Shader.PropertyToID("_CloudShadeTint");
        public static readonly int CloudTile = Shader.PropertyToID("_MeadowCloudTile");
    }
}
