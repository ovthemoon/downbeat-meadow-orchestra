using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>meadow</c> block of content.json (called by <see cref="ContentValidator"/>).</summary>
    public static class MeadowContentValidator
    {
        public const int StopCount = 3;
        public const int MaxRings = 4;
        public static readonly string[] StopIds = { "blueHour", "mid", "sunset" };

        public static void Check(MeadowDto meadow, List<string> errors)
        {
            if (meadow == null) { errors.Add("meadow is missing"); return; }
            CheckLayout(meadow.layout, errors);
            CheckCamera(meadow.camera, errors);
            CheckTerrain(meadow.terrain, errors);
            CheckSky(meadow.sky, errors);
            CheckHaze(meadow.haze, errors);
            CheckWind(meadow.wind, errors);
            CheckBreeze(meadow.breeze, errors);
            CheckGrass(meadow.grass, errors);
            CheckQuality(meadow.quality, errors);
            CheckFlowers(meadow.flowers, errors);
            CheckParticles(meadow.particles, errors);
            SkyContentValidator.Check(meadow.skyEffects, errors);
            CheckRibbon(meadow.ribbon, errors);
            CheckDemo(meadow.demo, errors);
            CheckWake(meadow.wake, errors);
        }

        static void CheckWake(MeadowWakeDto w, List<string> errors)
        {
            if (w == null) { errors.Add("meadow.wake is missing"); return; }
            if (!(w.radius > 0f && w.radius <= 20f)) errors.Add("meadow.wake.radius must be > 0 and <= 20");
            if (!(w.speed > 0.5f && w.speed <= 20f)) errors.Add("meadow.wake.speed must be 0.5..20");
            if (w.radius > 0f && w.speed > 0.5f && w.radius / w.speed > 3f) errors.Add("meadow.wake must be over within 3 s (radius / speed)");
            if (w.maxFlowers < 0 || w.maxFlowers > 200) errors.Add("meadow.wake.maxFlowers must be 0..200");
            if (!(w.jitterSec >= 0f && w.jitterSec <= 1f)) errors.Add("meadow.wake.jitterSec must be 0..1");
            if (!(w.gustAmplitude >= 0f && w.gustAmplitude <= 2f)) errors.Add("meadow.wake.gustAmplitude must be 0..2");
            if (!(w.gustLifeSec > 0f && w.gustLifeSec <= 10f)) errors.Add("meadow.wake.gustLifeSec must be > 0 and <= 10");
            if (!(w.gustGlow >= 0f && w.gustGlow <= 1f)) errors.Add("meadow.wake.gustGlow must be 0..1");
        }

        static void CheckDemo(MeadowDemoDto d, List<string> errors)
        {
            if (d == null) { errors.Add("meadow.demo is missing"); return; }
            if (!(d.startSec >= 0f && d.startSec <= 60f)) errors.Add("meadow.demo.startSec must be 0..60");
            if (!(d.radialDepth >= 0f && d.radialDepth <= 100f)) errors.Add("meadow.demo.radialDepth must be 0..100");
            if (!(d.radialVelocity >= 0f && d.radialVelocity <= 1f)) errors.Add("meadow.demo.radialVelocity must be 0..1");
        }

        static void CheckLayout(MeadowLayoutDto layout, List<string> errors)
        {
            if (layout == null) { errors.Add("meadow.layout is missing"); return; }
            if (!(layout.fieldWidth > 0f && layout.fieldWidth <= 400f)) errors.Add("meadow.layout.fieldWidth must be > 0 and <= 400");
            if (!(layout.nearZ > 0f && layout.nearZ <= 20f)) errors.Add("meadow.layout.nearZ must be > 0 and <= 20");
            if (!(layout.farZ > layout.nearZ && layout.farZ <= 200f)) errors.Add("meadow.layout.farZ must be above nearZ and <= 200");
            if (!(layout.horizonScreenY > 0.02f && layout.horizonScreenY < 0.9f)) errors.Add("meadow.layout.horizonScreenY must be 0.02..0.9");
        }

        static void CheckCamera(MeadowCameraDto camera, List<string> errors)
        {
            if (camera == null) { errors.Add("meadow.camera is missing"); return; }
            if (!(camera.height > 0.2f && camera.height <= 10f)) errors.Add("meadow.camera.height must be 0.2..10");
            if (!(camera.pitchDeg >= -60f && camera.pitchDeg <= 10f)) errors.Add("meadow.camera.pitchDeg must be -60..10");
            if (!(camera.fovDeg >= 20f && camera.fovDeg <= 100f)) errors.Add("meadow.camera.fovDeg must be 20..100");
        }

        static void CheckTerrain(MeadowTerrainDto terrain, List<string> errors)
        {
            if (terrain == null) { errors.Add("meadow.terrain is missing"); return; }
            if (!(terrain.maxHeight >= 0f && terrain.maxHeight <= 3f)) errors.Add("meadow.terrain.maxHeight must be 0..3 (a gentle slope needs a low ground)");
            if (!(terrain.cellSize >= 0.1f && terrain.cellSize <= 4f)) errors.Add("meadow.terrain.cellSize must be 0.1..4");
            if (!(terrain.zMin < 0f && terrain.zMin >= -20f)) errors.Add("meadow.terrain.zMin must be -20..0 (the ground starts behind the camera)");
            if (!(terrain.zMax > 1f && terrain.zMax <= 300f)) errors.Add("meadow.terrain.zMax must be > 1 and <= 300");
            if (!(terrain.xMargin >= 0f && terrain.xMargin <= 50f)) errors.Add("meadow.terrain.xMargin must be 0..50");
            if (!(terrain.patchScale >= 0.5f && terrain.patchScale <= 100f)) errors.Add("meadow.terrain.patchScale must be 0.5..100");
            if (!(terrain.reliefHeight >= 0f && terrain.reliefHeight <= 0.4f)) errors.Add("meadow.terrain.reliefHeight must be 0..0.4 (gentle: the grass is half a metre)");
            if (!(terrain.reliefWavelength >= 1f && terrain.reliefWavelength <= 20f)) errors.Add("meadow.terrain.reliefWavelength must be 1..20");
            if (!(terrain.reliefFromZ >= 0f && terrain.reliefToZ > terrain.reliefFromZ && terrain.reliefToZ <= 12f)) errors.Add("meadow.terrain.reliefFromZ/ToZ must be 0 <= from < to <= 12 (it has to be gone before the hand-over to the plate)");
            CheckColor(terrain.groundDark, "meadow.terrain.groundDark", errors);
            CheckColor(terrain.groundMid, "meadow.terrain.groundMid", errors);
            CheckColor(terrain.groundLight, "meadow.terrain.groundLight", errors);
        }

        static void CheckSky(MeadowSkyDto sky, List<string> errors)
        {
            if (sky == null || sky.stops == null) { errors.Add("meadow.sky.stops is missing"); return; }
            if (!(sky.sunAzimuthDeg >= -45f && sky.sunAzimuthDeg <= 45f)) errors.Add("meadow.sky.sunAzimuthDeg must be -45..45");
            CheckClouds(sky.clouds, errors);
            if (sky.stops.Length != StopCount) { errors.Add($"meadow.sky.stops must list exactly {StopCount} stops ({string.Join(", ", StopIds)})"); return; }
            for (var i = 0; i < StopCount; i++)
            {
                var stop = sky.stops[i];
                var name = $"meadow.sky.stops[{i}]";
                if (stop == null) { errors.Add(name + " is empty"); continue; }
                if (stop.id != StopIds[i]) errors.Add($"{name}.id must be '{StopIds[i]}' (stops are ordered quiet -> playing -> climax)");
                if (string.IsNullOrWhiteSpace(stop.backdrop)) errors.Add(name + ".backdrop is empty");
                if (!(stop.lightIntensity >= 0f && stop.lightIntensity <= 10f)) errors.Add(name + ".lightIntensity must be 0..10");
                if (!(stop.sunElevationDeg >= -20f && stop.sunElevationDeg <= 60f)) errors.Add(name + ".sunElevationDeg must be -20..60");
                CheckColor(stop.lightColor, name + ".lightColor", errors);
                CheckColor(stop.fogColor, name + ".fogColor", errors);
                CheckColor(stop.ambientColor, name + ".ambientColor", errors);
                CheckColor(stop.cloudLit, name + ".cloudLit", errors);
                CheckColor(stop.cloudShade, name + ".cloudShade", errors);
                if (!(stop.ambientIntensity >= 0f && stop.ambientIntensity <= 20f)) errors.Add(name + ".ambientIntensity must be 0..20");
            }
        }

        static void CheckHaze(MeadowHazeDto haze, List<string> errors)
        {
            if (haze == null) { errors.Add("meadow.haze is missing"); return; }
            if (!(haze.toRow >= 0.1f && haze.fromRow > haze.toRow && haze.fromRow <= 0.95f)) errors.Add("meadow.haze.fromRow/toRow must be 0.1 <= toRow < fromRow <= 0.95 (rows from the top: the plate is above toRow)");
            if (!(haze.plateFrom > 0f && haze.plateFrom <= 1f)) errors.Add("meadow.haze.plateFrom must be above 0 and at most 1");
            if (!(haze.sharpFrom >= 0f && haze.sharpFrom < 1f)) errors.Add("meadow.haze.sharpFrom must be 0..1 (exclusive)");
            if (!(haze.blurLod >= 0f && haze.blurLod <= 8f)) errors.Add("meadow.haze.blurLod must be 0..8");
            if (!(haze.curve >= 0.2f && haze.curve <= 8f)) errors.Add("meadow.haze.curve must be 0.2..8");
        }

        static void CheckWind(MeadowWindDto wind, List<string> errors)
        {
            if (wind == null) { errors.Add("meadow.wind is missing"); return; }
            if (!(wind.waveWidth >= 0.2f && wind.waveWidth <= 20f)) errors.Add("meadow.wind.waveWidth must be 0.2..20");
            if (!(wind.handRadius >= 0.2f && wind.handRadius <= 20f)) errors.Add("meadow.wind.handRadius must be 0.2..20");
            if (!(wind.bendScale >= 0f && wind.bendScale <= 10f)) errors.Add("meadow.wind.bendScale must be 0..10");
            if (!(wind.maxBendDeg >= 10f && wind.maxBendDeg <= 150f)) errors.Add("meadow.wind.maxBendDeg must be 10..150 (under 180, so the tip of a bent blade stays above its foot)");
            if (!(wind.handPush >= 0f && wind.handPush <= 5f)) errors.Add("meadow.wind.handPush must be 0..5");
            if (!(wind.handLean >= 0f && wind.handLean <= 5f)) errors.Add("meadow.wind.handLean must be 0..5");
            if (!(wind.handLookAheadSec >= 0.005f && wind.handLookAheadSec <= 0.5f)) errors.Add("meadow.wind.handLookAheadSec must be 0.005..0.5");
            if (!(wind.handGustWidth >= 0.2f && wind.handGustWidth <= 10f)) errors.Add("meadow.wind.handGustWidth must be 0.2..10");
            if (wind.handGustWidth >= wind.handRadius) errors.Add("meadow.wind.handGustWidth must be smaller than handRadius (the gust has to fit in the hand's wind)");
            if (!(wind.handSpeedCap >= 0.2f && wind.handSpeedCap <= 10f)) errors.Add("meadow.wind.handSpeedCap must be 0.2..10 m/s");
            if (wind.beatSweep == null) errors.Add("meadow.wind.beatSweep is missing");
            else
            {
                CheckWave("meadow.wind.beatSweep", wind.beatSweep.amplitude, wind.beatSweep.speed, wind.beatSweep.lifeSec, errors);
                if (!(wind.beatSweep.energyGain >= 0f && wind.beatSweep.energyGain <= 10f)) errors.Add("meadow.wind.beatSweep.energyGain must be 0..10");
                if (!(wind.beatSweep.downbeatGain >= 1f && wind.beatSweep.downbeatGain <= 10f)) errors.Add("meadow.wind.beatSweep.downbeatGain must be 1..10");
            }
            if (wind.noteRadial == null) errors.Add("meadow.wind.noteRadial is missing");
            else
            {
                CheckWave("meadow.wind.noteRadial", wind.noteRadial.amplitude, wind.noteRadial.speed, wind.noteRadial.lifeSec, errors);
                if (!(wind.noteRadial.velocityGain >= 0f && wind.noteRadial.velocityGain <= 10f)) errors.Add("meadow.wind.noteRadial.velocityGain must be 0..10");
            }
            // the two reactions of the bottom row are optional: the defaults apply to a file from before them
            var longWave = wind.longWave;
            if (longWave != null) CheckReaction("meadow.wind.longWave", longWave.amplitude, longWave.velocityGain, longWave.speed, longWave.lifeSec, longWave.widthScale, errors);
            var drumRing = wind.drumRing;
            if (drumRing != null) CheckReaction("meadow.wind.drumRing", drumRing.amplitude, drumRing.velocityGain, drumRing.speed, drumRing.lifeSec, drumRing.widthScale, errors);
        }

        static void CheckReaction(string name, float amplitude, float velocityGain, float speed, float lifeSec, float widthScale, List<string> errors)
        {
            CheckWave(name, amplitude, speed, lifeSec, errors);
            if (!(velocityGain >= 0f && velocityGain <= 10f)) errors.Add(name + ".velocityGain must be 0..10");
            if (!(widthScale >= 0.5f && widthScale <= 6f)) errors.Add(name + ".widthScale must be 0.5..6");
        }

        static void CheckBreeze(MeadowBreezeDto breeze, List<string> errors)
        {
            if (breeze == null) { errors.Add("meadow.breeze is missing"); return; }
            if (!(breeze.directionDeg >= 0f && breeze.directionDeg < 360f)) errors.Add("meadow.breeze.directionDeg must be 0..360 (exclusive)");
            if (!(breeze.lean >= 0f && breeze.lean <= 1f)) errors.Add("meadow.breeze.lean must be 0..1");
            if (!(breeze.meanPeriodSec >= 2f && breeze.meanPeriodSec <= 600f)) errors.Add("meadow.breeze.meanPeriodSec must be 2..600");
            if (!(breeze.meanVariation >= 0f && breeze.meanVariation <= 0.9f)) errors.Add("meadow.breeze.meanVariation must be 0..0.9");
            if (!(breeze.stiffnessMin > 0f && breeze.stiffnessMax >= breeze.stiffnessMin && breeze.stiffnessMax <= 3f)) errors.Add("meadow.breeze.stiffnessMin/stiffnessMax must be 0 < min <= max <= 3");
            CheckGust(breeze.gust, errors);
            if (breeze.sway == null) errors.Add("meadow.breeze.sway is missing");
            else
            {
                if (!(breeze.sway.wavelength >= 0.5f && breeze.sway.wavelength <= 100f)) errors.Add("meadow.breeze.sway.wavelength must be 0.5..100");
                if (!(breeze.sway.periodSec >= 0.5f && breeze.sway.periodSec <= 120f)) errors.Add("meadow.breeze.sway.periodSec must be 0.5..120");
                if (!(breeze.sway.amplitude >= 0f && breeze.sway.amplitude <= 1f)) errors.Add("meadow.breeze.sway.amplitude must be 0..1");
            }
            if (breeze.flutter == null) errors.Add("meadow.breeze.flutter is missing");
            else
            {
                if (!(breeze.flutter.amplitude >= 0f && breeze.flutter.amplitude <= 0.3f)) errors.Add("meadow.breeze.flutter.amplitude must be 0..0.3");
                if (!(breeze.flutter.periodSec >= 0.5f && breeze.flutter.periodSec <= 60f)) errors.Add("meadow.breeze.flutter.periodSec must be 0.5..60");
            }
            if (breeze.plate == null) errors.Add("meadow.breeze.plate is missing");
            else
            {
                if (!(breeze.plate.rippleGain >= 0f && breeze.plate.rippleGain <= 1f)) errors.Add("meadow.breeze.plate.rippleGain must be 0..1");
                if (!(breeze.plate.noneAtRow >= 0.1f && breeze.plate.fullFromRow > breeze.plate.noneAtRow && breeze.plate.fullFromRow <= 0.95f)) errors.Add("meadow.breeze.plate.fullFromRow/noneAtRow must be 0.1 <= noneAtRow < fullFromRow <= 0.95");
            }
        }

        /// <summary>How many gust widths the profile of a gust reaches (front, back and its dip behind), for the window of <see cref="BreezeModel"/>.</summary>
        public const float GustWidthFactor = 4.5f;
        /// <summary>How many spacings the window of gusts leaves for that reach, the lag of the tip and the wobble of the front.</summary>
        public const float GustReachShare = 2f;

        static void CheckGust(BreezeGustDto gust, List<string> errors)
        {
            if (gust == null) { errors.Add("meadow.breeze.gust is missing"); return; }
            if (!(gust.spacing >= 2f && gust.spacing <= 100f)) errors.Add("meadow.breeze.gust.spacing must be 2..100");
            if (!(gust.speed >= 0.1f && gust.speed <= 30f)) errors.Add("meadow.breeze.gust.speed must be 0.1..30");
            if (!(gust.width >= 0.3f && gust.width <= 30f)) errors.Add("meadow.breeze.gust.width must be 0.3..30");
            if (!(gust.amplitude >= 0f && gust.amplitude <= 2f)) errors.Add("meadow.breeze.gust.amplitude must be 0..2");
            if (!(gust.weakest >= 0f && gust.weakest <= 1f)) errors.Add("meadow.breeze.gust.weakest must be 0..1");
            if (!(gust.jitter >= 0f && gust.jitter <= 0.35f)) errors.Add("meadow.breeze.gust.jitter must be 0..0.35");
            if (!(gust.frontWobble >= 0f && gust.frontWobble <= 20f)) errors.Add("meadow.breeze.gust.frontWobble must be 0..20");
            if (!(gust.wobbleLength >= 2f && gust.wobbleLength <= 200f)) errors.Add("meadow.breeze.gust.wobbleLength must be 2..200");
            if (!(gust.lagMetres >= 0f && gust.lagMetres <= 10f)) errors.Add("meadow.breeze.gust.lagMetres must be 0..10");
            if (!(gust.springBack >= 0f && gust.springBack <= 0.6f)) errors.Add("meadow.breeze.gust.springBack must be 0..0.6");
            if (!(gust.brightness >= 0f && gust.brightness <= 2f)) errors.Add("meadow.breeze.gust.brightness must be 0..2");
            // BreezeModel looks at the gusts of four neighbouring cells. A gust that is wider than that window (or pushed out of it by the lag of the tip and the wobble of the front) would be cut off
            // at the edge of its cell, a step in the wind at every edge. 4.5 widths is how far the back dip of a gust reaches at most; the window leaves 2 spacings for it.
            if (!(GustWidthFactor * gust.width + gust.lagMetres + gust.frontWobble <= GustReachShare * gust.spacing))
                errors.Add($"meadow.breeze.gust: {GustWidthFactor} x width + lagMetres + frontWobble must be at most {GustReachShare} x spacing (a wider or more shifted gust would be cut off at the edge of its cell)");
        }

        static void CheckClouds(MeadowCloudsDto clouds, List<string> errors)
        {
            if (clouds == null) { errors.Add("meadow.sky.clouds is missing"); return; }
            if (!(clouds.driftMps >= 0f && clouds.driftMps <= 30f)) errors.Add("meadow.sky.clouds.driftMps must be 0..30");
            if (!(clouds.fadeFromRow >= 0f && clouds.fadeToRow > clouds.fadeFromRow && clouds.fadeToRow <= 0.6f)) errors.Add("meadow.sky.clouds.fadeFromRow/fadeToRow must be 0 <= fromRow < toRow <= 0.6");
            if (!(clouds.hueShare >= 0f && clouds.hueShare <= 1f)) errors.Add("meadow.sky.clouds.hueShare must be 0..1");
            if (!(clouds.litBrightness >= 1f && clouds.litBrightness <= 2f)) errors.Add("meadow.sky.clouds.litBrightness must be 1..2");
            if (!(clouds.shadeBrightness >= 0.3f && clouds.shadeBrightness <= 1f)) errors.Add("meadow.sky.clouds.shadeBrightness must be 0.3..1");
            CheckCloudLayer(clouds.near, "meadow.sky.clouds.near", errors);
            CheckCloudLayer(clouds.far, "meadow.sky.clouds.far", errors);
            if (clouds.shadow == null) errors.Add("meadow.sky.clouds.shadow is missing");
            else
            {
                if (!(clouds.shadow.cellMetres >= 1f && clouds.shadow.cellMetres <= 500f)) errors.Add("meadow.sky.clouds.shadow.cellMetres must be 1..500");
                if (!(clouds.shadow.coverage >= 0f && clouds.shadow.coverage <= 1f)) errors.Add("meadow.sky.clouds.shadow.coverage must be 0..1");
                if (!(clouds.shadow.softness >= 0.02f && clouds.shadow.softness <= 0.5f)) errors.Add("meadow.sky.clouds.shadow.softness must be 0.02..0.5");
                if (!(clouds.shadow.strength >= 0f && clouds.shadow.strength <= 0.8f)) errors.Add("meadow.sky.clouds.shadow.strength must be 0..0.8");
            }
        }

        static void CheckCloudLayer(CloudLayerDto layer, string name, List<string> errors)
        {
            if (layer == null) { errors.Add(name + " is missing"); return; }
            if (!(layer.altitude >= 1f && layer.altitude <= 2000f)) errors.Add(name + ".altitude must be 1..2000");
            if (!(layer.cellMetres >= 1f && layer.cellMetres <= 2000f)) errors.Add(name + ".cellMetres must be 1..2000");
            if (!(layer.coverage >= 0f && layer.coverage <= 1f)) errors.Add(name + ".coverage must be 0..1");
            if (!(layer.softness >= 0.02f && layer.softness <= 0.5f)) errors.Add(name + ".softness must be 0.02..0.5");
            if (!(layer.opacity >= 0f && layer.opacity <= 1f)) errors.Add(name + ".opacity must be 0..1");
        }

        static void CheckWave(string name, float amplitude, float speed, float lifeSec, List<string> errors)
        {
            if (!(amplitude >= 0f && amplitude <= 5f)) errors.Add(name + ".amplitude must be 0..5");
            if (!(speed >= 0f && speed <= 200f)) errors.Add(name + ".speed must be 0..200");
            if (!(lifeSec > 0f && lifeSec <= 30f)) errors.Add(name + ".lifeSec must be > 0 and <= 30");
        }

        static void CheckGrass(MeadowGrassDto grass, List<string> errors)
        {
            if (grass == null) { errors.Add("meadow.grass is missing"); return; }
            if (!(grass.heightMin > 0.02f && grass.heightMax >= grass.heightMin && grass.heightMax <= 3f)) errors.Add("meadow.grass.heightMin/heightMax must be 0.02 < min <= max <= 3");
            if (!(grass.width > 0.002f && grass.width <= 0.5f)) errors.Add("meadow.grass.width must be 0.002..0.5");
            if (!(grass.lean >= 0f && grass.lean <= 1f)) errors.Add("meadow.grass.lean must be 0..1");
            if (!(grass.clumpScale >= 0.1f && grass.clumpScale <= 20f)) errors.Add("meadow.grass.clumpScale must be 0.1..20");
            if (!(grass.clumpLean >= 0f && grass.clumpLean <= 1f)) errors.Add("meadow.grass.clumpLean must be 0..1");
            if (!(grass.hummockScale >= 1f && grass.hummockScale <= 50f)) errors.Add("meadow.grass.hummockScale must be 1..50");
            if (!(grass.hummockHeight >= 0f && grass.hummockHeight <= 0.9f)) errors.Add("meadow.grass.hummockHeight must be 0..0.9");
            if (!(grass.hummockDensity >= 0f && grass.hummockDensity <= 0.9f)) errors.Add("meadow.grass.hummockDensity must be 0..0.9");
            if (!(grass.hummockDark >= 0f && grass.hummockDark <= 0.9f)) errors.Add("meadow.grass.hummockDark must be 0..0.9");
            if (!(grass.fadeToRow >= 0.1f && grass.fadeFromRow > grass.fadeToRow && grass.fadeFromRow <= 0.95f)) errors.Add("meadow.grass.fadeFromRow/fadeToRow must be 0.1 <= toRow < fromRow <= 0.95");
            if (!(grass.fadeMinHeight >= 0f && grass.fadeMinHeight <= 1f)) errors.Add("meadow.grass.fadeMinHeight must be 0..1");
            if (!(grass.driedShare >= 0f && grass.driedShare <= 0.5f)) errors.Add("meadow.grass.driedShare must be 0..0.5");
            if (!(grass.tipGlow >= 0f && grass.tipGlow <= 20f)) errors.Add("meadow.grass.tipGlow must be 0..20");
            if (!(grass.tipRim >= 0f && grass.tipRim <= 2f)) errors.Add("meadow.grass.tipRim must be 0..2");
            if (!(grass.nearDarkness >= 0.05f && grass.nearDarkness <= 1f)) errors.Add("meadow.grass.nearDarkness must be 0.05..1");
            if (!(grass.nearDarkStart >= 0f && grass.nearDarkEnd > grass.nearDarkStart && grass.nearDarkEnd <= 30f)) errors.Add("meadow.grass.nearDarkStart/nearDarkEnd must be 0 <= start < end <= 30");
            if (!(grass.farDim >= 0.1f && grass.farDim <= 1.5f)) errors.Add("meadow.grass.farDim must be 0.1..1.5");
            if (!(grass.farDimFrom >= 0f && grass.farDimTo > grass.farDimFrom && grass.farDimTo <= 50f)) errors.Add("meadow.grass.farDimFrom/farDimTo must be 0 <= from < to <= 50");
            if (!(grass.sunColumn >= 0f && grass.sunColumn <= 64f)) errors.Add("meadow.grass.sunColumn must be 0..64");
            CheckColor(grass.yellowColor, "meadow.grass.yellowColor", errors);
            CheckColor(grass.driedColor, "meadow.grass.driedColor", errors);
            if (!(grass.backlight >= 0f && grass.backlight <= 20f)) errors.Add("meadow.grass.backlight must be 0..20");
            if (!(grass.sunBody >= 0f && grass.sunBody <= 5f)) errors.Add("meadow.grass.sunBody must be 0..5");
            if (!(grass.relief >= 0f && grass.relief <= 20f)) errors.Add("meadow.grass.relief must be 0..20");
            if (!(grass.ambientMix >= 0f && grass.ambientMix <= 5f)) errors.Add("meadow.grass.ambientMix must be 0..5");
            if (!(grass.rim >= 0f && grass.rim <= 10f)) errors.Add("meadow.grass.rim must be 0..10");
            if (!(grass.sparkle >= 0f && grass.sparkle <= 1f)) errors.Add("meadow.grass.sparkle must be 0..1");
            CheckColor(grass.rootColor, "meadow.grass.rootColor", errors);
            CheckColor(grass.midColor, "meadow.grass.midColor", errors);
            CheckColor(grass.tipColor, "meadow.grass.tipColor", errors);
            CheckColor(grass.backlightColor, "meadow.grass.backlightColor", errors);
            CheckColor(grass.glowColor, "meadow.grass.glowColor", errors);
            if (grass.rings == null || grass.rings.Length == 0 || grass.rings.Length > MaxRings) { errors.Add($"meadow.grass.rings needs 1..{MaxRings} bands"); return; }
            for (var i = 0; i < grass.rings.Length; i++)
            {
                var ring = grass.rings[i];
                var name = $"meadow.grass.rings[{i}]";
                if (ring == null) { errors.Add(name + " is empty"); continue; }
                if (!(ring.zFrom > 0f && ring.zTo > ring.zFrom && ring.zTo <= 100f)) errors.Add(name + ": zFrom must be > 0 and zTo above it (<= 100)");
                if (i > 0 && grass.rings[i - 1] != null && ring.zFrom < grass.rings[i - 1].zTo - 1e-4f) errors.Add(name + ".zFrom must not start before the previous band ends");
                if (!(ring.densityScale > 0f && ring.densityScale <= 1f)) errors.Add(name + ".densityScale must be > 0 and <= 1");
                if (!(ring.widthScale >= 0.5f && ring.widthScale <= 20f)) errors.Add(name + ".widthScale must be 0.5..20");
                if (!(ring.segments >= 1 && ring.segments <= 8)) errors.Add(name + ".segments must be 1..8");
                if (!(ring.darkness >= 0.05f && ring.darkness <= 2f)) errors.Add(name + ".darkness must be 0.05..2");
                if (!(ring.heightScale >= 0.2f && ring.heightScale <= 3f)) errors.Add(name + ".heightScale must be 0.2..3");
            }
        }

        static void CheckQuality(MeadowQualityDto[] quality, List<string> errors)
        {
            if (quality == null || quality.Length == 0) { errors.Add("meadow.quality needs at least one tier"); return; }
            var names = new HashSet<string>();
            for (var i = 0; i < quality.Length; i++)
            {
                var tier = quality[i];
                if (tier == null || string.IsNullOrWhiteSpace(tier.name)) { errors.Add($"meadow.quality[{i}].name is empty"); continue; }
                if (!names.Add(tier.name)) errors.Add($"meadow.quality: duplicate tier '{tier.name}'");
                if (!(tier.fireflyMax >= 0 && tier.fireflyMax <= 500)) errors.Add($"meadow.quality[{i}].fireflyMax must be 0..500");
                if (!(tier.flowerCount >= 0 && tier.flowerCount <= 20000)) errors.Add($"meadow.quality[{i}].flowerCount must be 0..20000");
                if (!(tier.grassPerSqm >= 1f && tier.grassPerSqm <= 20000f)) errors.Add($"meadow.quality[{i}].grassPerSqm must be 1..20000");
                if (!(tier.grassMaxSegments >= 1 && tier.grassMaxSegments <= 8)) errors.Add($"meadow.quality[{i}].grassMaxSegments must be 1..8");
                CheckRenderLevers(tier, i, errors);
            }
        }

        static readonly string[] Upscalers = { "Auto", "Linear", "Point", "FSR" };
        static readonly string[] BloomFilters = { "Gaussian", "Dual", "Kawase" };
        static readonly string[] BloomDownscales = { "Half", "Quarter" };

        static void CheckRenderLevers(MeadowQualityDto tier, int i, List<string> errors)
        {
            var at = $"meadow.quality[{i}]";
            if (!(tier.renderScale >= 0.3f && tier.renderScale <= 1f)) errors.Add($"{at}.renderScale must be 0.3..1");
            if (Array.IndexOf(Upscalers, tier.upscaler) < 0) errors.Add($"{at}.upscaler must be Auto, Linear, Point or FSR");
            if (!(tier.msaa == 0 || tier.msaa == 1 || tier.msaa == 2 || tier.msaa == 4 || tier.msaa == 8)) errors.Add($"{at}.msaa must be 0 (the asset's own), 1, 2, 4 or 8");
            if (!(tier.targetFps == 0 || (tier.targetFps >= 10 && tier.targetFps <= 240))) errors.Add($"{at}.targetFps must be 0 (the display's vertical sync) or 10..240");
            if (!(tier.skyDetail >= 0f && tier.skyDetail <= 1f)) errors.Add($"{at}.skyDetail must be 0..1");
            if (tier.bloom == null) return;
            if (Array.IndexOf(BloomFilters, tier.bloom.filter) < 0) errors.Add($"{at}.bloom.filter must be Gaussian, Dual or Kawase");
            if (Array.IndexOf(BloomDownscales, tier.bloom.downscale) < 0) errors.Add($"{at}.bloom.downscale must be Half or Quarter");
            if (!(tier.bloom.maxIterations >= 1 && tier.bloom.maxIterations <= 16)) errors.Add($"{at}.bloom.maxIterations must be 1..16");
        }

        static void CheckFlowers(MeadowFlowersDto flowers, List<string> errors)
        {
            if (flowers == null) { errors.Add("meadow.flowers is missing"); return; }
            if (!(flowers.maxPerNote >= 0 && flowers.maxPerNote <= 40)) errors.Add("meadow.flowers.maxPerNote must be 0..40");
            if (!(flowers.blend >= 0f && flowers.blend <= 0.3f)) errors.Add("meadow.flowers.blend must be 0..0.3");
            if (!(flowers.harpGlow >= 0f && flowers.harpGlow <= 5f)) errors.Add("meadow.flowers.harpGlow must be 0..5");
            if (!(flowers.clearRadius >= 0f && flowers.clearRadius <= 1f)) errors.Add("meadow.flowers.clearRadius must be 0..1");
            if (!(flowers.budGlow >= 0f && flowers.budGlow <= 10f)) errors.Add("meadow.flowers.budGlow must be 0..10");
            if (!(flowers.openGlow >= 0f && flowers.openGlow <= 10f)) errors.Add("meadow.flowers.openGlow must be 0..10");
            if (!(flowers.ambientMix >= 0f && flowers.ambientMix <= 10f)) errors.Add("meadow.flowers.ambientMix must be 0..10");
            if (!(flowers.sunBody >= 0f && flowers.sunBody <= 10f)) errors.Add("meadow.flowers.sunBody must be 0..10");
            if (!(flowers.translucency >= 0f && flowers.translucency <= 20f)) errors.Add("meadow.flowers.translucency must be 0..20");
            CheckColor(flowers.stemColor, "meadow.flowers.stemColor", errors);
            CheckBloom(flowers.bloom, errors);
            CheckLook(flowers.bellflower, "meadow.flowers.bellflower", errors);
            CheckLook(flowers.dandelion, "meadow.flowers.dandelion", errors);
            CheckLook(flowers.poppy, "meadow.flowers.poppy", errors);
        }

        static void CheckParticles(MeadowParticlesDto p, List<string> errors)
        {
            if (p == null) { errors.Add("meadow.particles is missing"); return; }
            CheckColor(p.seedColor, "meadow.particles.seedColor", errors);
            CheckColor(p.petalColor, "meadow.particles.petalColor", errors);
            CheckColor(p.fireflyColor, "meadow.particles.fireflyColor", errors);
            CheckColor(p.pollenColor, "meadow.particles.pollenColor", errors);
            if (!(p.seedLifeSec >= 0.5f && p.seedLifeSec <= 30f)) errors.Add("meadow.particles.seedLifeSec must be 0.5..30");
            if (!(p.petalLifeSec >= 0.5f && p.petalLifeSec <= 30f)) errors.Add("meadow.particles.petalLifeSec must be 0.5..30");
            if (!(p.fireflyLifeSec >= 1f && p.fireflyLifeSec <= 60f)) errors.Add("meadow.particles.fireflyLifeSec must be 1..60");
            if (!(p.seedSize >= 0.005f && p.seedSize <= 0.5f)) errors.Add("meadow.particles.seedSize must be 0.005..0.5");
            if (!(p.petalSize >= 0.005f && p.petalSize <= 0.5f)) errors.Add("meadow.particles.petalSize must be 0.005..0.5");
            if (!(p.fireflySize >= 0.005f && p.fireflySize <= 0.5f)) errors.Add("meadow.particles.fireflySize must be 0.005..0.5");
            if (!(p.pollenSize >= 0.002f && p.pollenSize <= 0.2f)) errors.Add("meadow.particles.pollenSize must be 0.002..0.2");
            if (!(p.windFollow >= 0f && p.windFollow <= 20f)) errors.Add("meadow.particles.windFollow must be 0..20");
            if (!(p.windDrift >= 0f && p.windDrift <= 3f)) errors.Add("meadow.particles.windDrift must be 0..3");
            if (!(p.riseSpeed >= 0f && p.riseSpeed <= 5f)) errors.Add("meadow.particles.riseSpeed must be 0..5");
            if (!(p.sinkSpeed >= 0f && p.sinkSpeed <= 5f)) errors.Add("meadow.particles.sinkSpeed must be 0..5");
            if (!(p.pollenCount >= 0 && p.pollenCount <= 2000)) errors.Add("meadow.particles.pollenCount must be 0..2000");
            if (!(p.pollenDrift >= 0f && p.pollenDrift <= 2f)) errors.Add("meadow.particles.pollenDrift must be 0..2");
            if (!(p.fireflyHeightMin >= 0f && p.fireflyHeightMax > p.fireflyHeightMin && p.fireflyHeightMax <= 4f)) errors.Add("meadow.particles.fireflyHeightMin/Max must be 0 <= min < max <= 4");
            if (!(p.fireflyGlow >= 0f && p.fireflyGlow <= 30f)) errors.Add("meadow.particles.fireflyGlow must be 0..30");
            if (!(p.seedGlow >= 0f && p.seedGlow <= 30f)) errors.Add("meadow.particles.seedGlow must be 0..30");
            if (!(p.maxSeeds >= 1 && p.maxSeeds <= 5000)) errors.Add("meadow.particles.maxSeeds must be 1..5000");
            if (!(p.maxPetals >= 1 && p.maxPetals <= 5000)) errors.Add("meadow.particles.maxPetals must be 1..5000");
            if (!(p.spawnSpread >= 0f && p.spawnSpread <= 3f)) errors.Add("meadow.particles.spawnSpread must be 0..3");
            if (!(p.spawnHeightMin >= 0f && p.spawnHeightMax >= p.spawnHeightMin && p.spawnHeightMax <= 3f)) errors.Add("meadow.particles.spawnHeightMin/Max must be 0 <= min <= max <= 3");
            if (!(p.fireflyFadeFraction > 0f && p.fireflyFadeFraction < 0.5f)) errors.Add("meadow.particles.fireflyFadeFraction must be above 0 and below 0.5");
            if (!(p.particleMinPixels >= 0f && p.particleMinPixels <= 20f)) errors.Add("meadow.particles.particleMinPixels must be 0..20");
            if (p.fireflyAnswerCount < 0 || p.fireflyAnswerCount > 20) errors.Add("meadow.particles.fireflyAnswerCount must be 0..20");
            if (!(p.fireflyAnswerRadius > 0f && p.fireflyAnswerRadius <= 20f)) errors.Add("meadow.particles.fireflyAnswerRadius must be > 0 and <= 20");
            if (!(p.fireflyAnswerAttackSec >= 0.01f && p.fireflyAnswerAttackSec <= 2f)) errors.Add("meadow.particles.fireflyAnswerAttackSec must be 0.01..2");
            if (!(p.fireflyAnswerDecaySec > p.fireflyAnswerAttackSec && p.fireflyAnswerDecaySec <= 5f)) errors.Add("meadow.particles.fireflyAnswerDecaySec must be above the attack and at most 5");
            if (!(p.fireflyAnswerGain >= 0f && p.fireflyAnswerGain <= 5f)) errors.Add("meadow.particles.fireflyAnswerGain must be 0..5");
            if (!(p.fireflyBlinkCount >= 1 && p.fireflyBlinkCount <= 30)) errors.Add("meadow.particles.fireflyBlinkCount must be 1..30");
            if (!(p.fireflyBlinkLow >= 0f && p.fireflyBlinkLow <= 1f)) errors.Add("meadow.particles.fireflyBlinkLow must be 0..1");
            if (!(p.fireflyNoiseStrength >= 0f && p.fireflyNoiseStrength <= 3f)) errors.Add("meadow.particles.fireflyNoiseStrength must be 0..3");
            if (!(p.fireflyNoiseFrequency >= 0.01f && p.fireflyNoiseFrequency <= 5f)) errors.Add("meadow.particles.fireflyNoiseFrequency must be 0.01..5");
            if (!(p.fireflyFloor >= 0f && p.fireflyFloor <= 2f)) errors.Add("meadow.particles.fireflyFloor must be 0..2");
            if (!(p.fireflyEdgeMargin >= 0f && p.fireflyEdgeMargin <= 3f)) errors.Add("meadow.particles.fireflyEdgeMargin must be 0..3");
        }

        static void CheckRibbon(MeadowRibbonDto r, List<string> errors)
        {
            if (r == null) { errors.Add("meadow.ribbon is missing"); return; }
            CheckColor(r.color, "meadow.ribbon.color", errors);
            CheckColor(r.tailColor, "meadow.ribbon.tailColor", errors);
            if (!(r.trailSec >= 0.1f && r.trailSec <= 5f)) errors.Add("meadow.ribbon.trailSec must be 0.1..5");
            if (!(r.width >= 0.01f && r.width <= 2f)) errors.Add("meadow.ribbon.width must be 0.01..2");
            if (!(r.height >= 0f && r.height <= 3f)) errors.Add("meadow.ribbon.height must be 0..3");
            if (!(r.appearSec >= 0.01f && r.appearSec <= 2f)) errors.Add("meadow.ribbon.appearSec must be 0.01..2");
            if (!(r.fadeSec >= 0.05f && r.fadeSec <= 5f)) errors.Add("meadow.ribbon.fadeSec must be 0.05..5");
            if (!(r.glow >= 0f && r.glow <= 30f)) errors.Add("meadow.ribbon.glow must be 0..30");
            if (!(r.minVertexDistance >= 0.005f && r.minVertexDistance <= 0.5f)) errors.Add("meadow.ribbon.minVertexDistance must be 0.005..0.5");
            if (!(r.headAlpha >= 0f && r.headAlpha <= 1f)) errors.Add("meadow.ribbon.headAlpha must be 0..1");
            if (!(r.midAlpha >= 0f && r.midAlpha <= 1f)) errors.Add("meadow.ribbon.midAlpha must be 0..1");
            if (!(r.midWidth >= 0f && r.midWidth <= 1f)) errors.Add("meadow.ribbon.midWidth must be 0..1");
            if (!(r.followMinHz >= 0f && r.followMinHz <= 30f)) errors.Add("meadow.ribbon.followMinHz must be 0..30 (0 turns the smoothing off)");
            if (!(r.followBeta >= 0f && r.followBeta <= 50f)) errors.Add("meadow.ribbon.followBeta must be 0..50");
            if (!(r.followSpeedHz >= 0.1f && r.followSpeedHz <= 30f)) errors.Add("meadow.ribbon.followSpeedHz must be 0.1..30");
        }

        static void CheckBloom(BloomDto bloom, List<string> errors)
        {
            if (bloom == null) { errors.Add("meadow.flowers.bloom is missing"); return; }
            if (!(bloom.leadSec >= 0f && bloom.leadSec <= 0.3f)) errors.Add("meadow.flowers.bloom.leadSec must be 0..0.3");
            if (!(bloom.openSec >= 0.05f && bloom.openSec <= 5f)) errors.Add("meadow.flowers.bloom.openSec must be 0.05..5");
            if (!(bloom.holdSec >= 0f && bloom.holdSec <= 60f)) errors.Add("meadow.flowers.bloom.holdSec must be 0..60");
            if (!(bloom.closeSec >= 0.1f && bloom.closeSec <= 60f)) errors.Add("meadow.flowers.bloom.closeSec must be 0.1..60");
            if (!(bloom.restLevel >= 0f && bloom.restLevel <= 1f)) errors.Add("meadow.flowers.bloom.restLevel must be 0..1");
        }

        static void CheckLook(FlowerLookDto look, string name, List<string> errors)
        {
            if (look == null) { errors.Add(name + " is missing"); return; }
            CheckColor(look.budColor, name + ".budColor", errors);
            CheckColor(look.openColor, name + ".openColor", errors);
            CheckColor(look.centerColor, name + ".centerColor", errors);
            if (!(look.stemHeight >= 0.05f && look.stemHeight <= 2f)) errors.Add(name + ".stemHeight must be 0.05..2");
            if (!(look.headSize >= 0.01f && look.headSize <= 0.5f)) errors.Add(name + ".headSize must be 0.01..0.5");
        }

        static void CheckColor(string value, string name, List<string> errors)
        {
            if (!ColorHex.TryParse(value, out _)) errors.Add($"{name} must be a colour like #RRGGBB (got '{value}')");
        }
    }
}
