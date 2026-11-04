using System.Collections.Generic;
using Downbeat.Content;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>
    /// Piano-roll placeholder: beat lines and note dots scroll to the "now" line. A dot appears as soon as the note is accepted
    /// (immediate feedback) and pops when the note is heard. The view is placed far to the right of the stage in world space so
    /// that each camera only sees its own content.
    /// </summary>
    public sealed class AudienceView
    {
        const float WorldOffsetX = 100f;
        const float NowLineX = -2.2f;
        const float ViewRightX = 3.8f;
        const float UnitsPerSecond = 1.8f;
        const float PastSeconds = 0.9f;
        const float RowTop = 3.2f;
        const float RowBottom = -3.2f;
        const float LaneSpread = 0.16f;
        const float DotSize = 0.48f;
        const float HitPopSeconds = 0.35f;
        const float HitPopScale = 1.0f;
        const float LineHeight = 8.6f;
        const float SlotLineWidth = 0.012f;
        const float BeatLineWidth = 0.028f;
        const float BarLineWidth = 0.05f;
        const float SlotLineAlpha = 0.07f;
        const float BeatLineAlpha = 0.2f;
        const float BarLineAlpha = 0.38f;
        const float RowLineAlpha = 0.09f;
        const float RowLineHeight = 0.012f;
        const float RowLineWidth = 8.6f;
        const int MaxLines = 64;
        const int MaxDots = 96;
        const int BackgroundOrder = -100;
        const int LineOrder = -50;
        const int DotOrder = 10;

        sealed class Dot
        {
            public SpriteRenderer Renderer;
            public bool Active;
            public ScheduledNote Note;
            public float HitAt = -100f;
        }

        readonly Camera _camera;
        readonly Transform _background;
        readonly Vector2 _backgroundSize;
        readonly SpriteRenderer[] _lines = new SpriteRenderer[MaxLines];
        readonly Dot[] _dots = new Dot[MaxDots];
        readonly Color[] _partColors = new Color[Parts.Count];
        readonly int _pitchSteps;
        readonly int _beatsPerBar;
        readonly SpriteRenderer _nowLine;
        readonly SpriteRenderer[] _rows;

        public AudienceView(ContentBundle content, ViewLayout layout, Sprite whitePixel, Sprite disc, int pitchSteps, int beatsPerBar)
        {
            _pitchSteps = pitchSteps;
            _beatsPerBar = beatsPerBar;
            var theme = content.Dto.theme;
            var root = new GameObject("AudienceView").transform;
            root.position = new Vector3(WorldOffsetX, 0f, 0f);
            _camera = StageView.CreateCamera("AudienceCamera", layout.AudienceViewport, root, WorldOffsetX);

            var backgroundTexture = content.Texture(theme.audienceBackground);
            var bg = new GameObject("AudienceBackground").AddComponent<SpriteRenderer>();
            bg.transform.SetParent(root, false);
            bg.sprite = SpriteFactory.FromImage(backgroundTexture, theme.pixelsPerUnit, new Vector2(0.5f, 0.5f));
            bg.sortingOrder = BackgroundOrder;
            _background = bg.transform;
            _backgroundSize = new Vector2(backgroundTexture.width / theme.pixelsPerUnit, backgroundTexture.height / theme.pixelsPerUnit);

            foreach (var entry in theme.partColors)
            {
                if (Parts.TryParse(entry.part, out var part)) ColorUtility.TryParseHtmlString(entry.color, out _partColors[(int)part]);
            }

            for (var i = 0; i < MaxLines; i++)
            {
                var line = new GameObject("Line").AddComponent<SpriteRenderer>();
                line.transform.SetParent(root, false);
                line.sprite = whitePixel;
                line.sortingOrder = LineOrder;
                line.enabled = false;
                _lines[i] = line;
            }
            for (var i = 0; i < MaxDots; i++)
            {
                var renderer = new GameObject("Dot").AddComponent<SpriteRenderer>();
                renderer.transform.SetParent(root, false);
                renderer.sprite = disc;
                renderer.sortingOrder = DotOrder;
                renderer.enabled = false;
                _dots[i] = new Dot { Renderer = renderer };
            }
            _nowLine = new GameObject("NowLine").AddComponent<SpriteRenderer>();
            _nowLine.transform.SetParent(root, false);
            _nowLine.sprite = whitePixel;
            _nowLine.sortingOrder = LineOrder + 1;
            _nowLine.color = new Color(1f, 0.92f, 0.7f, 0.75f);
            _nowLine.transform.localPosition = new Vector3(NowLineX, 0f, 0f);
            _nowLine.transform.localScale = new Vector3(0.045f, LineHeight, 1f);

            _rows = new SpriteRenderer[pitchSteps];
            for (var i = 0; i < pitchSteps; i++)
            {
                var row = new GameObject("Row").AddComponent<SpriteRenderer>();
                row.transform.SetParent(root, false);
                row.sprite = whitePixel;
                row.sortingOrder = LineOrder - 1;
                row.color = new Color(1f, 1f, 1f, RowLineAlpha);
                var t = pitchSteps <= 1 ? 0.5f : i / (float)(pitchSteps - 1);
                row.transform.localPosition = new Vector3(0f, Mathf.Lerp(RowBottom, RowTop, t), 0f);
                row.transform.localScale = new Vector3(RowLineWidth, RowLineHeight, 1f);
                _rows[i] = row;
            }
        }

        public Camera Camera => _camera;

        /// <summary>A note was accepted and put on the grid: show its dot at once.</summary>
        public void OnScheduled(in ScheduledNote note)
        {
            for (var i = 0; i < _dots.Length; i++)
            {
                var dot = _dots[i];
                if (dot.Active) continue;
                dot.Active = true;
                dot.Note = note;
                dot.HitAt = -100f;
                dot.Renderer.enabled = true;
                return;
            }
        }

        /// <summary>The note is being heard now: pop its dot.</summary>
        public void OnHeard(in ScheduledNote note, float time)
        {
            for (var i = 0; i < _dots.Length; i++)
            {
                var dot = _dots[i];
                if (dot.Active && dot.Note.Slot == note.Slot && dot.Note.Part == note.Part && dot.Note.PitchStep == note.PitchStep && dot.HitAt < 0f) dot.HitAt = time;
            }
        }

        public void Tick(float time, double heardNow, BeatGrid grid)
        {
            var viewHeight = _camera.orthographicSize * 2f;
            var viewWidth = viewHeight * _camera.aspect;
            var scale = Mathf.Max(viewWidth / _backgroundSize.x, viewHeight / _backgroundSize.y) * 1.02f;
            _background.localScale = new Vector3(scale, scale, 1f);
            TickLines(heardNow, grid);
            TickDots(time, heardNow);
        }

        void TickLines(double heardNow, BeatGrid grid)
        {
            var futureSeconds = (ViewRightX - NowLineX) / UnitsPerSecond;
            var first = grid.FirstSlotAtOrAfter(heardNow - PastSeconds);
            var last = grid.LastSlotAtOrBefore(heardNow + futureSeconds);
            var slotsPerBeat = grid.SlotsPerBeat;
            var slotsPerBar = slotsPerBeat * _beatsPerBar;
            var used = 0;
            for (var slot = first; slot <= last && used < MaxLines; slot++)
            {
                var line = _lines[used++];
                var x = NowLineX + (float)((grid.SlotTime(slot) - heardNow) * UnitsPerSecond);
                var onBar = slot % slotsPerBar == 0;
                var onBeat = slot % slotsPerBeat == 0;
                line.enabled = true;
                line.transform.localPosition = new Vector3(x, 0f, 0f);
                line.transform.localScale = new Vector3(onBar ? BarLineWidth : (onBeat ? BeatLineWidth : SlotLineWidth), LineHeight, 1f);
                line.color = new Color(1f, 1f, 1f, onBar ? BarLineAlpha : (onBeat ? BeatLineAlpha : SlotLineAlpha));
            }
            for (var i = used; i < MaxLines; i++) _lines[i].enabled = false;
        }

        void TickDots(float time, double heardNow)
        {
            var leftEdge = NowLineX - PastSeconds * UnitsPerSecond;
            for (var i = 0; i < _dots.Length; i++)
            {
                var dot = _dots[i];
                if (!dot.Active) continue;
                var x = NowLineX + (float)((dot.Note.AudioTime - heardNow) * UnitsPerSecond);
                if (x < leftEdge) { dot.Active = false; dot.Renderer.enabled = false; continue; }

                var row = _pitchSteps <= 1 ? 0.5f : dot.Note.PitchStep / (float)(_pitchSteps - 1);
                var y = Mathf.Lerp(RowBottom, RowTop, row) + ((int)dot.Note.Part - 1) * LaneSpread;
                var size = DotSize;
                var brightness = 0.9f;
                if (dot.HitAt >= 0f)
                {
                    var e = Mathf.Clamp01(1f - (time - dot.HitAt) / HitPopSeconds);
                    size = DotSize * (1f + HitPopScale * e * e);
                    brightness = 0.9f + 0.6f * e;
                }
                var fade = Mathf.Clamp01((x - leftEdge) / (PastSeconds * UnitsPerSecond * 0.6f));
                var c = _partColors[(int)dot.Note.Part];
                dot.Renderer.transform.localPosition = new Vector3(x, y, 0f);
                dot.Renderer.transform.localScale = Vector3.one * size;
                dot.Renderer.color = new Color(c.r * brightness, c.g * brightness, c.b * brightness, fade);
            }
        }
    }
}
