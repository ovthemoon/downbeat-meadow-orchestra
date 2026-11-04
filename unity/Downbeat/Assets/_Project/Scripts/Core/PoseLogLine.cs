using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>
    /// One line of <c>--log-pose</c> (the diagnostic that tells what the hand pose reading sees, to tune <c>pose.openRatio</c> and <c>pose.fistRatio</c> on the machine it runs on):
    /// <c>[pose] hand=0 index=1.82 middle=1.90 ring=1.75 pinky=1.60 raw=Open pose=Open canPlay=1 cell=2 part=bell</c>.
    /// <c>index</c>..<c>pinky</c> are the finger ratios of the latest picture (<c>n/a</c>: not readable), <c>raw</c> is the pose that this one picture gives, <c>pose</c> the pose the game takes
    /// (<see cref="HandMotion.Pose"/>: it changes only after <c>pose.confirmFrames</c> pictures in a row), <c>canPlay</c> 1 or 0 (<see cref="HandMotion.CanPlay"/>), <c>cell</c> the cell
    /// 0..5 the hand is in (-1: none, with <c>part=none</c>) and <c>part</c> the instrument of that cell.
    /// </summary>
    public static class PoseLogLine
    {
        public const string Tag = "[pose]";
        const string NotAvailable = "n/a";
        const string NoPart = "none";

        public static string Format(int handId, in FingerRatios fingers, HandPose raw, HandPose pose, bool canPlay, int cell)
        {
            var part = cell >= 0 && cell < Parts.Count ? Parts.Name((Part)cell) : NoPart;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} hand={1} index={2} middle={3} ring={4} pinky={5} raw={6} pose={7} canPlay={8} cell={9} part={10}",
                Tag, handId, Ratio(fingers, fingers.Index), Ratio(fingers, fingers.Middle), Ratio(fingers, fingers.Ring), Ratio(fingers, fingers.Pinky),
                raw, pose, canPlay ? 1 : 0, cell, part);
        }

        static string Ratio(in FingerRatios fingers, float value) =>
            !fingers.IsKnown || float.IsNaN(value) || float.IsInfinity(value) ? NotAvailable : value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
