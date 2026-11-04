using Downbeat.Meadow;
using Downbeat.Record;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>The meadow at the climax of a visit as a small PNG (the thumbnail of its card): the meadow camera drawn once into a picture, without the record strip and never a camera image.</summary>
    public static class ClimaxSnapshot
    {
        public static byte[] Capture(Camera meadowCamera, RecordScreenView record, int width, int height)
        {
            var strip = record != null && record.Visible && record.Mount == RecordMount.Strip;
            if (strip) record.Visible = false;
            try
            {
                var picture = MeadowCapture.Render(meadowCamera, width, height);
                var png = picture.EncodeToPNG();
                Disposal.Release(picture);
                return png;
            }
            finally
            {
                if (strip) record.Visible = true;
            }
        }
    }
}
