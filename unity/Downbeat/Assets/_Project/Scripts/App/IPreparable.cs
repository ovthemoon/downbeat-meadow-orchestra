using System;
using System.Collections;

namespace Downbeat.App
{
    /// <summary>Something that has to load data before the game can start (a replay file). A failure stops the boot with a readable message.</summary>
    public interface IPreparable
    {
        IEnumerator Prepare(Action<string> onFailed);
    }
}
