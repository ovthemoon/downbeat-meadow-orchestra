using System;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The note trail behind the hands and the names of the cells, on the overlay layer of the meadow camera: makes the two views as children of the layer, tells every frame which hands are there (the tracker's own
    /// hands, by id) and which cell each is in (<see cref="HandCellTracker"/>, which picks the cell as the conducting play does, so the lit cell is the instrument that plays), and keeps the notes of a hand in the colour
    /// of its cell. A fist, a hand that is gone and a hand held still leave no notes. Mouse and keyboard play have no hands in the picture, so they show neither trail nor cell. The strip of the record screen is read
    /// from the layer (<see cref="MeadowOverlayLayer.StripFraction"/>) at every frame, and so is the horizon where its two rows meet (<see cref="MeadowOverlayLayer.HorizonViewport"/>). The guide sorts at <c>SortingOrderBase + 10</c> (its names a little above), the notes at <c>SortingOrderBase + 20</c>.
    /// </summary>
    public sealed class TrailOverlay
    {
        public const int GuideSortingOffset = 10;
        public const int TrailSortingOffset = 20;

        readonly MeadowOverlayLayer _overlay;
        readonly TrailSettings _settings;
        readonly HandCellTracker _cells;
        readonly NoteTrailView _notes;
        readonly CellGuideView _guide;
        readonly TrailHandInput[] _inputs = new TrailHandInput[HandCellTracker.MaxHands];
        double _clock;

        public TrailOverlay(MeadowOverlayLayer overlay, ContentDto content)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            if (content == null) throw new ArgumentNullException(nameof(content));
            _settings = TrailSettings.From(content.trail);
            _cells = HandCellTracker.From(content);
            var cells = CellSettings.From(content.cells);
            var fonts = content.record != null ? content.record.fontNames : null;

            _guide = new GameObject("CellGuide").AddComponent<CellGuideView>();
            _guide.Configure(overlay.Root, overlay.LocalPoint, cells.Labels, cells.Colors, fonts, overlay.StripFraction, _settings, overlay.SortingOrderBase + GuideSortingOffset);
            _guide.HorizonViewport = overlay.HorizonViewport;
            _notes = new GameObject("NoteTrail").AddComponent<NoteTrailView>();
            _notes.Configure(overlay.Root, overlay.LocalPoint, _settings, overlay.StripFraction, overlay.SortingOrderBase + TrailSortingOffset);
            _notes.RowSplit = overlay.RowSplit;
            _notes.HorizonViewport = overlay.HorizonViewport;
        }

        public NoteTrailView Notes => _notes;
        public CellGuideView Guide => _guide;
        public HandCellTracker Cells => _cells;
        /// <summary>The seconds this has been ticked for (the clock of the views).</summary>
        public double Clock => _clock;

        /// <summary>A cheaper tier of the picture (Low) draws <c>trail.lowCountScale</c> of the notes, the first (best) tier all of them.</summary>
        public void SetCheaperTier(bool cheaper) => _notes.CountScale = cheaper ? _settings.LowCountScale : 1f;

        /// <summary>Once per drawn frame (<paramref name="deltaSec"/> long) with the tracker's hands.</summary>
        public void Tick(IHandMotionView motion, float deltaSec)
        {
            _clock += Math.Max(0f, deltaSec);
            _notes.OverlayFraction = _overlay.StripFraction;
            _notes.RowSplit = _overlay.RowSplit;
            _notes.HorizonViewport = _overlay.HorizonViewport;
            _guide.OverlayFraction = _overlay.StripFraction;
            _guide.HorizonViewport = _overlay.HorizonViewport;
            _cells.Update(_clock, motion);
            for (var slot = 0; slot < _inputs.Length; slot++) _inputs[slot] = InputOf(slot, motion);
            _notes.Tick(_clock, _inputs);
            _guide.Tick(_clock, _cells.CellOf(0), _cells.CellOf(1));
        }

        // The hand of an id: there when the tracker has it in its latest picture, and it leaves a trail only when it plays (is in a cell: not a fist) in the colour of that cell.
        TrailHandInput InputOf(int id, IHandMotionView motion)
        {
            if (motion == null || !motion.TryGetById(id, out var hand) || !hand.IsTracked) return default;
            var cell = _cells.CellOf(id);
            return new TrailHandInput(true, cell < 0, hand.X, hand.Y, hand.CaptureMs, hand.Id, hand.Epoch, cell < 0 ? Color.white : _guide.ColorOf(cell));
        }

        /// <summary>Takes the two views away (the meadow is built again, or switched off).</summary>
        public void Dispose()
        {
            if (_notes != null) OverlayAssets.Release(_notes.gameObject);
            if (_guide != null) OverlayAssets.Release(_guide.gameObject);
        }
    }
}
