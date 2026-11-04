using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Where things are on the meadow, in metres: X to the right, Z away from the camera, the camera at x 0, z 0. The playing field is the part of
    /// the ground a hand can reach: from <see cref="NearZ"/> (the bottom edge of the picture) to <see cref="FarZ"/> (the top of the hand's range).
    /// It is a wedge that follows the view: <see cref="FieldWidth"/> is its width at <see cref="FarZ"/> and it narrows in a straight line to the
    /// camera, so the left, middle and right third of the picture are the left, middle and right third of the field at every depth.
    /// Depth follows the perspective of the ground: a point at depth z is drawn <c>horizon + (1 - horizon) * nearZ / z</c> of the picture height
    /// from the top, and the hand's height range is spread evenly over the screen rows between the far edge and the bottom edge, so a hand and the
    /// flowers it plays are at the same place on screen. The camera of content <c>meadow.camera</c> is chosen so that this holds.
    /// </summary>
    public sealed class MeadowLayout
    {
        public MeadowLayout(float fieldWidth, float nearZ, float farZ, float horizonScreenY)
        {
            if (!(fieldWidth > 0f)) throw new ArgumentOutOfRangeException(nameof(fieldWidth), "must be > 0");
            if (!(nearZ > 0f)) throw new ArgumentOutOfRangeException(nameof(nearZ), "must be > 0");
            if (!(farZ > nearZ)) throw new ArgumentOutOfRangeException(nameof(farZ), "must be above nearZ");
            if (!(horizonScreenY > 0f && horizonScreenY < 1f)) throw new ArgumentOutOfRangeException(nameof(horizonScreenY), "must be between 0 and 1 (exclusive)");
            FieldWidth = fieldWidth;
            NearZ = nearZ;
            FarZ = farZ;
            HorizonScreenY = horizonScreenY;
        }

        /// <summary>Width of the field at <see cref="FarZ"/>.</summary>
        public float FieldWidth { get; }
        public float NearZ { get; }
        public float FarZ { get; }
        /// <summary>Where the horizon is on screen, as a fraction of the picture height measured from the top.</summary>
        public float HorizonScreenY { get; }

        /// <summary>Screen row (0 = top, 1 = bottom) of the farthest ground of the field.</summary>
        public float FarScreenY => ScreenYForDepth(FarZ);

        /// <summary>Half the width of the field at depth <paramref name="z"/>.</summary>
        public float HalfWidthAt(float z) => FieldWidth * 0.5f * z / FarZ;

        /// <summary>
        /// A hand position in the camera image (x 0..1 left to right as the player sees it, y 0..1 from the top, as in <see cref="HandFrame"/>)
        /// on the field. The top of the image is the far end of the field, the bottom the near end. Values outside 0..1 are clamped.
        /// </summary>
        public FieldPoint HandToField(float handX01, float handY01)
        {
            var u = Clamp01(handX01);
            var y = Clamp01(handY01);
            var screenY = FarScreenY + (1f - FarScreenY) * y;
            var z = DepthForScreenY(screenY);
            return new FieldPoint((u - 0.5f) * FieldWidth * z / FarZ, z);
        }

        /// <summary>The hand height (image y, 0 = top) whose position on the field has depth <paramref name="z"/>.</summary>
        public float HandYForDepth(float z)
        {
            var screenY = ScreenYForDepth(z);
            return Clamp01((screenY - FarScreenY) / (1f - FarScreenY));
        }

        /// <summary>
        /// Depth of the ground that plays pitch step <paramref name="step"/> of <paramref name="pitchSteps"/>: the depth under a hand held at the middle of
        /// the height band of that step (<see cref="PitchHeightMap"/>). A higher step is a farther depth.
        /// </summary>
        public float DepthForPitchStep(int step, int pitchSteps)
        {
            if (pitchSteps < 1) throw new ArgumentOutOfRangeException(nameof(pitchSteps));
            var clamped = step < 0 ? 0 : (step >= pitchSteps ? pitchSteps - 1 : step);
            var height = PitchHeightMap.VerticalMargin + (clamped + 0.5f) / pitchSteps * PitchHeightMap.UsableHeight;
            return HandToField(0.5f, 1f - height).Z;
        }

        /// <summary>Where the point (<paramref name="x"/>, <paramref name="z"/>) is across the picture: 0 left edge of the field, 0.5 the centre line, 1 right edge.</summary>
        public float LateralFraction(float x, float z) => 0.5f + x / (FieldWidth * z / FarZ);

        /// <summary>Screen row (0 = top, 1 = bottom) where ground at depth <paramref name="z"/> is drawn; <paramref name="z"/> is limited to the field.</summary>
        public float ScreenYForDepth(float z)
        {
            var clamped = z < NearZ ? NearZ : (z > FarZ ? FarZ : z);
            return HorizonScreenY + (1f - HorizonScreenY) * NearZ / clamped;
        }

        float DepthForScreenY(float screenY)
        {
            var below = screenY - HorizonScreenY;
            if (below <= 0f) return FarZ;
            var z = (1f - HorizonScreenY) * NearZ / below;
            return z < NearZ ? NearZ : (z > FarZ ? FarZ : z);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
