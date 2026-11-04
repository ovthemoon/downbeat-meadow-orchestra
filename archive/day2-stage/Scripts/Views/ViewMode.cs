namespace Downbeat.Views
{
    public enum ViewMode
    {
        Both,
        StageOnly,
        AudienceOnly,
    }

    public static class ViewModeParser
    {
        public static ViewMode Parse(string value)
        {
            switch (value)
            {
                case "stage": return ViewMode.StageOnly;
                case "audience": return ViewMode.AudienceOnly;
                default: return ViewMode.Both;
            }
        }
    }
}
