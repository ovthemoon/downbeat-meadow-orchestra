using System;
using System.Collections;
using System.Collections.Generic;
using Downbeat.Content;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Downbeat.App
{
    /// <summary>
    /// Plays a recorded hand file as if it were the camera. The recording starts on a bar line of the beat grid and runs on the audio
    /// clock, and each note is requested at the audio time its frame had in the recording, so the same file gives the same notes
    /// on the same slots every run. Optionally restarts (on a bar line) after a pause when it ends.
    /// </summary>
    public sealed class ReplayHandSource : IHandSource, IGridAwareInput, IPreparable
    {
        /// <summary>The default wait before the first play: just enough to land on the next bar line.</summary>
        public const double DefaultFirstStartDelaySec = 0.5;

        readonly IAudioEngine _audio;
        readonly string _file;
        readonly bool _loop;
        readonly int _maxPlays;
        readonly ReplayGapSchedule _gaps;
        readonly double _firstStartDelaySec;
        readonly HandSettingsHolder _handSettings;
        int _plays = 1;
        ReplayPlayer _player;
        BeatGrid _grid;
        int _slotsPerBar;
        bool _gridReady;
        double _startAudio;

        /// <param name="gaps">How long the recording rests after each play (<c>--replay-gaps</c>); null = 1 s every time.</param>
        /// <param name="maxPlays">With <paramref name="loop"/>: how many times the recording plays in all before it stops for good (0 = for ever). <c>--replay-loops N</c>.</param>
        /// <param name="firstStartDelaySec">How long the meadow stays idle before the first play (<c>--replay-delay</c>; recordings of an idle-then-playing run).</param>
        /// <param name="handSettings">Where the hand pose thresholds come from (the loaded content); null reads with the defaults.</param>
        public ReplayHandSource(IAudioEngine audio, string file, bool loop, int maxPlays = 0, ReplayGapSchedule gaps = null, double firstStartDelaySec = DefaultFirstStartDelaySec, HandSettingsHolder handSettings = null)
        {
            _handSettings = handSettings;
            _firstStartDelaySec = firstStartDelaySec >= 0.0 ? firstStartDelaySec : DefaultFirstStartDelaySec;
            _audio = audio;
            _file = file;
            _loop = loop;
            _maxPlays = maxPlays;
            _gaps = gaps ?? ReplayGapSchedule.Parse(null);
        }

        /// <summary>For tests and tools: a source whose recording is already loaded.</summary>
        public ReplayHandSource(IAudioEngine audio, ReplayPlayer player, string file, bool loop, int maxPlays = 0, ReplayGapSchedule gaps = null, double firstStartDelaySec = DefaultFirstStartDelaySec) : this(audio, file, loop, maxPlays, gaps, firstStartDelaySec)
        {
            _player = player;
        }

        public TrackerState State => _player == null ? TrackerState.Off : TrackerState.Running;
        public int Generation { get; private set; }
        public string FileName => _file;

        public IEnumerator Prepare(Action<string> onFailed)
        {
            if (!ReplayPath.IsAllowed(_file)) { onFailed($"replay file '{_file}' is not allowed (use a relative .json path inside the site)"); yield break; }
            var url = ContentLocator.BaseUrl() + _file;
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onFailed($"replay file: {request.error} ({url})"); yield break; }
                ReplayDto dto;
                try { dto = JsonUtility.FromJson<ReplayDto>(TextBom.Strip(request.downloadHandler.text)); }
                catch (ArgumentException e) { onFailed($"replay file is not valid JSON: {e.Message}"); yield break; }
                var problems = ReplayValidator.Validate(dto);
                if (problems.Count > 0) { onFailed("replay file rejected: " + string.Join("; ", problems)); yield break; }
                _player = new ReplayPlayer(dto, _handSettings);
            }
        }

        public void OnGridReady(in BeatGrid grid, int slotsPerBar)
        {
            _grid = grid;
            _slotsPerBar = slotsPerBar;
            _gridReady = true;
            _startAudio = grid.BarTimeAtOrAfter(_audio.Now + _firstStartDelaySec, slotsPerBar);
        }

        public void Poll(List<HandFrame> frames)
        {
            if (_player == null || !_gridReady) return;
            var elapsedMs = (_audio.Now - _startAudio) * 1000.0;
            if (_player.IsFinished)
            {
                var mayPlayAgain = _loop && (_maxPlays <= 0 || _plays < _maxPlays);
                if (mayPlayAgain && elapsedMs > _player.DurationMs + _gaps.GapAfterPlay(_plays) * 1000.0) Restart();
                return;
            }
            if (elapsedMs < 0) return;
            _player.Advance(elapsedMs, frames);
        }

        public double RequestTimeFor(double captureMs) => _startAudio + captureMs / 1000.0;

        void Restart()
        {
            _startAudio = _grid.BarTimeAtOrAfter(_audio.Now + DefaultFirstStartDelaySec, _slotsPerBar);
            _player.Rewind();
            _plays++;
            Generation++;
        }
    }
}
