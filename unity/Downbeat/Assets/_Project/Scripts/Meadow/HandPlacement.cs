using System;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// Where a hand is in the meadow, found from where it is on the screen. There is one mapping for everything the visitor sees (<see cref="HandScreenMap"/>): a hand at (x01, y01) of the tracker's picture is drawn at a
    /// viewport point, the top row above the horizon and the bottom row below it. From that point the meadow's camera looks into the world: a hand of the bottom row touches the ground its line of sight meets
    /// (<see cref="TryGround"/>, the ribbon floats above it on the same line, <see cref="TryAboveGround"/>), a hand of the top row is in the air, <see cref="SkyPoint"/> along the same line. So the ribbon, the wind, the flowers
    /// that open and the effects in the air are all seen where the hand is, and a hand that goes up is seen going up. Which row a hand is in is not decided here: it is the row of the cell it plays in
    /// (<see cref="HandCellTracker.TryGetRow"/>, with the dead band of the rows), so a hand held in its row a little past the horizon keeps its wind (<see cref="TryGroundRowHead"/>) or its place in the air.
    /// </summary>
    public sealed class HandPlacement
    {
        const float SkyClearance = 0.3f;           // the least a point in the air is above the ground: a hill in front of the horizon does not swallow it
        const float SkyPullIn = 0.85f;             // a point in the air that is under the ground is brought this much nearer to the camera, along its line of sight
        const int SkyPullInSteps = 8;
        const float HorizonMargin = 0.005f;        // the horizon is a little under the true horizon line of the camera, so that a line of sight just under it meets the ground

        readonly Camera _camera;
        readonly Func<float, float, float> _heightAt;
        readonly MeadowOverlayLayer _overlay;
        readonly MeadowLayout _layout;
        readonly float _groundLevel;

        /// <param name="heightAt">The height of the ground at (x, z) in world metres (<see cref="TerrainView.HeightAt"/>).</param>
        /// <param name="groundLevel">The height of the plane a line of sight is first expected to meet before the hills are looked up (half way up the terrain's range of heights).</param>
        public HandPlacement(Camera camera, Func<float, float, float> heightAt, MeadowOverlayLayer overlay, MeadowLayout layout, float groundLevel)
        {
            _camera = camera;
            _heightAt = heightAt;
            _overlay = overlay;
            _layout = layout;
            _groundLevel = groundLevel;
        }

        /// <summary>How far in front of the camera a hand of the top row is in the air (<c>meadow.skyEffects.distance</c>).</summary>
        public float SkyDistance { get; set; } = 7f;

        /// <summary>The hand height (0 top .. 1 bottom) where the sky row ends and the ground row begins (<c>cells.rowSplit</c>).</summary>
        public float RowSplit => _overlay.RowSplit;

        /// <summary>
        /// The size, in the units of the ribbon's follow filter, of one whole picture of the tracker: the filter was tuned for metres on the field, so the hand's place is scaled by the width the field has half way
        /// between its near and far edge. A hand that crosses the picture in a second moves that many units per second.
        /// </summary>
        public float FollowScale => _layout.FieldWidth * (_layout.NearZ + _layout.FarZ) / (2f * _layout.FarZ);

        /// <summary>The viewport point where a hand at (<paramref name="x01"/>, <paramref name="y01"/>) is drawn.</summary>
        public (float vx, float vy) Viewport(float x01, float y01) => _overlay.ViewportOfHand(x01, y01);

        /// <summary>
        /// The viewport y of the horizon where the rows meet, as <see cref="HandScreenMap"/> uses it (the far edge of the playing field, kept inside the picture above the record strip).
        /// </summary>
        public float HorizonViewport => HandScreenMap.HorizonOf(_overlay.HorizonViewport, _overlay.StripFraction);

        /// <summary>
        /// The row of a hand that is in no cell (a fist, a hand that has only just come in) at this height, the rule <see cref="CellPicker"/> has for a hand it has not seen before: above the split is the sky, on it and below it the ground.
        /// A hand that plays is in the row of its cell instead (<see cref="HandCellTracker.TryGetRow"/>), which holds it through the dead band.
        /// </summary>
        public bool IsSkyRow(float y01) => y01 < RowSplit;

        /// <summary>
        /// The point in the air that the screen point looks at, <see cref="SkyDistance"/> in front of the camera along the camera's own forward direction. A point that would be under a hill
        /// is brought nearer along its line of sight (which only lifts it above the ground; it stays on the same screen point).
        /// </summary>
        public Vector3 SkyPoint(float vx, float vy)
        {
            var distance = SkyDistance;
            var point = _camera.ViewportToWorldPoint(new Vector3(vx, vy, distance));
            for (var step = 0; step < SkyPullInSteps && point.y < _heightAt(point.x, point.z) + SkyClearance; step++)
            {
                distance *= SkyPullIn;
                point = _camera.ViewportToWorldPoint(new Vector3(vx, vy, distance));
            }
            return point;
        }

        /// <summary>The ground the line of sight through the screen point meets. False when it never does (it is above the horizon of the camera).</summary>
        public bool TryGround(float vx, float vy, out FieldPoint ground)
        {
            ground = default;
            if (!TryAboveGround(vx, vy, 0f, out var point)) return false;
            ground = new FieldPoint(point.x, point.z);
            return true;
        }

        /// <summary>
        /// The ground under a hand at (<paramref name="x01"/>, <paramref name="y01"/>): where the line of sight through its screen point meets the ground. A hand above the line between the rows is taken to the line,
        /// the far edge of the field: that is where the wind and the notes of a ground-row hand that the dead band of the rows holds a little above the horizon are put, so they do not jump and no line of sight goes into the sky.
        /// </summary>
        public bool TryGroundOfHand(float x01, float y01, out FieldPoint ground)
        {
            var (vx, vy) = Viewport(x01, y01 < RowSplit ? RowSplit : y01);
            return TryGround(vx, vy, out ground);
        }

        /// <summary>
        /// The point on the line of sight through the screen point that is <paramref name="above"/> metres over the ground (the hills looked up in a few steps). It is on the line of sight whatever the steps found, so it is drawn at the screen point.
        /// </summary>
        public bool TryAboveGround(float vx, float vy, float above, out Vector3 point)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(vx, vy, 0f));
            return TryRayAboveGround(ray, above, out point);
        }

        /// <summary>The same for any line of sight: the first ground it meets, found to within <see cref="GroundRay.Tolerance"/> (<see cref="GroundRay"/>).</summary>
        public bool TryRayAboveGround(Ray ray, float above, out Vector3 point) => GroundRay.TryMeet(ray, above, _groundLevel, _heightAt, out point);

        /// <summary>
        /// The head of the ribbon of a hand of the ground row, <paramref name="above"/> metres over the grass on the line of sight through its screen point. A ground-row hand held by the dead band of the rows a little above
        /// the horizon has the screen point of the top row: its ribbon stays on the line of sight through that point (so it is drawn where the hand is), at the depth of the far edge of the field, where its wind blows
        /// (<see cref="TryGroundOfHand"/>). False when even the horizon has no ground under it.
        /// </summary>
        public bool TryGroundRowHead(float x01, float y01, float above, out Vector3 head)
        {
            var (vx, vy) = Viewport(x01, y01);
            var horizon = HorizonViewport;
            if (vy <= horizon) return TryAboveGround(vx, vy, above, out head);
            head = default;
            if (!TryAboveGround(vx, horizon, above, out var farEdge)) return false;
            head = _camera.ViewportToWorldPoint(new Vector3(vx, vy, _camera.WorldToViewportPoint(farEdge).z));
            return true;
        }

        /// <summary>
        /// The viewport y of the far edge of the playing field, seen by the camera: the horizon where the sky row meets the ground row. Found by projecting the ground at the far end of the field
        /// (<see cref="MeadowLayout.FarZ"/>, in the middle) with the camera, and kept a little under the camera's own horizon line.
        /// </summary>
        public float FarEdgeViewport()
        {
            var far = new Vector3(0f, _heightAt(0f, _layout.FarZ), _layout.FarZ);
            var farEdge = _camera.WorldToViewportPoint(far).y;
            var flat = _camera.transform.position + Vector3.forward * (_layout.FarZ * 100f);   // far away on the level of the eyes: where the horizon line is
            var horizonLine = _camera.WorldToViewportPoint(flat).y;
            return Mathf.Min(farEdge, horizonLine - HorizonMargin);
        }
    }
}
