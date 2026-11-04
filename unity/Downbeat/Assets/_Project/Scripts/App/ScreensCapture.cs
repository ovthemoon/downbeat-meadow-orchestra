using System.IO;
using Downbeat.Core;
using Downbeat.Meadow;
using Downbeat.Record;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--capture-screens PATH [--capture-after SEC]</c>: both screens of the exhibition side by side in one picture (3840 x 1080): the meadow as the first screen shows it, the record as the
    /// second one shows it. Each is drawn into a picture of its own, so it works on a laptop with one screen. Never the desktop, never a camera.
    /// </summary>
    public sealed class ScreensCapture
    {
        const int ScreenWidth = 1920;
        const int ScreenHeight = 1080;
        const double DefaultAfterSec = 12.0;

        readonly string _path;
        readonly double _afterSec;
        readonly Camera _meadowCamera;
        readonly RecordScreenView _record;
        bool _done;

        public ScreensCapture(LaunchArgs args, Camera meadowCamera, RecordScreenView record)
        {
            var path = args.Get("capture-screens");
            _path = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
            _afterSec = args.TryGetDouble("capture-after", out var after) ? after : DefaultAfterSec;
            _meadowCamera = meadowCamera;
            _record = record;
        }

        public bool IsActive => _path != null && !_done;

        public void Tick(double realtimeSec, double heardNow)
        {
            if (!IsActive || realtimeSec < _afterSec) return;
            _done = true;
            var meadow = MeadowCapture.Render(_meadowCamera, ScreenWidth, ScreenHeight);
            var record = _record.RenderPicture(ScreenWidth, ScreenHeight, heardNow);
            var both = new Texture2D(ScreenWidth * 2, ScreenHeight, TextureFormat.RGBA32, false);
            both.SetPixels32(0, 0, ScreenWidth, ScreenHeight, meadow.GetPixels32());
            both.SetPixels32(ScreenWidth, 0, ScreenWidth, ScreenHeight, record.GetPixels32());
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllBytes(_path, both.EncodeToPNG());
            Object.Destroy(meadow);
            Object.Destroy(record);
            Object.Destroy(both);
            Debug.Log($"[ScreensCapture] meadow and record side by side: {_path}");
        }
    }
}
