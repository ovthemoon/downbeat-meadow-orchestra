using System;
using Downbeat.Core;
using Downbeat.Record;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>The list of screens, behind an interface so that the routing is tested with any number of them.</summary>
    public interface IDisplayApi
    {
        int Count { get; }
        void Activate(int index);
    }

    public sealed class UnityDisplayApi : IDisplayApi
    {
        public int Count => Display.displays.Length;
        public void Activate(int index) => Display.displays[index].Activate();
    }

    /// <summary>
    /// Decides which screen shows what: with two screens the meadow on one and the record on the other (both activated), with one the record is a strip over the lower part of the meadow that
    /// the Tab key hides and shows again. <c>display.mode</c> of content.json and <c>--display auto|dual|single</c> on the command line choose; one screen is always a strip.
    /// </summary>
    public sealed class DisplayRouter
    {
        readonly IDisplayApi _displays;

        public DisplayRouter(ContentDto content, LaunchArgs launch, IDisplayApi displays, bool isMac = false)
        {
            _displays = displays ?? throw new ArgumentNullException(nameof(displays));
            var preference = launch.Has("display") ? launch.Get("display") : content.display.mode;
            Mode = DisplayPlan.Resolve(displays.Count, preference, isMac);
            Assignment = DisplayAssignment.For(Mode, content.display.meadowIndex, content.display.recordIndex, Math.Max(1, displays.Count));
        }

        public DisplayMode Mode { get; }
        public DisplayAssignment Assignment { get; }
        public RecordMount Mount => Mode == DisplayMode.Dual ? RecordMount.Display : RecordMount.Strip;
        /// <summary>Whether the strip is shown (Tab toggles it). Always true on two screens.</summary>
        public bool StripShown { get; private set; } = true;

        /// <summary>Sends the meadow camera to its screen and switches the screens on (screen 0 is always on).</summary>
        public void Apply(Camera meadowCamera)
        {
            meadowCamera.targetDisplay = Assignment.MeadowDisplay;
            if (Mode != DisplayMode.Dual) return;
            ActivateIfNeeded(Assignment.MeadowDisplay);
            ActivateIfNeeded(Assignment.RecordDisplay);
        }

        /// <summary>Hides and shows the strip (the strip switch of the settings panel that Tab opens calls this); on two screens the record stays.</summary>
        public bool ToggleStrip()
        {
            if (Mode == DisplayMode.Dual) return StripShown;
            StripShown = !StripShown;
            return StripShown;
        }

        void ActivateIfNeeded(int index)
        {
            if (index > 0) _displays.Activate(index);
        }
    }
}
