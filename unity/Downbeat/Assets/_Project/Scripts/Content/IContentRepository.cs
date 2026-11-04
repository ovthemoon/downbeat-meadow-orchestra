using System;
using System.Collections;

namespace Downbeat.Content
{
    public interface IContentRepository
    {
        /// <summary>
        /// Coroutine: choose the content (the folder next to the program, else the last known good copy, else the built-in one), validate it, fetch the images. Calls exactly one of the callbacks.
        /// A copy that cannot be used is passed over, with the reason in the log (a sound that Unity cannot open counts); only when even the built-in content is unusable does this fail.
        /// </summary>
        IEnumerator Load(Action<ContentBundle> onLoaded, Action<string> onFailed);

        /// <summary>
        /// Coroutine: read the external content again while the app runs (F5). Unlike <see cref="Load"/> it never falls back to another copy: content that is broken is refused, with the reason, and
        /// the app goes on with what it has. Calls exactly one of the callbacks.
        /// </summary>
        IEnumerator Reload(Action<ContentBundle> onLoaded, Action<string> onFailed);

        /// <summary>
        /// The app is ready with the content that <see cref="Load"/> (or <see cref="Reload"/>) gave it: the sounds are in as well. Only now is content from the folder next to the program kept as the
        /// last known good copy, so that a copy the app could not start with is never the one it falls back to.
        /// </summary>
        void ConfirmLoaded();
    }
}
