namespace Downbeat.Core
{
    /// <summary>A point on the meadow ground: X to the right, Z away from the camera (metres). Core has no UnityEngine, so this stands in for a Vector2.</summary>
    public readonly struct FieldPoint
    {
        public float X { get; }
        public float Z { get; }

        public FieldPoint(float x, float z)
        {
            X = x;
            Z = z;
        }
    }
}
