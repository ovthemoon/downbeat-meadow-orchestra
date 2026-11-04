using System.Collections;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>
    /// The pictures of the visual polish (Part D) that cannot be judged from numbers: a poppy up close, the fireflies answering a bell, the hummocks, the sky at the end of a strong
    /// performance. Each test draws the real scene into <c>captures/visual</c> (and, with <c>DOWNBEAT_KEEP_CAPTURES=1</c>, a JPEG into <c>measurements/visual</c>) and checks only that
    /// something was drawn; the numbers behind each item have their own tests.
    /// </summary>
    public class PartDVisualPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const double FakeHeardNow = 2000.0;

        static IEnumerator Load(System.Action<MeadowStage> done, params string[] extraArgs)
        {
            var args = new System.Collections.Generic.List<string> { "Downbeat", "--input", "mouse", "--mute", "--no-record" };
            args.AddRange(extraArgs);
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var app = Object.FindFirstObjectByType<DownbeatLifetimeScope>().Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment: " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            if (app.Record != null) app.Record.View.Visible = false;
            done(Object.FindFirstObjectByType<MeadowStage>());
        }

        /// <summary>Draws the scene from a camera put close to a point and looking at it; the camera is put back.</summary>
        static Texture2D CloseUp(MeadowStage stage, Vector3 lookAt, Vector3 from, float fieldOfView, int width, int height)
        {
            var camera = stage.Camera;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var fov = camera.fieldOfView;
            try
            {
                camera.transform.position = from;
                camera.transform.rotation = Quaternion.LookRotation(lookAt - from, Vector3.up);
                camera.fieldOfView = fieldOfView;
                return MeadowCapture.Render(camera, width, height);
            }
            finally
            {
                camera.transform.position = position;
                camera.transform.rotation = rotation;
                camera.fieldOfView = fov;
            }
        }

        static bool Drawn(Texture2D picture)
        {
            var pixels = picture.GetPixels32();
            var sum = 0L;
            for (var i = 0; i < pixels.Length; i += 37) sum += pixels[i].r + pixels[i].g + pixels[i].b;
            return sum > 1000;
        }

        /// <summary>The poppy nearest to the middle of the field whose stem is tall enough to stand in the light.</summary>
        static int PoppyNear(MeadowStage stage, float x, float z)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            var instances = stage.Flowers.Instances;
            for (var i = 0; i < instances.Length; i++)
            {
                var flower = instances[i];
                if (flower.Species != FlowerSpecies.Poppy || flower.Height < 0.95f) continue;
                var d = (flower.X - x) * (flower.X - x) + (flower.Z - z) * (flower.Z - z);
                if (d < bestDistance) { best = i; bestDistance = d; }
            }
            return best;
        }

        [UnityTest]
        public IEnumerator Poppies_UpClose_AreCuppedAndLayered()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High");
            stage.Backdrop.SetSunset(0.5f);
            var index = PoppyNear(stage, 0f, 3.6f);
            Assert.GreaterOrEqual(index, 0, "there is a poppy to look at");
            var flower = stage.Flowers.Instances[index];
            var look = stage.Settings.flowers.poppy;
            stage.Flowers.Bloom(new[] { index }, FakeHeardNow);
            stage.Flowers.Tick(FakeHeardNow + 1.0);   // open
            var ground = stage.Terrain.HeightAt(flower.X, flower.Z);
            var head = new Vector3(flower.X + 0.06f * flower.Scale, ground + look.stemHeight * flower.Height * flower.Scale + look.headSize * 0.3f * flower.Scale, flower.Z);

            var front = CloseUp(stage, head, head + new Vector3(0f, 0.12f, -0.55f), 28f, 1280, 720);
            var side = CloseUp(stage, head, head + new Vector3(0.45f, 0.18f, -0.35f), 28f, 1280, 720);
            CaptureOutput.SavePicture(front, "partd-poppy-front.png");
            CaptureOutput.SavePicture(side, "partd-poppy-side.png");

            Assert.IsTrue(Drawn(front));
            Assert.IsTrue(Drawn(side));
            Object.Destroy(front);
            Object.Destroy(side);
        }

        [UnityTest]
        public IEnumerator Fireflies_AtDusk_AreWarmPointsOfLight()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High", "--fireflies", "1", "--sky", "0.25");
            yield return new WaitForSecondsRealtime(9f);   // they are born one by one over their life
            Assert.Greater(stage.Particles.AliveFireflies, 100, "the fireflies have come");

            var whole = MeadowCapture.Render(stage.Camera, 1920, 1080);
            CaptureOutput.SavePicture(whole, "partd-fireflies.png");
            var ground = stage.Terrain.HeightAt(0f, 4f);
            var zoom = CloseUp(stage, new Vector3(0f, ground + 0.7f, 4.5f), new Vector3(0f, ground + 0.8f, 1.2f), 22f, 1600, 900);
            CaptureOutput.SavePicture(zoom, "partd-fireflies-zoom.png");

            // a bell: the nearest two glow, here in the middle of the picture
            Assert.Greater(stage.Particles.AnswerBell(new FieldPoint(0f, 4.6f)), 0);
            yield return new WaitForSecondsRealtime(0.07f);
            var answer = CloseUp(stage, new Vector3(0f, ground + 0.7f, 4.5f), new Vector3(0f, ground + 0.8f, 1.2f), 22f, 1600, 900);
            CaptureOutput.SavePicture(answer, "partd-fireflies-answer.png");

            Assert.IsTrue(Drawn(whole));
            Object.Destroy(whole);
            Object.Destroy(zoom);
            Object.Destroy(answer);
        }

        /// <summary>The weight of the hills' pulse, how near the plate is to the fog colour and the plate's luma at screen positions (x, y 0..1, y up), drawn by the probe shader from the real plate and the real function.</summary>
        static Vector3[] ProbeHills(Vector2[] places)
        {
            var shader = Shader.Find("Hidden/Downbeat/HillPulseProbe");
            Assert.IsNotNull(shader, "the probe shader is in the project");
            var material = new Material(shader);
            var input = new Texture2D(places.Length, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var target = RenderTexture.GetTemporary(places.Length, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var output = new Texture2D(places.Length, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                var colours = new Color[places.Length];
                for (var i = 0; i < places.Length; i++) colours[i] = new Color(places[i].x, places[i].y, 0f, 0f);
                input.SetPixels(colours);
                input.Apply();
                Shader.SetGlobalTexture("_ProbeIn", input);
                var previous = RenderTexture.active;
                Graphics.Blit(null, target, material);
                RenderTexture.active = target;
                output.ReadPixels(new Rect(0, 0, places.Length, 1), 0, 0);
                output.Apply();
                RenderTexture.active = previous;
                var read = output.GetPixels();
                var result = new Vector3[places.Length];
                for (var i = 0; i < places.Length; i++) result[i] = new Vector3(read[i].r, read[i].g, read[i].b);
                return result;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(input);
                Object.DestroyImmediate(output);
            }
        }

        [UnityTest]
        public IEnumerator HillPulse_FollowsThePaintedHazeOfTheHills_NotOneFlatBand()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High");
            stage.Backdrop.SetSunset(0.5f);
            var nature = stage.Bundle.Dto.nature;
            var row = nature.hillPulseRow;
            var half = nature.hillPulseHalfHeight;
            Assert.Greater(nature.hillPulseFollow, 0f, "the shipped pulse follows the plate");

            // a line of places across the picture over the hills, at the middle of the band's upper half (the 3D grass hides the lower half)
            const int Across = 256;
            var screenY = 1f - (row - half * 0.4f);   // up from the bottom
            var places = new Vector2[Across];
            for (var i = 0; i < Across; i++) places[i] = new Vector2((i + 0.5f) / Across, screenY);

            Object.Destroy(MeadowCapture.Render(stage.Camera, 64, 36));   // a camera has drawn the plate at this window's shape: the probe reads the same globals
            stage.Backdrop.SetHillPulse(1f, row, half, 0f);
            var flat = ProbeHills(places);
            stage.Backdrop.SetHillPulse(1f, row, half, nature.hillPulseFollow);
            var follow = ProbeHills(places);

            float flatMin = float.MaxValue, flatMax = float.MinValue, followMin = float.MaxValue, followMax = float.MinValue, followMean = 0f;
            float hazeSum = 0f, weightSum = 0f, hazeWeightSum = 0f, hazeSquares = 0f, weightSquares = 0f;
            for (var i = 0; i < Across; i++)
            {
                flatMin = Mathf.Min(flatMin, flat[i].x); flatMax = Mathf.Max(flatMax, flat[i].x);
                followMin = Mathf.Min(followMin, follow[i].x); followMax = Mathf.Max(followMax, follow[i].x);
                followMean += follow[i].x / Across;
                hazeSum += follow[i].y; weightSum += follow[i].x; hazeWeightSum += follow[i].x * follow[i].y; hazeSquares += follow[i].y * follow[i].y; weightSquares += follow[i].x * follow[i].x;
            }
            var correlation = (hazeWeightSum - hazeSum * weightSum / Across) / Mathf.Sqrt(Mathf.Max(1e-9f, (hazeSquares - hazeSum * hazeSum / Across) * (weightSquares - weightSum * weightSum / Across)));
            float lumaMin = float.MaxValue, lumaMax = float.MinValue;
            for (var i = 0; i < Across; i++) { lumaMin = Mathf.Min(lumaMin, follow[i].z); lumaMax = Mathf.Max(lumaMax, follow[i].z); }
            Debug.Log($"[PartD] the plate over the hills at that row has luma {lumaMin:0.000}..{lumaMax:0.000}");
            Debug.Log($"[PartD] hill pulse over the hills at one row: flat band {flatMin:0.000}..{flatMax:0.000}; following the plate {followMin:0.000}..{followMax:0.000} (mean {followMean:0.000}), correlation with the plate's haze {correlation:0.00}");

            Assert.AreEqual(flatMin, flatMax, 1e-5f, "a flat band lights every place of a row alike");
            Assert.Greater(followMax - followMin, 0.25f * followMax, "one that follows the plate does not: it is stronger where the hills are hazy");
            Assert.Greater(correlation, 0.9f, "and the more hazy the plate, the stronger the pulse");
            Assert.Greater(followMean, 0.1f, "but it is still there over the hills");
        }

        static float MeanAbsoluteDifferenceRows(Texture2D a, Texture2D b, float fromRow, float toRow)
        {
            var y0 = Mathf.RoundToInt((1f - toRow) * a.height);
            var y1 = Mathf.RoundToInt((1f - fromRow) * a.height);
            var pa = a.GetPixels(0, y0, a.width, y1 - y0);
            var pb = b.GetPixels(0, y0, b.width, y1 - y0);
            var sum = 0f;
            for (var i = 0; i < pa.Length; i++) sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (3f * pa.Length);
        }

        /// <summary>A soft round disc (white, the alpha falls off from the middle) for the probe particle.</summary>
        static Texture2D Disc()
        {
            const int Size = 32;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var d = new Vector2(x + 0.5f - Size / 2f, y + 0.5f - Size / 2f).magnitude / (Size / 2f);
                    var a = Mathf.Clamp01(1f - d);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            texture.Apply();
            return texture;
        }

        /// <summary>The sum of the brightness over a square of a picture around a screen position (pixels from the bottom left).</summary>
        static float LightAround(Texture2D picture, Vector2 at, int half)
        {
            var x0 = Mathf.Clamp(Mathf.RoundToInt(at.x) - half, 0, picture.width - 2 * half);
            var y0 = Mathf.Clamp(Mathf.RoundToInt(at.y) - half, 0, picture.height - 2 * half);
            var pixels = picture.GetPixels(x0, y0, 2 * half, 2 * half);
            var sum = 0f;
            foreach (var pixel in pixels) sum += 0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b;
            return sum;
        }

        [UnityTest]
        public IEnumerator FarParticles_StayAFewPixelsAcross_NotAFlickerUnderAPixel()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High");
            stage.Backdrop.SetSunset(0.3f);
            Assert.GreaterOrEqual(stage.Settings.particles.particleMinPixels, 3f, "the shipped floor is a few pixels");

            // one small particle in the open air above the grass, 4 m from the camera (particles beyond about 5 m are in the haze that hands over to the plate, and fade), drawn by the very shader and vertex streams of the meadow's particles
            var camera = stage.Camera;
            var host = new GameObject("size floor probe");
            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 100f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            ParticleDirector.ConfigureStreams(system, renderer);
            var material = new Material(Shader.Find("Downbeat/Meadow/Particle")) { mainTexture = Disc() };
            material.SetFloat("_Gain", 3f);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            renderer.sharedMaterial = material;
            var forward = camera.transform.forward;
            forward.y = 0f;
            var place = camera.transform.position + forward.normalized * 4f;
            place.y = stage.Terrain.HeightAt(place.x, place.z) + 0.6f;
            system.Play();
            system.Emit(new ParticleSystem.EmitParams { position = place, velocity = Vector3.zero, startLifetime = 100f, startSize = 0.03f, startColor = Color.white }, 1);
            yield return null;
            yield return null;
            var screen = (Vector2)camera.WorldToScreenPoint(place);
            var at = new Vector2(screen.x * 1920f / camera.pixelWidth, screen.y * 1080f / camera.pixelHeight);

            renderer.enabled = false;
            var nothing = MeadowCapture.Render(camera, 1920, 1080);
            renderer.enabled = true;
            Debug.Log($"[PartD] particle alive: {system.particleCount}; light with the probe switched off: {LightAround(nothing, at, 20):0.0}");
            material.SetFloat("_MinPixels", 0f);
            var without = MeadowCapture.Render(camera, 1920, 1080);
            material.SetFloat("_MinPixels", stage.Settings.particles.particleMinPixels);
            var shipped = MeadowCapture.Render(camera, 1920, 1080);
            material.SetFloat("_MinPixels", 24f);
            var big = MeadowCapture.Render(camera, 1920, 1080);
            // the answer to a bell travels on the same streams: the same particle with the number 1 on Custom1.x is brighter than with 0
            material.SetFloat("_MinPixels", 0f);
            material.SetFloat("_AnswerGain", 3f);
            var calmData = new System.Collections.Generic.List<Vector4> { Vector4.zero };
            var glowData = new System.Collections.Generic.List<Vector4> { new Vector4(1f, 0f, 0f, 0f) };
            system.SetCustomParticleData(calmData, ParticleSystemCustomData.Custom1);
            var calm = MeadowCapture.Render(camera, 1920, 1080);
            system.SetCustomParticleData(glowData, ParticleSystemCustomData.Custom1);
            var glowing = MeadowCapture.Render(camera, 1920, 1080);
            var lightCalm = LightAround(calm, at, 20);
            var lightGlowing = LightAround(glowing, at, 20);
            Object.Destroy(calm);
            Object.Destroy(glowing);
            var lightWithout = LightAround(without, at, 20);
            var lightShipped = LightAround(shipped, at, 20);
            var lightBig = LightAround(big, at, 20);
            Debug.Log($"[PartD] a particle 3 cm across, 4 m away at ({at.x:0}, {at.y:0}): the light round it is {lightWithout:0.0} without the floor, {lightShipped:0.0} with the shipped floor, {lightBig:0.0} with a floor of 24 pixels; with the answer of a bell {lightGlowing:0.0} against {lightCalm:0.0} without");
            CaptureOutput.SavePicture(shipped, "partd-seed-floor.png");
            CaptureOutput.SavePicture(big, "partd-seed-floor-big.png");
            Object.Destroy(without);
            Object.Destroy(shipped);
            Object.Destroy(big);
            Object.Destroy(host);
            Object.Destroy(material);

            Assert.AreEqual(lightWithout, lightShipped, lightWithout * 0.01f, "a particle that is near enough is not touched by the shipped floor (it is 9 pixels across)");
            Assert.Greater(lightGlowing - lightCalm, 0.5f * (lightCalm - lightWithout) + 2f, "a bell makes the particle glow brighter: the number reaches the shader");
            Assert.Greater(lightBig, lightWithout * 1.15f, "the floor is in pixels: one that is larger than the particle makes it bigger on the screen, in the shader, with no pass on the CPU");
        }

        /// <summary>The mean brightness, and the share of pixels that are nearly white, over the rows of a picture between two heights (0 = top).</summary>
        static (float mean, float bright) GrassLight(Texture2D picture, float fromRow, float toRow)
        {
            var y0 = Mathf.RoundToInt((1f - toRow) * picture.height);
            var y1 = Mathf.RoundToInt((1f - fromRow) * picture.height);
            var pixels = picture.GetPixels(0, y0, picture.width, y1 - y0);
            float sum = 0f;
            var bright = 0;
            foreach (var pixel in pixels)
            {
                var luma = 0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b;
                sum += luma;
                if (luma > 0.92f) bright++;
            }
            return (sum / pixels.Length, bright / (float)pixels.Length);
        }

        [UnityTest]
        public IEnumerator TipGlint_IsSubtle_WithinTheBloomLimits_AndHighOnly()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High");
            stage.Backdrop.SetSunset(0.5f);
            var rim = stage.Settings.grass.tipRim;
            Assert.Greater(rim, 0f, "the shipped grass has the rim");
            var cheap = Shader.PropertyToID("_MeadowSkyCheap");

            Shader.SetGlobalFloat(GrassRenderer.TipRimId, 0f);
            var without = MeadowCapture.Render(stage.Camera, 1920, 1080);
            Shader.SetGlobalFloat(GrassRenderer.TipRimId, rim);
            var with = MeadowCapture.Render(stage.Camera, 1920, 1080);
            Shader.SetGlobalFloat(cheap, 1f);   // a weak tier: the same two pictures
            var weak = MeadowCapture.Render(stage.Camera, 1920, 1080);
            Shader.SetGlobalFloat(GrassRenderer.TipRimId, 0f);
            var weakWithout = MeadowCapture.Render(stage.Camera, 1920, 1080);
            Shader.SetGlobalFloat(GrassRenderer.TipRimId, rim);
            Shader.SetGlobalFloat(cheap, 0f);
            CaptureOutput.SavePicture(with, "partd-tiprim-on.png");
            CaptureOutput.SavePicture(without, "partd-tiprim-off.png");

            var off = GrassLight(without, 0.55f, 0.95f);
            var on = GrassLight(with, 0.55f, 0.95f);
            var weakOn = GrassLight(weak, 0.55f, 0.95f);
            var weakOff = GrassLight(weakWithout, 0.55f, 0.95f);
            var gain = on.mean / off.mean - 1f;
            Debug.Log($"[PartD] tip rim {rim}: the grass is {gain * 100f:0.00}% brighter on average, the share of nearly white pixels goes from {off.bright * 100f:0.000}% to {on.bright * 100f:0.000}% (a weak tier: {weakOff.mean:0.00000} with, {weakOn.mean:0.00000} without)");
            Object.Destroy(without);
            Object.Destroy(with);
            Object.Destroy(weak);
            Object.Destroy(weakWithout);

            Assert.Greater(gain, 0.001f, "there is a rim");
            Assert.Less(gain, 0.06f, "and it is subtle: a few per cent of brightness at most");
            Assert.Less(on.bright - off.bright, 0.002f, "within the bloom limits: it adds hardly any nearly white pixel for the glow to catch");
            Assert.AreEqual(weakOff.mean, weakOn.mean, 1e-6f, "a weak tier (the cheap sky) has no rim: it is High only");
        }

        [UnityTest]
        public IEnumerator Hummocks_AreRealGround_TheGrassIsPlantedOnThem_AndTheHandOverToThePlateIsUntouched()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High", "--wind-mute", "all");   // a still meadow: the two pictures differ by the ground alone
            stage.Backdrop.SetSunset(0.5f);
            var terrain = stage.Settings.terrain;
            Assert.Greater(terrain.reliefHeight, 0f);

            // the grass stands on the same surface as the ground mesh, the flowers and the particles: the map its placement reads is the height function, relief included
            var random = new System.Random(4);
            var worst = 0f;
            var rises = 0f;
            for (var i = 0; i < 300; i++)
            {
                var z = 1.5f + (float)random.NextDouble() * 4f;
                var x = ((float)random.NextDouble() - 0.5f) * 2f * stage.Layout.HalfWidthAt(z);
                var ground = stage.Terrain.HeightAt(x, z);
                worst = Mathf.Max(worst, Mathf.Abs(stage.Grass.PlantedHeightAt(x, z) - ground));
                rises = Mathf.Max(rises, Mathf.Abs(MeadowTerrain.Relief(x, z, terrain.seed, new TerrainRelief(terrain.reliefHeight, terrain.reliefWavelength, terrain.reliefFromZ, terrain.reliefToZ))));
            }
            Assert.Less(worst, 0.01f, "the blades are planted on the ground the mesh is drawn at");
            Assert.Greater(rises, 0.5f * terrain.reliefHeight, "and there is relief to be planted on");

            // the same scene without the relief, from the same camera
            var with = MeadowCapture.Render(stage.Camera, 1920, 1080);
            CaptureOutput.SavePicture(with, "partd-hummocks-on.png");
            var kept = terrain.reliefHeight;
            terrain.reliefHeight = 0f;
            stage.Configure(stage.Bundle, stage.QualityName);
            stage.Backdrop.SetSunset(0.5f);
            yield return null;
            var without = MeadowCapture.Render(stage.Camera, 1920, 1080);
            CaptureOutput.SavePicture(without, "partd-hummocks-off.png");
            terrain.reliefHeight = kept;

            var endRow = stage.Layout.ScreenYForDepth(terrain.reliefToZ);   // the picture row of the ground where the relief has faded to nothing
            var near = MeanAbsoluteDifferenceRows(with, without, 0.75f, 0.98f);
            var handOver = MeanAbsoluteDifferenceRows(with, without, Mathf.Max(0f, endRow - 0.07f), Mathf.Max(0.01f, endRow - 0.01f));   // beyond it: the far meadow and the hand-over to the plate
            var plate = MeanAbsoluteDifferenceRows(with, without, 0.0f, 0.45f);
            Debug.Log($"[PartD] relief on and off (the relief ends at picture row {endRow:0.00}): the picture differs by {near:0.0000} near the camera, {handOver:0.0000} over the hand-over to the plate, {plate:0.0000} over the hills and sky");
            Object.Destroy(with);
            Object.Destroy(without);

            Assert.Greater(near, 0.004f, "the near field looks different: hummocks");
            Assert.Less(handOver, near * 0.25f, "the line where the 3D ground hands over to the plate hardly changes");
            Assert.Less(plate, 0.004f, "and the plate and the sky not at all (the clouds have drifted a frame)");
        }

        // ---- the background moves at every tier (the complaint of 01:49: a static background is boring) ----------------------------------------------

        static IEnumerator BackgroundMoves(MeadowStage stage, bool weakTier, System.Action<float, float, float, float> report)
        {
            stage.Backdrop.SetSunset(0.5f);
            var meadow = stage.Settings;
            var breeze = new BreezeGpu(meadow.breeze, meadow.sky.clouds);
            var field = new BreezeField();
            var cheap = RenderLevers.SkyCheapId;
            try
            {
                Shader.SetGlobalFloat(cheap, weakTier ? 1f : 0f);
                Texture2D At(double time)
                {
                    breeze.Upload(time);
                    field.Bake();
                    return MeadowCapture.Render(stage.Camera, 1280, 720);
                }
                var first = At(100.0);
                var nextFrame = At(100.0 + 1.0 / 60.0);
                var later = At(130.0);
                const float SkyFrom = 0.03f, SkyTo = 0.22f, FarFrom = 0.40f, FarTo = 0.46f;
                report(MeanAbsoluteDifferenceRows(first, later, SkyFrom, SkyTo), MeanAbsoluteDifferenceRows(first, nextFrame, SkyFrom, SkyTo),
                    MeanAbsoluteDifferenceRows(first, later, FarFrom, FarTo), MeanAbsoluteDifferenceRows(first, nextFrame, FarFrom, FarTo));
                if (weakTier) CaptureOutput.SavePicture(later, "partd-low-background-later.png");
                if (weakTier) CaptureOutput.SavePicture(first, "partd-low-background-first.png");
                Object.Destroy(first);
                Object.Destroy(nextFrame);
                Object.Destroy(later);
            }
            finally
            {
                Shader.SetGlobalFloat(cheap, 0f);
                field.Dispose();
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TheBackgroundMoves_AtTheWeakTierToo_AndDoesNotFlicker()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "Low", "--wind-mute", "all");
            float sky = 0f, skyStep = 0f, far = 0f, farStep = 0f;
            yield return BackgroundMoves(stage, true, (a, b, c, d) => { sky = a; skyStep = b; far = c; farStep = d; });
            Debug.Log($"[PartD] background at the weak tier: the sky differs by {sky:0.0000} over 30 s and {skyStep:0.00000} over one frame; the far meadow {far:0.0000} and {farStep:0.00000}");

            Assert.Greater(sky, 0.003f, "the clouds drift at the weak tier");
            Assert.Greater(far, 0.0015f, "and the gusts and the cloud shadows go over the painted far meadow");
            Assert.Less(skyStep, 0.25f * sky, "and a frame later the picture is the same to within a small step: no flicker");
            Assert.Less(farStep, 0.25f * far);
        }

        [UnityTest]
        public IEnumerator TheBackgroundMoves_AtTheFullTier()
        {
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High", "--wind-mute", "all");
            float sky = 0f, skyStep = 0f, far = 0f, farStep = 0f;
            yield return BackgroundMoves(stage, false, (a, b, c, d) => { sky = a; skyStep = b; far = c; farStep = d; });
            Debug.Log($"[PartD] background at the full tier: the sky differs by {sky:0.0000} over 30 s and {skyStep:0.00000} over one frame; the far meadow {far:0.0000} and {farStep:0.00000}");

            Assert.Greater(sky, 0.003f);
            Assert.Greater(far, 0.0015f);
            Assert.Less(skyStep, 0.25f * sky);
            Assert.Less(farStep, 0.25f * far);
        }

        [UnityTest]
        public IEnumerator TheWeakTiersClouds_AreTheSameCloudsAsTheStrongTiersDraws()
        {
            // the weak tier reads the cloud noise from a baked tile, the strong tier works it out per pixel: at one moment they must draw the same clouds in the same places
            MeadowStage stage = null;
            yield return Load(s => stage = s, "--quality", "High", "--wind-mute", "all");
            stage.Backdrop.SetSunset(0.5f);
            var breeze = new BreezeGpu(stage.Settings.breeze, stage.Settings.sky.clouds);
            var field = new BreezeField();
            var cheap = RenderLevers.SkyCheapId;
            Texture2D exact = null, tile = null, drifted = null;
            try
            {
                breeze.Upload(100.0);
                field.Bake();
                Shader.SetGlobalFloat(cheap, 0f);
                exact = MeadowCapture.Render(stage.Camera, 1280, 720);
                Shader.SetGlobalFloat(cheap, 1f);
                tile = MeadowCapture.Render(stage.Camera, 1280, 720);
                breeze.Upload(130.0);
                field.Bake();
                Shader.SetGlobalFloat(cheap, 0f);
                drifted = MeadowCapture.Render(stage.Camera, 1280, 720);
            }
            finally
            {
                Shader.SetGlobalFloat(cheap, 0f);
                field.Dispose();
            }
            const float SkyFrom = 0.03f, SkyTo = 0.22f, FarFrom = 0.40f, FarTo = 0.46f;
            var tierDifference = MeanAbsoluteDifferenceRows(exact, tile, SkyFrom, SkyTo);
            var driftDifference = MeanAbsoluteDifferenceRows(exact, drifted, SkyFrom, SkyTo);
            var farDifference = MeanAbsoluteDifferenceRows(exact, tile, FarFrom, FarTo);
            Debug.Log($"[PartD] the sky differs {tierDifference:0.00000} between the two ways of drawing the clouds, {driftDifference:0.00000} between two moments 30 s apart; the far meadow {farDifference:0.00000} between the two ways");
            CaptureOutput.SavePicture(tile, "partd-low-clouds-tile.png");
            CaptureOutput.SavePicture(exact, "partd-low-clouds-exact.png");
            Object.Destroy(exact);
            Object.Destroy(tile);
            Object.Destroy(drifted);

            Assert.Greater(driftDifference, 0.003f, "the clouds of the strong tier drift");
            Assert.Less(tierDifference, 0.3f * driftDifference, "and the tile draws the same clouds, with a little less detail, in the same places");
            Assert.Less(farDifference, 0.0005f, "the far meadow is drawn the same way at both tiers");
        }

        [UnityTest]
        public IEnumerator TheWeakTier_DrawsTheSkyTheCheapWay_ButStillDrawsIt()
        {
            // skyDetail 0 of the Low tier is "the cheap way" (the clouds from the baked tile), not "left out": the clouds are there in the picture
            yield return Load(_ => { }, "--quality", "Low", "--wind-mute", "all");

            Assert.AreEqual(1f, Shader.GetGlobalFloat(RenderLevers.SkyCheapId), "Low reads the clouds from the tile");
            Assert.NotNull(Shader.GetGlobalTexture(Shader.PropertyToID("_MeadowCloudTile")), "and the tile is there");
        }

        [UnityTest]
        public IEnumerator TheStrongTier_WorksTheCloudNoiseOutForEveryPixel()
        {
            yield return Load(_ => { }, "--quality", "High", "--wind-mute", "all");

            Assert.AreEqual(0f, Shader.GetGlobalFloat(RenderLevers.SkyCheapId));
        }
    }
}
