using System;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// Where a line of sight meets the ground (a surface <c>above</c> metres over the hills). The line is walked in steps until it is under the surface, and the step in which it went under is then halved until it is
    /// <see cref="Tolerance"/> long. The answer is the first ground the line meets (a hill no wider than a step that it only grazes can be missed) and does not depend on where the search began, so a line that falls only a
    /// little per metre (near the horizon, where the hills are as steep as the line) gives the same point every time instead of one that depends on the height of the last guess. Allocation-free and cheap (a dozen or two looks
    /// at the ground for a line that meets it within the field): a hand asks for three of these in every drawn frame.
    /// </summary>
    public static class GroundRay
    {
        /// <summary>The point found is within this many metres, along the line of sight, of the first ground the line meets (so it is within this much of the surface in height too: the hills are gentler than 1 in 1).</summary>
        public const float Tolerance = 0.02f;

        const float SteepestFlatRay = 1e-3f;      // a line of sight that falls less than this per metre never meets the ground
        const float WalkStep = 1.5f;              // metres along the line between two looks at the ground (the hills are wider than this)
        const int MaxWalkSteps = 40;              // a line that falls very slowly is walked in longer steps rather than in more of them
        const int MaxReachDoublings = 4;          // how often the stretch to walk is doubled when the line is still over the ground at the end of it
        const int MaxHalvings = 24;

        /// <param name="ray">The line of sight (its direction is a unit vector, as <see cref="Camera.ViewportPointToRay(Vector3)"/> gives it).</param>
        /// <param name="above">How far over the surface the point is.</param>
        /// <param name="guessLevel">The height of the plane half way up the hills: where the line is first expected to meet the ground, which sets how far it is walked.</param>
        /// <param name="heightAt">The height of the ground at (x, z).</param>
        /// <returns>False when the line never meets the ground (it does not fall, or it starts under the surface).</returns>
        public static bool TryMeet(Ray ray, float above, float guessLevel, Func<float, float, float> heightAt, out Vector3 point)
        {
            point = default;
            if (ray.direction.y >= -SteepestFlatRay || !(Gap(ray, 0f, above, heightAt) > 0f)) return false;

            var reach = Mathf.Max((guessLevel + above - ray.origin.y) / ray.direction.y, WalkStep);
            var gapAtReach = Gap(ray, reach, above, heightAt);
            for (var i = 0; i < MaxReachDoublings && gapAtReach > 0f; i++)
            {
                reach *= 2f;
                gapAtReach = Gap(ray, reach, above, heightAt);
            }
            if (gapAtReach > 0f) return false;

            var over = 0f;       // the last distance at which the line was over the surface
            var under = reach;   // the first at which it is under it
            var step = Mathf.Max(WalkStep, reach / MaxWalkSteps);
            for (var distance = step; distance < reach; distance += step)
            {
                if (Gap(ray, distance, above, heightAt) <= 0f)
                {
                    under = distance;
                    break;
                }
                over = distance;
            }

            for (var i = 0; i < MaxHalvings && under - over > Tolerance; i++)
            {
                var middle = 0.5f * (over + under);
                if (Gap(ray, middle, above, heightAt) > 0f) over = middle;
                else under = middle;
            }
            point = ray.GetPoint(0.5f * (over + under));
            return true;
        }

        // how far the line is over the surface at a distance along it (negative: under it)
        static float Gap(Ray ray, float distance, float above, Func<float, float, float> heightAt)
        {
            var x = ray.origin.x + ray.direction.x * distance;
            var z = ray.origin.z + ray.direction.z * distance;
            return ray.origin.y + ray.direction.y * distance - (heightAt(x, z) + above);
        }
    }
}
