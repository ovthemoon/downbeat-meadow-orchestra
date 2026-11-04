using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Record
{
    public sealed class RecordCardView
    {
        public RecordCardView(SessionCard card, Texture2D thumbnail)
        {
            Card = card;
            Thumbnail = thumbnail;
        }

        public SessionCard Card { get; }
        /// <summary>The meadow at the climax of the visit; null when there is none (the card shows a quiet panel instead).</summary>
        public Texture2D Thumbnail { get; }
        /// <summary>The title of the song the visit was played to, drawn under <see cref="DetailLabel"/>; empty for free play and for a card from before songs.</summary>
        public string SongLabel => Card.SongTitle ?? string.Empty;

        string _timeLabel, _timeFormat, _detailLabel, _durationFormat, _natureFormat;
        string[] _meridiem;

        /// <summary>The time of day the card was made ("오후 3:42"): formatted once and kept, because it never changes (only a different format in the content makes a new one).</summary>
        public string TimeLabel(RecordTextDto text)
        {
            if (_timeLabel == null || !ReferenceEquals(_timeFormat, text.timeFormat) || !ReferenceEquals(_meridiem, text.meridiem))
            {
                _timeLabel = CardText.TimeOfDay(Card.StartedLocal, text.timeFormat, text.meridiem);
                _timeFormat = text.timeFormat;
                _meridiem = text.meridiem;
            }
            return _timeLabel;
        }

        /// <summary>How long it was played and how many nature layers joined ("1분 12초  자연 3"): formatted once and kept.</summary>
        public string DetailLabel(RecordTextDto text)
        {
            if (_detailLabel == null || !ReferenceEquals(_durationFormat, text.durationFormat) || !ReferenceEquals(_natureFormat, text.natureFormat))
            {
                _detailLabel = CardText.Duration(Card.DurationSec, text.durationFormat) + "  " + CardText.Nature(Card.MaxLevel, text.natureFormat);
                _durationFormat = text.durationFormat;
                _natureFormat = text.natureFormat;
            }
            return _detailLabel;
        }
    }

    /// <summary>All colours of the record screen, linear (see <see cref="RecordMeshBuilder"/>).</summary>
    public sealed class RecordPalette
    {
        public Color Background;
        public Color BackgroundBottom;
        public Color Ink;
        public Color Muted;
        public Color Staff;
        public Color Accent;
        public Color Card;
        /// <summary>
        /// The colour of each of the six parts (in the order of <see cref="Part"/>): the glow behind the flower of a part of the lower row (the flower of its column), and the colour of the small light dot
        /// that a note of the upper row (the sky) is drawn as.
        /// </summary>
        public Color[] FlowerGlow = new Color[Parts.Count];
        /// <summary>Line colour of the three nature layers (fireflies, sky, hills).</summary>
        public Color[] NatureLine = new Color[NatureLayers.Count];
    }

    public enum TextAnchorKind { MiddleLeft, MiddleCenter, MiddleRight }

    public readonly struct TextCommand
    {
        public TextCommand(string slot, string text, float x, float y, float height, Color color, TextAnchorKind anchor)
        {
            Slot = slot;
            Text = text;
            X = x;
            Y = y;
            Height = height;
            Color = color;
            Anchor = anchor;
        }

        public string Slot { get; }
        public string Text { get; }
        public float X { get; }
        public float Y { get; }
        public float Height { get; }
        public Color Color { get; }
        public TextAnchorKind Anchor { get; }
    }

    public readonly struct ThumbnailCommand
    {
        public ThumbnailCommand(int slot, LayoutRect rect, Texture2D texture)
        {
            Slot = slot;
            Rect = rect;
            Texture = texture;
        }

        public int Slot { get; }
        public LayoutRect Rect { get; }
        public Texture2D Texture { get; }
    }

    /// <summary>The quads of the record screen, one builder per texture/draw order.</summary>
    public sealed class RecordBuilders
    {
        public readonly RecordMeshBuilder Solid = new RecordMeshBuilder();    // white: the backdrop gradient (and the strip's accent line)
        public readonly RecordMeshBuilder Lines = new RecordMeshBuilder();    // white: the staff lines, the line of now and the lines of the nature layers: over the motes, under the translucent panels
        public readonly RecordMeshBuilder Round = new RecordMeshBuilder();    // rounded corners: panels and cards
        public readonly RecordMeshBuilder Overlay = new RecordMeshBuilder();  // white, drawn over the thumbnails
        public readonly RecordMeshBuilder Glow = new RecordMeshBuilder();     // soft disc: glows and glints
        public readonly RecordMeshBuilder Flowers = new RecordMeshBuilder();  // the flower icons
        public readonly RecordMeshBuilder Motes = new RecordMeshBuilder();    // the idle fireflies and petals (MoteAtlas), behind the panels

        public void Clear()
        {
            Solid.Clear();
            Lines.Clear();
            Round.Clear();
            Overlay.Clear();
            Glow.Clear();
            Flowers.Clear();
            Motes.Clear();
        }
    }

    /// <summary>
    /// Decides what the record screen looks like at one moment: it fills the mesh builders and the lists of texts and thumbnails from the score, the cards and the layout. It draws nothing
    /// itself. The right edge of the score is now; flowers pop in, drift left and fade; a nature line lights up softly when its layer joins.
    /// </summary>
    public sealed class RecordPainter
    {
        const float PopSec = 0.28f;
        const float FadeFrom = 0.55f;
        /// <summary>The ground row of the six cells: its three columns are the three flowers.</summary>
        const int GroundRow = 1;
        /// <summary>A light dot (a note of the sky) is this share of the size of a flower: small, so that the flowers stay the main thing on the score.</summary>
        const float LightDotShare = 0.32f;
        const float JoinSec = 1.4f;
        const float PanelRadius = 26f;
        const float CardRadius = 20f;
        const float ThumbPad = 10f;
        const float ThumbAspect = 9f / 16f;
        const float LabelWidthShare = 0.14f;
        const uint MoteSeed = 7u;                  // the motes are the same on every start (a seed, not a random draw)
        const float TimeLabelHeightShare = 0.19f;  // of the picture of the card
        const float TimeLabelMaxHeight = 26f;
        const float TimeShadeShare = 0.3f;         // the dark fade at the top of the picture that the time is written on
        const float SongLineShare = 0.4f;          // of the room under the picture: each of the two lines of a card that names its song (the detail line above, the song under it)
        const float SongLineMaxHeight = 32f;
        const float SongLineWidthShare = 0.1f;     // of the width of the card, as a most for the height of a line
        const float DetailLineCentre = 0.72f;      // where, in the room under the picture, the detail line sits when the song line is under it ...
        const float SongLineCentre = 0.27f;        // ... and where the song line sits

        const int MaxSlots = 12;   // cards shown: at most 12 (content record.cardsShown)
        // the names of the text slots, made once: building "card" + index every frame makes garbage
        static readonly string[] CardSlots = SlotNames("card", MaxSlots);
        static readonly string[] TimeSlots = SlotNames("time", MaxSlots);
        static readonly string[] SongSlots = SlotNames("song", MaxSlots);
        static readonly string[] LayerSlots = SlotNames("layer", NatureLayers.Count);

        static readonly Vector2 White = new Vector2(0.5f, 0.5f);
        static readonly Vector2 DiscMin = Vector2.zero;
        static readonly Vector2 DiscMax = Vector2.one;

        readonly RecordDto _record;
        readonly RecordPalette _palette;
        readonly int _pitchSteps;
        readonly List<ScoreMark> _visible = new List<ScoreMark>(128);
        readonly List<NatureMark> _glints = new List<NatureMark>(128);
        readonly IdleMoteField _motes;
        DateTime _dateDay = DateTime.MinValue;
        string _dateFormat;
        string _dateText;
        readonly MoteState[] _moteStates;

        /// <summary>How many idle motes the screen has (content <c>record.idleMotes</c>).</summary>
        public int MoteCount => _motes.Count;
        /// <summary>1 while the meadow is idle, the performing level while a visit is on.</summary>
        public float MoteLevel => _motes.Level;
        /// <summary>The mote as it was drawn last (screen heights, see <see cref="MoteState"/>).</summary>
        public MoteState MoteAt(int index) => _moteStates[index];

        public RecordPainter(RecordDto record, RecordPalette palette, int pitchSteps)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            _palette = palette ?? throw new ArgumentNullException(nameof(palette));
            _pitchSteps = pitchSteps;
            _motes = new IdleMoteField(record.idleMotes, MoteSeed, record.idleMoteSpeed, record.idleMoteAlpha, record.idleMoteDimmed);
            _moteStates = new MoteState[_motes.Count];
        }

        /// <param name="performing">A visit is on (hands in): the idle motes step back.</param>
        public void Paint(RecordLayout layout, double now, double clockSeconds, ScoreModel score, IReadOnlyList<RecordCardView> cards, DateTime localToday, float panelWidth, float panelHeight,
            RecordBuilders b, List<TextCommand> texts, List<ThumbnailCommand> thumbnails, bool performing = false)
        {
            b.Clear();
            texts.Clear();
            thumbnails.Clear();
            var strip = layout.Mode == DisplayMode.SingleOverlay;
            PaintBackground(b, panelWidth, panelHeight, strip);
            if (!strip) PaintMotes(clockSeconds, performing, panelWidth, panelHeight, b);
            if (!strip) PaintTitle(layout, localToday, b, texts);
            PaintScore(layout, now, clockSeconds, score, b, texts);
            PaintNature(layout, now, score, b, texts);
            PaintCards(layout, cards, b, texts, thumbnails);
        }

        static string[] SlotNames(string prefix, int count)
        {
            var names = new string[count];
            for (var i = 0; i < count; i++) names[i] = prefix + i;
            return names;
        }

        /// <summary>"10월 30일": formatted again only when the day or the format changes.</summary>
        string DateText(DateTime today)
        {
            if (_dateText == null || today.Date != _dateDay || !ReferenceEquals(_dateFormat, _record.text.dateFormat))
            {
                _dateText = CardText.Date(today, _record.text.dateFormat);
                _dateDay = today.Date;
                _dateFormat = _record.text.dateFormat;
            }
            return _dateText;
        }

        void PaintBackground(RecordBuilders b, float w, float h, bool strip)
        {
            var top = _palette.Background;
            var bottom = _palette.BackgroundBottom;
            if (strip) { top.a = 1f; bottom.a = 1f; } // nearly opaque: the sparkles of the meadow behind it would bloom through a see-through panel
            b.Solid.Quad(0f, 0f, w, h, bottom, bottom, top, top, White, White);
            if (!strip) return;
            var accent = _palette.Accent;
            accent.a = 0.55f;
            b.Solid.Solid(0f, h - 3f, w, 3f, accent);
        }

        /// <summary>The idle fireflies and petals, all in one builder drawn under the panels and the text. Only on a screen of its own: the strip hangs over a meadow that moves already.</summary>
        void PaintMotes(double clockSeconds, bool performing, float w, float h, RecordBuilders b)
        {
            _motes.Advance(clockSeconds, performing);
            if (_motes.Count == 0) return;
            _motes.Evaluate(clockSeconds, w / h, _moteStates);
            MoteAtlas.GlowUv(out var glowMin, out var glowMax);
            MoteAtlas.PetalUv(out var petalMin, out var petalMax);
            foreach (var mote in _moteStates)
            {
                var size = mote.Size * h;
                if (mote.Kind == MoteKind.Firefly)
                {
                    var glow = _palette.NatureLine[(int)NatureLayer.Fireflies];
                    glow.a = mote.Alpha;
                    b.Motes.Centered(mote.X * h, mote.Y * h, size, glow, glowMin, glowMax);
                    continue;
                }
                var petal = _palette.FlowerGlow[(int)Parts.At(GroundRow, Mathf.Clamp(mote.Variant, 0, Parts.Columns - 1))];   // a petal is one of the three flowers, which are the columns of the ground row
                petal.a = mote.Alpha;
                b.Motes.Rotated(mote.X * h, mote.Y * h, size, size, mote.Angle, petal, petalMin, petalMax);
            }
        }

        void PaintTitle(RecordLayout layout, DateTime today, RecordBuilders b, List<TextCommand> texts)
        {
            var t = layout.Title;
            texts.Add(new TextCommand("title", _record.text.title, t.X, t.Y + t.Height * 0.5f, t.Height * 0.8f, _palette.Ink, TextAnchorKind.MiddleLeft));
            texts.Add(new TextCommand("date", DateText(today), t.Right, t.Y + t.Height * 0.5f, t.Height * 0.42f, _palette.Muted, TextAnchorKind.MiddleRight));
            var icon = t.Height * 0.62f;
            var x = t.Right - t.Height * 3.6f - icon * 3f;
            for (var i = 0; i < FlowerIconPainter.IconCount; i++)
            {
                FlowerIconPainter.Uv(i, out var uvMin, out var uvMax);
                b.Flowers.Centered(x + i * (icon * 1.15f) + icon * 0.5f, t.Y + t.Height * 0.5f, icon, new Color(1f, 1f, 1f, 0.9f), uvMin, uvMax);
            }
        }

        void PaintScore(RecordLayout layout, double now, double clockSeconds, ScoreModel score, RecordBuilders b, List<TextCommand> texts)
        {
            var r = layout.Score;
            var pad = Mathf.Min(r.Height * 0.07f, 26f);
            var panel = _palette.Card;
            panel.a = 0.42f;
            b.Round.RoundedRect(r.X, r.Y, r.Width, r.Height, PanelRadius * Mathf.Min(1f, r.Height / 380f), panel);
            var px = r.X + pad;
            var py = r.Y + pad;
            var pw = r.Width - 2f * pad;
            var ph = r.Height - 2f * pad;

            var staffFade = new Color(_palette.Staff.r, _palette.Staff.g, _palette.Staff.b, 0f);
            var staff = _palette.Staff;
            staff.a = 1f;
            var thickness = Mathf.Max(2f, ph * 0.008f);
            for (var line = 0; line < 5; line++)
            {
                var y = py + ScoreLayout.StaffLineY(line, ph, _pitchSteps);
                b.Lines.Quad(px, y - thickness * 0.5f, pw * 0.16f, thickness, staffFade, staff, staff, staffFade, White, White);
                b.Lines.Solid(px + pw * 0.16f, y - thickness * 0.5f, pw * 0.84f, thickness, staff);
            }

            // the edge of "now": a thin line that fades out at both ends and a soft glow behind it
            var accent = _palette.Accent;
            var lineCore = new Color(accent.r, accent.g, accent.b, 0.55f);
            var lineEnd = new Color(accent.r, accent.g, accent.b, 0f);
            var lineWidth = Mathf.Max(2f, ph * 0.007f);
            b.Lines.Quad(px + pw - lineWidth, py, lineWidth, ph * 0.5f, lineEnd, lineEnd, lineCore, lineCore, White, White);
            b.Lines.Quad(px + pw - lineWidth, py + ph * 0.5f, lineWidth, ph * 0.5f, lineCore, lineCore, lineEnd, lineEnd, White, White);
            b.Glow.Centered(px + pw, py + ph * 0.5f, ph * 1.1f, new Color(accent.r, accent.g, accent.b, 0.14f), DiscMin, DiscMax);

            _visible.Clear();
            score.CopyVisible(now, _record.windowSec, _visible);
            foreach (var mark in _visible) PaintFlower(mark, now, px, py, pw, ph, b);
            if (_visible.Count == 0 && score.ActiveLayerCount == 0)
            {
                var breathe = 0.55f + 0.2f * Mathf.Sin((float)clockSeconds * 0.9f);
                var hint = _palette.Muted;
                hint.a = breathe;
                var hintHeight = Mathf.Min(ph * 0.12f, 52f);
                var hintWidth = EstimateWidth(_record.text.hint, hintHeight) + hintHeight * 1.2f;
                var pill = _palette.BackgroundBottom;
                pill.a = 0.92f;
                b.Round.RoundedRect(px + pw * 0.5f - hintWidth * 0.5f, py + ph * 0.5f - hintHeight * 0.85f, hintWidth, hintHeight * 1.7f, hintHeight * 0.85f, pill);
                texts.Add(new TextCommand("hint", _record.text.hint, px + pw * 0.5f, py + ph * 0.5f, hintHeight, hint, TextAnchorKind.MiddleCenter));
            }
        }

        /// <summary>One note of the live score: the flower of its column for a note of the ground, a small light dot in the colour of its cell for a note of the sky.</summary>
        void PaintFlower(in ScoreMark mark, double now, float px, float py, float pw, float ph, RecordBuilders b)
        {
            var p = ScoreLayout.Position(mark, now, _record.windowSec, pw, ph, _pitchSteps);
            var age = (float)((now - mark.Time) / _record.windowSec);
            var seconds = (float)(now - mark.Time);
            var pop = EaseOutBack(Mathf.Clamp01(seconds / PopSec));
            var alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeFrom, 1f, age));
            var size = ph * (0.15f + 0.08f * mark.Velocity) * pop;
            var glow = _palette.FlowerGlow[(int)mark.Part];
            if (Parts.IsSky(mark.Part))
            {
                PaintLightDot(px + p.X, py + p.Y, size * LightDotShare, glow, alpha, b);
                return;
            }
            FlowerIconPainter.Uv(Parts.Column(mark.Part), out var uvMin, out var uvMax);
            glow.a = 0.4f * alpha * Mathf.Clamp01(1.2f - seconds * 0.8f);
            if (glow.a > 0.01f) b.Glow.Centered(px + p.X, py + p.Y, size * 2.4f, glow, DiscMin, DiscMax);
            b.Flowers.Centered(px + p.X, py + p.Y, size, new Color(1f, 1f, 1f, alpha), uvMin, uvMax);
        }

        /// <summary>A small light: a soft halo around a brighter heart, drawn with the glows (no flower, so a note of the sky is never mistaken for a bloom).</summary>
        static void PaintLightDot(float x, float y, float size, Color color, float alpha, RecordBuilders b)
        {
            var halo = color;
            halo.a = 0.4f * alpha;
            b.Glow.Centered(x, y, size * 2.2f, halo, DiscMin, DiscMax);
            var heart = color;
            heart.a = alpha;
            b.Glow.Centered(x, y, size * 0.9f, heart, DiscMin, DiscMax);
        }

        void PaintNature(RecordLayout layout, double now, ScoreModel score, RecordBuilders b, List<TextCommand> texts)
        {
            var r = layout.Nature;
            var panel = _palette.Card;
            panel.a = 0.3f;
            b.Round.RoundedRect(r.X, r.Y, r.Width, r.Height, PanelRadius * Mathf.Min(1f, r.Height / 150f), panel);
            var pad = Mathf.Min(r.Height * 0.09f, 18f);
            var rowHeight = (r.Height - 2f * pad) / NatureLayers.Count;
            var labelWidth = r.Width * LabelWidthShare;
            var x0 = r.X + pad + labelWidth;
            var x1 = r.Right - pad;
            _glints.Clear();
            score.CopyVisibleNature(now, _record.windowSec, _glints);

            for (var layer = 0; layer < NatureLayers.Count; layer++)
            {
                var y = r.Top - pad - (layer + 0.5f) * rowHeight;
                var join = score.IsLayerActive((NatureLayer)layer)
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(now - score.LayerChangedAt((NatureLayer)layer)) / JoinSec))
                    : 0f;
                var line = Color.Lerp(_palette.Staff, _palette.NatureLine[layer], join);
                line.a = Mathf.Lerp(0.55f, 0.95f, join);
                var thickness = Mathf.Max(2f, rowHeight * 0.09f);
                b.Lines.Solid(x0, y - thickness * 0.5f, x1 - x0, thickness, line);
                if (join > 0.01f)
                {
                    var halo = _palette.NatureLine[layer];
                    halo.a = 0.3f * join;
                    b.Glow.Centered(x0, y, rowHeight * 1.7f, halo, DiscMin, DiscMax);
                }
                var label = Color.Lerp(_palette.Muted, _palette.Ink, join);
                label.a = Mathf.Lerp(0.55f, 1f, join);
                texts.Add(new TextCommand(LayerSlots[layer], _record.text.layers[layer], r.X + pad, y, Mathf.Min(rowHeight * 0.74f, 46f), label, TextAnchorKind.MiddleLeft));
            }

            foreach (var glint in _glints)
            {
                var layer = (int)glint.Layer;
                var y = r.Top - pad - (layer + 0.5f) * rowHeight;
                var age = (float)((now - glint.Time) / _record.windowSec);
                var x = Mathf.Lerp(x0, x1, 1f - age);
                var color = _palette.NatureLine[layer];
                color.a = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeFrom, 1f, age))) * Mathf.Lerp(0.6f, 1f, glint.Gain);
                b.Glow.Centered(x, y, rowHeight * Mathf.Lerp(0.55f, 1f, glint.Gain), color, DiscMin, DiscMax);
            }
        }

        void PaintCards(RecordLayout layout, IReadOnlyList<RecordCardView> cards, RecordBuilders b, List<TextCommand> texts, List<ThumbnailCommand> thumbnails)
        {
            var slots = layout.CardColumns * layout.CardRows;
            var count = Math.Min(cards.Count, slots);
            if (count == 0)
            {
                var c = layout.Cards;
                var muted = _palette.Muted;
                muted.a = 0.7f;
                texts.Add(new TextCommand("nocards", _record.text.noCards, c.X + c.Width * 0.5f, c.Y + c.Height * 0.5f, Mathf.Min(c.Height * 0.2f, 44f), muted, TextAnchorKind.MiddleCenter));
                return;
            }
            for (var i = 0; i < count; i++) PaintCard(layout.CardRect(i), cards[i], i == 0, i, b, texts, thumbnails);
        }

        void PaintCard(in LayoutRect s, RecordCardView view, bool newest, int index, RecordBuilders b, List<TextCommand> texts, List<ThumbnailCommand> thumbnails)
        {
            if (newest)
            {
                var ring = _palette.Accent;
                ring.a = 0.55f;
                b.Round.RoundedRect(s.X - 3f, s.Y - 3f, s.Width + 6f, s.Height + 6f, CardRadius + 3f, ring);
            }
            var fill = _palette.Card;
            fill.a = 0.95f;
            b.Round.RoundedRect(s.X, s.Y, s.Width, s.Height, CardRadius, fill);

            var thumbWidth = s.Width - 2f * ThumbPad;
            var thumbHeight = thumbWidth * ThumbAspect;
            var thumb = new LayoutRect(s.X + ThumbPad, s.Top - ThumbPad - thumbHeight, thumbWidth, thumbHeight);
            if (view.Thumbnail != null) thumbnails.Add(new ThumbnailCommand(index, thumb, view.Thumbnail));
            else
            {
                var quiet = _palette.BackgroundBottom;
                b.Round.RoundedRect(thumb.X, thumb.Y, thumb.Width, thumb.Height, 10f, quiet);
            }

            var stripHeight = thumb.Height * 0.4f;
            var dark = _palette.Background;
            var clear = new Color(dark.r, dark.g, dark.b, 0f);
            dark.a = 0.78f;
            b.Overlay.Quad(thumb.X, thumb.Y, thumb.Width, stripHeight, dark, dark, clear, clear, White, White);
            PaintMiniScore(view.Card, thumb, stripHeight, b);
            PaintTime(view, thumb, index, b, texts);

            var label = view.DetailLabel(_record.text);
            var below = s.Height - ThumbPad - thumb.Height;
            if (view.SongLabel.Length > 0)
            {
                PaintDetailAndSong(s, view, label, below, index, texts);
                return;
            }
            texts.Add(new TextCommand(CardSlots[index], label, s.X + s.Width * 0.5f, s.Y + below * 0.5f, Mathf.Min(Mathf.Min(below * 0.52f, s.Width * 0.125f), 40f), _palette.Ink, TextAnchorKind.MiddleCenter));
        }

        /// <summary>A card of a visit that played a song: the duration and nature count, and under them the title of the song, both centred in the room under the picture; the title is made smaller when it would be wider than the card.</summary>
        void PaintDetailAndSong(in LayoutRect s, RecordCardView view, string detail, float below, int index, List<TextCommand> texts)
        {
            var lineHeight = Mathf.Min(Mathf.Min(below * SongLineShare, s.Width * SongLineWidthShare), SongLineMaxHeight);
            var x = s.X + s.Width * 0.5f;
            texts.Add(new TextCommand(CardSlots[index], detail, x, s.Y + below * DetailLineCentre, lineHeight, _palette.Ink, TextAnchorKind.MiddleCenter));
            var title = view.SongLabel;
            var fitting = (s.Width - 2f * ThumbPad) / EstimateWidth(title, 1f);
            var accent = _palette.Accent;
            accent.a = 0.95f;
            texts.Add(new TextCommand(SongSlots[index], title, x, s.Y + below * SongLineCentre, Mathf.Min(lineHeight, fitting), accent, TextAnchorKind.MiddleCenter));
        }

        /// <summary>When the card was made (a time of day, in the corner of its picture on a dark fade): cards of one performance, replayed, would look exactly alike side by side.</summary>
        void PaintTime(RecordCardView view, in LayoutRect thumb, int index, RecordBuilders b, List<TextCommand> texts)
        {
            var dark = _palette.Background;
            var clear = new Color(dark.r, dark.g, dark.b, 0f);
            dark.a = 0.7f;
            var shade = thumb.Height * TimeShadeShare;
            b.Overlay.Quad(thumb.X, thumb.Top - shade, thumb.Width, shade, clear, clear, dark, dark, White, White);
            var height = Mathf.Min(thumb.Height * TimeLabelHeightShare, TimeLabelMaxHeight);
            var ink = _palette.Ink;
            ink.a = 0.95f;
            texts.Add(new TextCommand(TimeSlots[index], view.TimeLabel(_record.text),
                thumb.X + ThumbPad, thumb.Top - ThumbPad - height * 0.5f, height, ink, TextAnchorKind.MiddleLeft));
        }

        void PaintMiniScore(SessionCard card, in LayoutRect thumb, float stripHeight, RecordBuilders b)
        {
            var duration = Mathf.Max(1f, (float)card.DurationSec);
            var margin = 8f;
            var size = Mathf.Max(12f, stripHeight * 0.42f);
            var line = card.ScoreLine;
            for (var i = 0; i < line.Count; i++)   // an indexed loop: foreach over the interface makes an enumerator every frame
            {
                var mark = line[i];
                var x = thumb.X + margin + Mathf.Clamp01((float)(mark.Time / duration)) * (thumb.Width - 2f * margin);
                var y = thumb.Y + margin + (mark.PitchStep + 0.5f) / _pitchSteps * (stripHeight - 2f * margin);
                if (Parts.IsSky(mark.Part))
                {
                    PaintLightDot(x, y, size * LightDotShare, _palette.FlowerGlow[(int)mark.Part], 0.95f, b);
                    continue;
                }
                FlowerIconPainter.Uv(Parts.Column(mark.Part), out var uvMin, out var uvMax);
                b.Flowers.Centered(x, y, size, new Color(1f, 1f, 1f, 0.95f), uvMin, uvMax);
            }
        }

        /// <summary>About how wide a line of text is: a Korean syllable is about one em wide, a space a third of that.</summary>
        static float EstimateWidth(string text, float height)
        {
            var width = 0f;
            foreach (var c in text) width += c == ' ' ? 0.33f : (c < 128 ? 0.55f : 1f);
            return width * height;
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            var u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
