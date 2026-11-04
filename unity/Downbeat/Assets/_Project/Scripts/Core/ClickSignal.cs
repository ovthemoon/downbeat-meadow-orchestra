using System;

namespace Downbeat.Core
{
    /// <summary>The click used to measure the beat clock: a 3 kHz sine burst with a 3 ms exponential decay, 30 ms long. Same formula as tools/gen_click.mjs.</summary>
    public static class ClickSignal
    {
        const double FrequencyHz = 3000.0;
        const double DurationSec = 0.03;
        const double DecaySec = 0.003;
        const double Amplitude = 0.6;

        public static float[] Render(int sampleRate)
        {
            var frames = (int)Math.Round(sampleRate * DurationSec);
            var click = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var t = (double)i / sampleRate;
                click[i] = (float)(Amplitude * Math.Sin(2.0 * Math.PI * FrequencyHz * t) * Math.Exp(-t / DecaySec));
            }
            return click;
        }
    }
}
