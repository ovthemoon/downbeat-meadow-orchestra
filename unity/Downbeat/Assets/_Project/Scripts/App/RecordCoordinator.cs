using System;
using System.Collections.Generic;
using System.IO;
using Downbeat.Core;
using Downbeat.Record;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// Everything between the performance and the record screen: the notes and nature notes that are heard go onto the live score, the visit is followed from the first hand to the quiet
    /// afterwards, and when it ends a card is written (with the meadow at its climax as thumbnail) and shown among today's cards. A date change starts an empty day. A card that cannot be
    /// written is a warning, never a stop.
    /// </summary>
    public sealed class RecordCoordinator
    {

        readonly RecordDto _record;
        readonly Camera _meadowCamera;
        readonly ExperienceStateMachine _experience;
        readonly CardStore _store;
        readonly SessionRecorder _recorder;
        readonly Dictionary<string, Texture2D> _thumbnails = new Dictionary<string, Texture2D>();
        readonly List<RecordCardView> _shown = new List<RecordCardView>();
        byte[] _pendingThumbnail;
        DateTime _shownDay = DateTime.MinValue;
        double _nextDateCheck;

        public RecordCoordinator(ContentDto content, string cardsRoot, Camera meadowCamera, RecordScreenView view, ExperienceStateMachine experience)
        {
            _record = content.record;
            _meadowCamera = meadowCamera;
            View = view;
            _experience = experience;
            Score = new ScoreModel(_record.scoreCapacity);
            _store = new CardStore(new CardStoreIO(), new JsonCardCodec(), cardsRoot, message => Debug.LogWarning("[record] " + message));
            _recorder = new SessionRecorder(_record.minNotes, _record.cardMaxMarks);
            _recorder.ClimaxReached += TakeClimaxPicture;
            experience.SessionStarted += t => _recorder.OnSessionStarted(t, DateTime.Now);
            experience.SessionEnded += OnSessionEnded;
        }

        public ScoreModel Score { get; }
        public RecordScreenView View { get; }
        public CardStore Store => _store;
        public int CardsShown => _shown.Count;
        public int CardsWritten { get; private set; }

        /// <summary>Call once the view is configured: shows the cards of today that are already on disk.</summary>
        public void Start()
        {
            RefreshCards();
        }

        /// <summary>Says where to ask which song a visit was played to when its card is made (<see cref="SessionRecorder.SetSongSource"/>).</summary>
        public void SetSongSource(Func<SongLabel> songOfVisit) => _recorder.SetSongSource(songOfVisit);

        public void OnNote(in ScheduledNote note)
        {
            Score.Add(new ScoreMark(note.AudioTime, note.Part, note.PitchStep, note.Velocity));
            _recorder.OnNote(note);
        }

        /// <summary>The piece was ended: the notes that are heard after <paramref name="audioTime"/> are not on the score (their sound was cancelled).</summary>
        public void CancelNotesAfter(double audioTime) => Score.RemoveMarksAfter(audioTime);

        /// <param name="level">The energy level: layer 0 (fireflies) is in from level 1, the sky from 2, the hills at 3.</param>
        public void Tick(double heardNow, double exactNow, int level, IReadOnlyList<NatureEvent> newNatureEvents)
        {
            for (var i = 0; i < newNatureEvents.Count; i++) Score.AddNature(newNatureEvents[i].Note.Layer, newNatureEvents[i].AudioTime, newNatureEvents[i].Note.Gain);
            for (var layer = 0; layer < NatureLayers.Count; layer++) Score.SetLayerActive((NatureLayer)layer, level > layer, heardNow);
            _recorder.OnLevel(level, exactNow);
            if (Time.realtimeSinceStartupAsDouble >= _nextDateCheck)
            {
                _nextDateCheck = Time.realtimeSinceStartupAsDouble + _record.dateCheckSec;
                if (DateTime.Today != _shownDay) RefreshCards();
            }
            View.Performing = _experience.State != ExperienceState.Idle;   // a visit is on: the idle motes of the screen step back
            View.Tick(heardNow);
        }

        void TakeClimaxPicture()
        {
            _pendingThumbnail = ClimaxSnapshot.Capture(_meadowCamera, View, _record.thumbnailWidth, _record.thumbnailHeight);
        }

        void OnSessionEnded(double now)
        {
            var card = _recorder.OnSessionEnded(now);
            var thumbnail = _pendingThumbnail;
            _pendingThumbnail = null;
            if (card == null) return;
            try
            {
                _store.Save(card, thumbnail);
                CardsWritten++;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[record] the card could not be written: " + e.Message);
                return;
            }
            RefreshCards();
        }

        /// <summary>Reads today's cards again and shows the newest ones.</summary>
        void RefreshCards()
        {
            _shownDay = DateTime.Today;
            IReadOnlyList<SessionCard> today;
            try { today = _store.LoadDay(_shownDay); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[record] today's cards could not be read: " + e.Message);
                today = new List<SessionCard>();
            }

            var keep = new HashSet<string>();
            _shown.Clear();
            for (var i = today.Count - 1; i >= 0 && _shown.Count < _record.cardsShown; i--)
            {
                var card = today[i];
                keep.Add(card.Id);
                _shown.Add(new RecordCardView(card, ThumbnailOf(card)));
            }
            foreach (var id in new List<string>(_thumbnails.Keys))
            {
                if (keep.Contains(id)) continue;
                if (_thumbnails[id] != null) Disposal.Release(_thumbnails[id]);
                _thumbnails.Remove(id);
            }
            View.SetCards(_shown);
        }

        Texture2D ThumbnailOf(SessionCard card)
        {
            if (_thumbnails.TryGetValue(card.Id, out var cached)) return cached;
            Texture2D texture = null;
            var path = _store.ThumbnailPath(card);
            if (path != null && File.Exists(path))
            {
                try
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(File.ReadAllBytes(path))) { Disposal.Release(texture); texture = null; }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[record] thumbnail {path}: {e.Message}");
                    if (texture != null) Disposal.Release(texture);
                    texture = null;
                }
            }
            _thumbnails[card.Id] = texture;
            return texture;
        }
    }
}
