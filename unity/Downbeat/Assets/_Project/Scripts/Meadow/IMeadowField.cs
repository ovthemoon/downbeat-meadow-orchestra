using Downbeat.Core;

namespace Downbeat.Meadow
{
    /// <summary>The ground and the wind of the meadow, as the things that live on it (particles) see them.</summary>
    public interface IMeadowField
    {
        float HeightAt(float x, float z);
        WindField Wind { get; }
        /// <summary>The clock of the wind (seconds of the scene).</summary>
        double WindTime { get; }
        /// <summary>The way the steady wind blows, on the ground (unit length; zero before the meadow is built).</summary>
        FieldPoint WindDirection { get; }
    }
}
