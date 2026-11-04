using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Content;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// Reads the content again while the app runs (the F5 key). The external content is validated first; if it is broken the picture stays exactly as it is and the log says why. If it is good and
    /// the <c>meadow</c> section differs, the meadow is built again from it at the next bar line (the sound engine, the song grid, the hand input, the nature score and the screens were set up
    /// when the app started, so a change in those waits for the next start: the log lists them).
    /// </summary>
    public sealed class ContentReload
    {
        readonly IContentRepository _content;
        readonly CoroutineRunner _runner;
        readonly MeadowStage _meadow;
        readonly BeatGrid _grid;
        readonly int _slotsPerBar;
        readonly System.Func<double> _heardNow;
        ContentBundle _current;
        ContentBundle _pending;
        double _applyAtHeard;
        bool _loading;

        public ContentReload(IContentRepository content, CoroutineRunner runner, MeadowStage meadow, ContentBundle current, BeatGrid grid, int slotsPerBar, System.Func<double> heardNow)
        {
            _content = content;
            _runner = runner;
            _meadow = meadow;
            _current = current;
            _grid = grid;
            _slotsPerBar = slotsPerBar;
            _heardNow = heardNow;
        }

        /// <summary>The content the meadow was built from last (after a reload, the reloaded one).</summary>
        public ContentBundle Current => _current;
        /// <summary>A reload that was read and accepted and waits for its bar line.</summary>
        public bool HasPending => _pending != null;
        public int Reloads { get; private set; }
        public int Refusals { get; private set; }

        /// <summary>F5: read the external content again. Ignored while an earlier reload is still being read.</summary>
        public void Request()
        {
            if (_loading) return;
            _loading = true;
            Debug.Log("[content] reload requested (F5)");
            _runner.StartCoroutine(Read());
        }

        /// <summary>Once per frame, with the audio time of what is being heard: applies a waiting reload when its bar line is reached.</summary>
        public void Tick(double heardNow)
        {
            if (_pending == null || heardNow < _applyAtHeard) return;
            var bundle = _pending;
            _pending = null;
            Apply(bundle);
        }

        /// <summary>Takes an accepted bundle: from the next bar line on, the meadow is the one it describes.</summary>
        public void Queue(ContentBundle bundle, double applyAtHeard)
        {
            if (_pending != null && !ReferenceEquals(_pending, bundle)) _pending.Release();   // an older reload that was never applied
            _pending = bundle;
            _applyAtHeard = applyAtHeard;
        }

        IEnumerator Read()
        {
            ContentBundle bundle = null;
            string failure = null;
            try
            {
                // The reading is stepped by hand: Unity drops a coroutine whose nested coroutine throws without running the finally of the outer one, and a reload that threw
                // (a disk that went away, a bug) must end as a refusal like any other, not as a flag that is never given back.
                var reading = _content.Reload(b => bundle = b, e => failure = e);
                while (true)
                {
                    bool more;
                    try { more = reading.MoveNext(); }
                    catch (System.Exception e) { failure = $"reading the content threw {e.GetType().Name}: {e.Message}"; break; }
                    if (!more) break;
                    yield return reading.Current;
                }
            }
            finally
            {
                _loading = false;   // also when the coroutine is stopped: the next F5 must not be ignored for ever
            }
            if (bundle == null)
            {
                Refusals++;
                Debug.LogWarning($"[content] reload refused, the picture stays as it is: {failure}");
                yield break;
            }

            var changed = ContentSections.Changed(_current.Dto, bundle.Dto);
            var restart = ContentSections.NeedingRestart(_current.Dto, bundle.Dto);
            if (restart.Count > 0) Debug.LogWarning($"[content] changed, and applied at the next start of the program: {string.Join(", ", restart)}");
            if (!changed.Contains(ContentSections.Meadow))
            {
                bundle.Release();
                Debug.Log("[content] reload: the meadow section is the same, nothing to apply");
                yield break;
            }
            var at = _grid.BarTimeAtOrAfter(_heardNow(), _slotsPerBar);
            Queue(bundle, at);
            Debug.Log($"[content] reload accepted: the meadow is built again at the next bar line ({at - _heardNow():0.0} s from now)");
        }

        void Apply(ContentBundle bundle)
        {
            var old = _current;
            _meadow.Configure(bundle, _meadow.QualityName);
            _current = bundle;
            old.Release();
            _content.ConfirmLoaded();   // it is in use now: it may become the last known good copy
            Reloads++;
            Debug.Log("[content] reloaded: the meadow was built again from the new content");
        }
    }
}
