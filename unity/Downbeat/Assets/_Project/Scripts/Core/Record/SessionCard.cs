using System;
using System.Collections.Generic;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>
    /// What is kept of one visit: when it was, how long it was played, how many notes, how many nature layers joined at most, a score line of at most 64 notes and, when the visit
    /// was played to a song, the id and title of that song. Nothing that identifies a person: no camera picture, no hand shape, no name. The thumbnail (the meadow at its climax,
    /// drawn by the app) is a file next to the card.
    /// </summary>
    public sealed class SessionCard
    {
        public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss";
        public const int MaxStoredMarks = 256;
        /// <summary>A song title longer than this is cut: it is shown on the card, and a card file is not trusted.</summary>
        public const int MaxSongTitleLength = 60;

        readonly ScoreMark[] _scoreLine;

        public SessionCard(string id, DateTime startedLocal, double durationSec, int noteCount, int maxLevel, IReadOnlyList<ScoreMark> scoreLine, string thumbnailFile, string songId = null, string songTitle = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("a card needs an id", nameof(id));
            Id = id;
            StartedLocal = startedLocal;
            DurationSec = Math.Max(0.0, durationSec);
            NoteCount = Math.Max(0, noteCount);
            MaxLevel = Math.Max(0, Math.Min(NatureLayers.Count, maxLevel));
            _scoreLine = new ScoreMark[scoreLine?.Count ?? 0];
            for (var i = 0; i < _scoreLine.Length; i++) _scoreLine[i] = scoreLine[i];
            ThumbnailFile = thumbnailFile ?? string.Empty;
            SongId = string.IsNullOrWhiteSpace(songId) ? null : songId;
            SongTitle = string.IsNullOrWhiteSpace(songTitle) ? null : CutTitle(songTitle);
        }

        public string Id { get; }
        public DateTime StartedLocal { get; }
        public double DurationSec { get; }
        public int NoteCount { get; }
        /// <summary>How many nature layers had joined at the climax (0..3).</summary>
        public int MaxLevel { get; }
        /// <summary>The notes, with their times counted from the start of the visit.</summary>
        public IReadOnlyList<ScoreMark> ScoreLine => _scoreLine;
        public string ThumbnailFile { get; }
        /// <summary>The id of the song the visit was played to; null for free play and for cards written before songs existed.</summary>
        public string SongId { get; }
        /// <summary>The title of that song (at most <see cref="MaxSongTitleLength"/> characters); null when there was none.</summary>
        public string SongTitle { get; }

        public SessionCard WithIdAndThumbnail(string id, string thumbnailFile) => new SessionCard(id, StartedLocal, DurationSec, NoteCount, MaxLevel, _scoreLine, thumbnailFile, SongId, SongTitle);

        /// <summary>The title cut to <see cref="MaxSongTitleLength"/> characters, never between the two halves of a surrogate pair.</summary>
        static string CutTitle(string title)
        {
            if (title.Length <= MaxSongTitleLength) return title;
            var length = char.IsHighSurrogate(title[MaxSongTitleLength - 1]) ? MaxSongTitleLength - 1 : MaxSongTitleLength;
            return title.Substring(0, length);
        }

        public SessionCardDto ToDto()
        {
            var marks = new ScoreMarkDto[_scoreLine.Length];
            for (var i = 0; i < marks.Length; i++) marks[i] = new ScoreMarkDto { t = (float)_scoreLine[i].Time, part = Parts.Name(_scoreLine[i].Part), step = _scoreLine[i].PitchStep, v = _scoreLine[i].Velocity };
            return new SessionCardDto
            {
                version = SessionCardDto.CurrentVersion, id = Id, startedLocal = StartedLocal.ToString(TimeFormat, CultureInfo.InvariantCulture),
                durationSec = (float)DurationSec, noteCount = NoteCount, maxLevel = MaxLevel, scoreLine = marks, thumbnailFile = ThumbnailFile,
                songId = SongId, songTitle = SongTitle,
            };
        }

        /// <summary>Reads a card back from what was stored; false (and a reason) for anything that does not look like a card this app wrote: file content is not trusted.</summary>
        public static bool TryFromDto(SessionCardDto dto, out SessionCard card, out string problem)
        {
            card = null;
            if (dto == null) { problem = "empty"; return false; }
            if (dto.version != SessionCardDto.CurrentVersion) { problem = $"version {dto.version}"; return false; }
            if (string.IsNullOrWhiteSpace(dto.id) || dto.id.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || dto.id.Contains("..")) { problem = "bad id"; return false; }
            if (!DateTime.TryParseExact(dto.startedLocal, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)) { problem = "bad time"; return false; }
            if (!(dto.durationSec >= 0f) || dto.durationSec > 86400f || dto.noteCount < 0 || dto.maxLevel < 0 || dto.maxLevel > NatureLayers.Count) { problem = "number out of range"; return false; }
            if (!string.IsNullOrEmpty(dto.thumbnailFile) && (dto.thumbnailFile.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || dto.thumbnailFile.Contains(".."))) { problem = "bad thumbnail name"; return false; }
            var line = dto.scoreLine ?? Array.Empty<ScoreMarkDto>();
            if (line.Length > MaxStoredMarks) { problem = "too many marks"; return false; }
            var marks = new List<ScoreMark>(line.Length);
            foreach (var m in line)
            {
                if (m == null || !Parts.TryParse(m.part, out var part) || m.step < 0 || m.step > 63 || !(m.t >= 0f)) { problem = "bad mark"; return false; }
                marks.Add(new ScoreMark(m.t, part, m.step, m.v));
            }
            card = new SessionCard(dto.id, started, dto.durationSec, dto.noteCount, dto.maxLevel, marks, dto.thumbnailFile, dto.songId, dto.songTitle);
            problem = null;
            return true;
        }
    }

    [Serializable]
    public sealed class ScoreMarkDto
    {
        public float t;
        public string part;
        public int step;
        public float v;
    }

    /// <summary>
    /// The stored form of a <see cref="SessionCard"/> (JSON). Numbers, the local time of the visit and the id and title of its song only. The two song fields are new but the version stays 1:
    /// a card written before them has no such keys and reads with no song, and <c>JsonUtility</c> writes a missing song as empty text, which also reads as no song.
    /// </summary>
    [Serializable]
    public sealed class SessionCardDto
    {
        public const int CurrentVersion = 1;

        public int version;
        public string id;
        public string startedLocal;
        public float durationSec;
        public int noteCount;
        public int maxLevel;
        public ScoreMarkDto[] scoreLine;
        public string thumbnailFile;
        public string songId;
        public string songTitle;
    }

    /// <summary>
    /// Follows one visit from the moment a hand came (<see cref="OnSessionStarted"/>) to the moment the meadow was quiet again (<see cref="OnSessionEnded"/>), and turns it into a card.
    /// A visit of fewer than <c>minNotes</c> notes (somebody walking past) makes no card. <see cref="ClimaxReached"/> fires whenever the energy level of the visit reaches a new
    /// highest level: the app takes a picture of the meadow then, and the last one it took is the climax.
    /// </summary>
    public sealed class SessionRecorder
    {
        /// <summary>A visit keeps at most this many notes for its score line; a longer one still counts all its notes.</summary>
        public const int MaxStoredNotes = 10000;

        readonly int _minNotes;
        readonly int _maxMarks;
        readonly List<ScoreMark> _marks = new List<ScoreMark>(256);
        double _startNow;
        DateTime _startLocal;
        int _maxLevel;
        Func<SongLabel> _songOfVisit;

        public SessionRecorder(int minNotes, int maxMarks)
        {
            if (minNotes < 1) throw new ArgumentOutOfRangeException(nameof(minNotes), minNotes, "must be at least 1");
            if (maxMarks < 1 || maxMarks > SessionCard.MaxStoredMarks) throw new ArgumentOutOfRangeException(nameof(maxMarks), maxMarks, $"1..{SessionCard.MaxStoredMarks}");
            _minNotes = minNotes;
            _maxMarks = maxMarks;
        }

        /// <summary>
        /// Says where to ask which song the visit was played to: <paramref name="songOfVisit"/> is read once, when the visit ends and has enough notes for a card, and what it gives goes on the card
        /// (<c>default</c> is no song). It stays set from one visit to the next (<see cref="OnSessionStarted"/> does not reset it); with none set, or <c>null</c>, every visit is free play.
        /// </summary>
        public void SetSongSource(Func<SongLabel> songOfVisit) => _songOfVisit = songOfVisit;

        public bool IsRecording { get; private set; }
        /// <summary>All the notes of the visit so far (the stored marks are fewer after <see cref="MaxStoredNotes"/>).</summary>
        public int NoteCount { get; private set; }
        public int MaxLevel => _maxLevel;
        /// <summary>How many marks are kept for the score line (never more than <see cref="MaxStoredNotes"/>).</summary>
        public int StoredMarkCount => _marks.Count;
        public event Action ClimaxReached;

        public void OnSessionStarted(double now, DateTime localNow)
        {
            _marks.Clear();
            NoteCount = 0;
            _startNow = now;
            _startLocal = localNow;
            _maxLevel = 0;
            IsRecording = true;
        }

        public void OnNote(in ScheduledNote note)
        {
            if (!IsRecording) return;
            NoteCount++;
            if (_marks.Count < MaxStoredNotes) _marks.Add(new ScoreMark(Math.Max(0.0, note.AudioTime - _startNow), note.Part, note.PitchStep, note.Velocity));
        }

        public void OnLevel(int level, double now)
        {
            if (!IsRecording || level <= _maxLevel) return;
            _maxLevel = Math.Min(level, NatureLayers.Count);
            ClimaxReached?.Invoke();
        }

        /// <returns>The card of the visit, or null when it had too few notes.</returns>
        public SessionCard OnSessionEnded(double now)
        {
            if (!IsRecording) return null;
            IsRecording = false;
            if (NoteCount < _minNotes) return null;

            var duration = 0.0;
            foreach (var m in _marks) duration = Math.Max(duration, m.Time);
            var song = _songOfVisit?.Invoke() ?? default;
            return new SessionCard(_startLocal.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), _startLocal, duration, NoteCount, _maxLevel, Reduce(_marks, _maxMarks), string.Empty, song.Id, song.Title);
        }

        /// <summary>At most <paramref name="max"/> marks, evenly spread over the visit, the first and the last kept.</summary>
        static List<ScoreMark> Reduce(List<ScoreMark> marks, int max)
        {
            if (marks.Count <= max) return new List<ScoreMark>(marks);
            var reduced = new List<ScoreMark>(max);
            for (var i = 0; i < max; i++) reduced.Add(marks[(int)Math.Round(i * (marks.Count - 1) / (double)(max - 1))]);
            return reduced;
        }
    }

    public interface ICardFileSystem
    {
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        bool FileExists(string path);
        void WriteAllText(string path, string text);
        void WriteAllBytes(string path, byte[] bytes);
        string ReadAllText(string path);
        IReadOnlyList<string> GetFiles(string directory, string searchPattern);
    }

    public interface ICardCodec
    {
        string Encode(SessionCard card);
        bool TryDecode(string json, out SessionCard card, out string problem);
    }

    /// <summary>
    /// The cards on disk: <c>root/yyyy-MM-dd/ID.json</c> and <c>ID.png</c>. The day is the local date the visit started, so a new date starts an empty day. A card file that cannot be read
    /// (broken JSON, a value that makes no sense) is skipped with a warning and never stops the others.
    /// </summary>
    public sealed class CardStore
    {
        readonly ICardFileSystem _fs;
        readonly ICardCodec _codec;
        readonly string _root;
        readonly Action<string> _warn;

        public CardStore(ICardFileSystem fs, ICardCodec codec, string rootDir, Action<string> warn)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            if (string.IsNullOrWhiteSpace(rootDir)) throw new ArgumentException("a folder is needed", nameof(rootDir));
            _root = rootDir;
            _warn = warn ?? (_ => { });
        }

        public string DayFolder(DateTime localDate) => System.IO.Path.Combine(_root, localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        public string ThumbnailPath(SessionCard card) =>
            string.IsNullOrEmpty(card.ThumbnailFile) ? null : System.IO.Path.Combine(DayFolder(card.StartedLocal), card.ThumbnailFile);

        /// <summary>Writes the card (and its thumbnail when there is one) into the folder of its day. Returns the card as stored: a taken id gets a number added.</summary>
        public SessionCard Save(SessionCard card, byte[] thumbnailPng)
        {
            var folder = DayFolder(card.StartedLocal);
            _fs.CreateDirectory(folder);
            var id = card.Id;
            for (var n = 2; _fs.FileExists(System.IO.Path.Combine(folder, id + ".json")); n++) id = card.Id + "-" + n.ToString(CultureInfo.InvariantCulture);

            var thumbnail = thumbnailPng != null && thumbnailPng.Length > 0 ? id + ".png" : string.Empty;
            var stored = card.WithIdAndThumbnail(id, thumbnail);
            if (thumbnail.Length > 0) _fs.WriteAllBytes(System.IO.Path.Combine(folder, thumbnail), thumbnailPng);
            _fs.WriteAllText(System.IO.Path.Combine(folder, id + ".json"), _codec.Encode(stored));
            return stored;
        }

        /// <summary>The cards of a day, the oldest first.</summary>
        public IReadOnlyList<SessionCard> LoadDay(DateTime localDate)
        {
            var folder = DayFolder(localDate);
            var cards = new List<SessionCard>();
            if (!_fs.DirectoryExists(folder)) return cards;
            foreach (var file in _fs.GetFiles(folder, "*.json"))
            {
                string text;
                try { text = _fs.ReadAllText(file); }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    _warn($"card {file}: cannot be read ({e.Message}), skipped");
                    continue;
                }
                if (_codec.TryDecode(text, out var card, out var problem)) cards.Add(card);
                else _warn($"card {file}: {problem}, skipped");
            }
            cards.Sort((a, b) => a.StartedLocal.CompareTo(b.StartedLocal));
            return cards;
        }
    }
}
