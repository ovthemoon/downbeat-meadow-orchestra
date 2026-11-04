namespace Downbeat.Core
{
    /// <summary>What the tutorial looks like at one moment: how much of it is there (fading in or out), which of its three steps is on, how far into that step, and which steps have been ticked.</summary>
    public readonly struct TutorialFrame
    {
        readonly int _checks;

        public TutorialFrame(float alpha, int step, float stepProgress, bool checked0, bool checked1, bool checked2)
        {
            Alpha = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
            Step = step < 0 ? 0 : step >= TutorialSettings.StepCount ? TutorialSettings.StepCount - 1 : step;
            StepProgress = stepProgress < 0f ? 0f : stepProgress > 1f ? 1f : stepProgress;
            _checks = (checked0 ? 1 : 0) | (checked1 ? 2 : 0) | (checked2 ? 4 : 0);
        }

        /// <summary>The nothing of a hidden tutorial (what <c>default</c> is).</summary>
        public static TutorialFrame Hidden => default;

        /// <summary>0 (not there) .. 1 (fully there): the fade in and the fade out.</summary>
        public float Alpha { get; }
        public bool IsVisible => Alpha > 0f;
        /// <summary>The step on show, 0..2.</summary>
        public int Step { get; }
        /// <summary>0 at the start of the step, 1 at its end.</summary>
        public float StepProgress { get; }

        /// <summary>The visitor has done what the step asks (an open hand, a move to another cell, two fists).</summary>
        public bool IsChecked(int step) => step >= 0 && step < TutorialSettings.StepCount && (_checks & (1 << step)) != 0;
    }
}
