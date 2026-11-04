using System;
using System.Collections;
using System.Collections.Generic;

namespace Downbeat.App
{
    /// <summary>
    /// Runs a coroutine so that an exception in it, or in any routine it waits for, is handed to a callback instead of stopping the coroutine without a word (Unity ends a coroutine that
    /// throws and does not tell the one that started it; an exception in a nested routine does not even run the outer routine's <c>catch</c> or <c>finally</c>). The routines are stepped by hand:
    /// a nested <see cref="IEnumerator"/> is run to its end before the parent goes on, which is what Unity does with it too, and everything else that is yielded (a frame, a wait, an
    /// <c>AsyncOperation</c>) is passed on to the engine.
    /// </summary>
    public static class GuardedRoutine
    {
        public static IEnumerator Run(IEnumerator routine, Action<Exception> onThrow)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try
                {
                    more = top.MoveNext();
                }
                catch (Exception error)
                {
                    onThrow(error);
                    yield break;
                }

                if (!more)
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return top.Current;
            }
        }
    }
}
