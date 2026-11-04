namespace Downbeat.Core
{
    /// <summary>Where notes come from. Chosen with --input hand|mouse|replay. Hand = the Python hand tracker over UDP; one that is not running falls back to the mouse.</summary>
    public enum InputMode
    {
        Mouse,
        Hand,
        Replay,
    }

    public static class InputModeParser
    {
        /// <summary>--input hand|mouse|replay on the command line; without it the hand tracker (UDP) is asked for.</summary>
        public static InputMode FromArgs(LaunchArgs args) => Parse(args?.Get("input"));

        /// <summary>"mouse" and "replay" are explicit; anything else (including nothing) asks for the hand tracker.</summary>
        public static InputMode Parse(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "mouse": return InputMode.Mouse;
                case "replay": return InputMode.Replay;
                default: return InputMode.Hand;
            }
        }
    }

    public readonly struct ModeDecision
    {
        public InputMode Active { get; }
        /// <summary>Short text for the on-screen badge: what is playing the instruments and why.</summary>
        public string Label { get; }

        public ModeDecision(InputMode active, string label)
        {
            Active = active;
            Label = label;
        }
    }

    /// <summary>Decides which input is active from what was asked for and what the camera tracker reports.</summary>
    public static class InputModeResolver
    {
        public static ModeDecision Resolve(InputMode requested, TrackerState tracker, string replayName)
        {
            switch (requested)
            {
                case InputMode.Mouse:
                    return new ModeDecision(InputMode.Mouse, "Mouse mode: click the stage");
                case InputMode.Replay:
                    return new ModeDecision(InputMode.Replay, string.IsNullOrEmpty(replayName) ? "Replay: recorded hand motion" : "Replay: recorded hand motion (" + replayName + ")");
                default:
                    return ResolveCamera(tracker);
            }
        }

        static ModeDecision ResolveCamera(TrackerState tracker)
        {
            switch (tracker)
            {
                case TrackerState.Running:
                    return new ModeDecision(InputMode.Hand, "Camera mode: strike downward with your hand");
                case TrackerState.CameraDenied:
                    return Fallback("camera permission was denied");
                case TrackerState.CameraMissing:
                    return Fallback("no camera found");
                case TrackerState.CameraError:
                    return Fallback("the camera could not be opened");
                case TrackerState.ModelFailed:
                    return Fallback("the hand model could not be loaded");
                default:
                    return new ModeDecision(InputMode.Mouse, "Mouse mode for now: camera is starting");
            }
        }

        static ModeDecision Fallback(string reason) => new ModeDecision(InputMode.Mouse, "Mouse mode: " + reason);
    }
}
