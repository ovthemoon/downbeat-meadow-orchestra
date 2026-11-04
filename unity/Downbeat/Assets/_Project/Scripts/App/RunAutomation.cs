using System.IO;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;
using VContainer.Unity;

namespace Downbeat.App
{
    /// <summary>
    /// Hands-off runs for tests and measurements: <c>--quit-after SEC</c> closes the app after that many seconds of real time, and
    /// <c>--capture PATH [--capture-after SEC] [--capture-size WxH]</c> saves this app's own rendered frame (never the desktop) as a PNG; give
    /// <c>--quit-after</c> at least a second later than <c>--capture-after</c> so the file is written. With <c>--capture-size</c> the meadow camera is drawn once
    /// into an off-screen picture of that size (also in batchmode, where there is no window); without it the window's own frame is saved.
    /// <c>--mute</c> silences the output (notes are still scheduled and counted). <c>--uncapped</c> turns vSync and the frame-rate cap off (measuring how fast the
    /// meadow draws, not just that it holds the display's rate). Without these options it does nothing.
    /// </summary>
    public sealed class RunAutomation : ITickable
    {
        const double DefaultCaptureAfterSec = 3.0;

        readonly double _quitAfterSec;
        readonly double _captureAfterSec;
        readonly string _capturePath;
        readonly bool _uncapped;
        readonly bool _hasCaptureSize;
        readonly int _captureWidth;
        readonly int _captureHeight;
        bool _captureRequested;

        public RunAutomation(LaunchArgs args)
        {
            _quitAfterSec = args.TryGetDouble("quit-after", out var quit) ? quit : double.NaN;
            var path = args.Get("capture");
            _capturePath = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
            _captureAfterSec = args.TryGetDouble("capture-after", out var after) ? after : DefaultCaptureAfterSec;
            _hasCaptureSize = MeadowCapture.TryParseSize(args.Get("capture-size"), out _captureWidth, out _captureHeight);
            _uncapped = args.Has("uncapped");
            if (args.Has("mute")) AudioListener.volume = 0f;
            Debug.Log($"[RunAutomation] screen {Screen.width}x{Screen.height} fullscreen={Screen.fullScreen} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        }

        public void Tick()
        {
            if (_uncapped && (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != -1))
            {
                QualitySettings.vSyncCount = 0; // a quality level switch puts the tier's vSync back, so this is checked every frame
                Application.targetFrameRate = -1;
            }
            var now = Time.realtimeSinceStartupAsDouble;
            if (_capturePath != null && !_captureRequested && now >= _captureAfterSec)
            {
                _captureRequested = true;
                Directory.CreateDirectory(Path.GetDirectoryName(_capturePath));
                if (_hasCaptureSize && Camera.main != null)
                {
                    MeadowCapture.SavePng(Camera.main, _captureWidth, _captureHeight, _capturePath);
                    Debug.Log($"[RunAutomation] captured {_captureWidth}x{_captureHeight}: {_capturePath}");
                }
                else
                {
                    ScreenCapture.CaptureScreenshot(_capturePath);
                    Debug.Log("[RunAutomation] capture requested: " + _capturePath);
                }
            }

            if (!double.IsNaN(_quitAfterSec) && now >= _quitAfterSec)
            {
                Debug.Log("[RunAutomation] quit-after reached");
                Application.Quit();
            }
        }
    }
}
