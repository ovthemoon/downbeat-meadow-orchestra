using UnityEngine;

namespace Downbeat.Record
{
    /// <summary>Destroys a Unity object whether the app is playing or (in the edit-mode tests) not.</summary>
    public static class Disposal
    {
        public static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
