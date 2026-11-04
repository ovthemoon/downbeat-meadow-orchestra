using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// Hands the steady wind to the shaders (see MeadowBreeze.hlsl): the settings once, and every frame the small wrapped numbers of the clock (<see cref="BreezePhase"/>), which are also what
    /// moves the cloud layers and their shadows. Everything is a global, so the grass, the flowers, the ground and the painted far meadow all feel the same wind.
    /// </summary>
    public sealed class BreezeGpu
    {
        static readonly int DirId = Shader.PropertyToID("_BreezeDir");
        static readonly int AId = Shader.PropertyToID("_BreezeA");
        static readonly int GustAId = Shader.PropertyToID("_BreezeGustA");
        static readonly int GustBId = Shader.PropertyToID("_BreezeGustB");
        static readonly int GustCId = Shader.PropertyToID("_BreezeGustC");
        static readonly int FlutterId = Shader.PropertyToID("_BreezeFlutter");
        static readonly int PhaseAId = Shader.PropertyToID("_BreezePhaseA");
        static readonly int PhaseBId = Shader.PropertyToID("_BreezePhaseB");
        static readonly int ShadowAId = Shader.PropertyToID("_CloudShadowA");
        static readonly int ShadowBId = Shader.PropertyToID("_CloudShadowB");
        static readonly int NearId = Shader.PropertyToID("_CloudNear");
        static readonly int FarId = Shader.PropertyToID("_CloudFar");
        static readonly int ShiftId = Shader.PropertyToID("_CloudShift");
        static readonly int ParamsId = Shader.PropertyToID("_CloudParams");
        static readonly int PlateRippleId = Shader.PropertyToID("_MeadowPlateRipple");

        readonly MeadowCloudsDto _clouds;

        public BreezeGpu(MeadowBreezeDto breeze, MeadowCloudsDto clouds)
        {
            Model = new BreezeModel(breeze, clouds);
            _clouds = clouds;
            var gust = breeze.gust;
            Shader.SetGlobalVector(DirId, new Vector4(Model.Direction.X, Model.Direction.Z, Model.Across.X, Model.Across.Z));
            Shader.SetGlobalVector(AId, new Vector4(breeze.lean, breeze.meanVariation, breeze.stiffnessMin, breeze.stiffnessMax));
            Shader.SetGlobalVector(GustAId, new Vector4(gust.spacing, gust.width, gust.amplitude, gust.weakest));
            Shader.SetGlobalVector(GustBId, new Vector4(gust.jitter, gust.frontWobble, gust.wobbleLength, gust.lagMetres));
            Shader.SetGlobalVector(GustCId, new Vector4(gust.springBack, gust.brightness, breeze.sway.wavelength, breeze.sway.amplitude));
            Shader.SetGlobalVector(FlutterId, new Vector4(breeze.flutter.amplitude, 0f, 0f, 0f));
            Shader.SetGlobalVector(ShadowBId, new Vector4(clouds.shadow.coverage, clouds.shadow.softness, 0f, 0f));
            Shader.SetGlobalVector(NearId, new Vector4(clouds.near.altitude, clouds.near.cellMetres, clouds.near.coverage, clouds.near.softness));
            Shader.SetGlobalVector(FarId, new Vector4(clouds.far.altitude, clouds.far.cellMetres, clouds.far.coverage, clouds.far.softness));
            Shader.SetGlobalVector(ParamsId, new Vector4(clouds.near.opacity, clouds.far.opacity, clouds.fadeFromRow, clouds.fadeToRow));
            Shader.SetGlobalVector(PlateRippleId, new Vector4(breeze.plate.rippleGain, breeze.plate.fullFromRow, breeze.plate.noneAtRow, 0f));
            Upload(0.0);
        }

        public BreezeModel Model { get; }

        /// <summary>Puts the wind of this moment (scene time, seconds) into the shaders.</summary>
        public void Upload(double timeSec)
        {
            var phase = Model.PhaseAt(timeSec);
            Shader.SetGlobalVector(PhaseAId, new Vector4(phase.GustShift, phase.GustCell, phase.SwayPhase, phase.FlutterPhase));
            Shader.SetGlobalVector(PhaseBId, new Vector4(phase.MeanPhase, 0f, 0f, 0f));
            Shader.SetGlobalVector(ShadowAId, new Vector4(phase.ShadowShiftX, phase.ShadowShiftZ, _clouds.shadow.cellMetres, _clouds.shadow.strength));
            Shader.SetGlobalVector(ShiftId, new Vector4(phase.NearShiftX, phase.NearShiftZ, phase.FarShiftX, phase.FarShiftZ));
        }
    }
}
