namespace Downbeat.Core
{
    /// <summary>
    /// Whether the small settings panel of the Tab key is open. Tab opens and closes it; it closes by itself when nobody touches it for <see cref="IdleCloseSec"/>, so an operator who walks away
    /// does not leave it over the meadow. The times are seconds of any clock that does not stop (the caller passes the same clock every time).
    /// </summary>
    public sealed class SettingsPanelState
    {
        /// <summary>The panel closes this long after the last Tab or touch.</summary>
        public const double IdleCloseSec = 10.0;

        double _lastTouch;

        public bool IsOpen { get; private set; }

        /// <summary>Tab: opens a closed panel, closes an open one.</summary>
        public void ToggleOpen(double now)
        {
            IsOpen = !IsOpen;
            _lastTouch = now;
        }

        /// <summary>Somebody used the panel: the ten seconds start again. A closed panel stays closed.</summary>
        public void Touch(double now)
        {
            if (IsOpen) _lastTouch = now;
        }

        /// <summary>Once a frame: closes the panel that has been left alone for <see cref="IdleCloseSec"/>.</summary>
        public void Tick(double now)
        {
            if (IsOpen && now - _lastTouch >= IdleCloseSec) IsOpen = false;
        }
    }
}
