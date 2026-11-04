using System;
using Downbeat.Content;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// The follow-along tutorial in the running app: once a frame it reads the hands (<see cref="TutorialHandSignals"/>), asks the <see cref="TutorialDirector"/> what is on and draws it with the <see cref="TutorialView"/>
    /// that hangs on the overlay layer of the meadow camera. It only watches: nothing it does reaches the notes, so a visitor's first note comes as soon as it would without the tutorial. The view is made once
    /// and lives under the overlay layer; a change of the <c>tutorial</c> block of the content waits for a restart (see <c>ContentSections</c>).
    /// </summary>
    public sealed class TutorialCoordinator
    {
        readonly TutorialDirector _director;
        readonly TutorialHandSignals _signals;
        readonly MeadowOverlayLayer _overlay;

        /// <param name="bundle">The content that was loaded: where the pictures of the hands are (the ones it could not load are not there, and those hands are drawn in code). Null: both are drawn in code.</param>
        public TutorialCoordinator(ContentDto content, MeadowOverlayLayer overlay, ContentBundle bundle = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            _overlay = overlay != null ? overlay : throw new ArgumentNullException(nameof(overlay));
            var cells = CellSettings.From(content.cells);
            var settings = TutorialSettings.From(content.tutorial);
            _director = new TutorialDirector(settings);
            _signals = new TutorialHandSignals(HandCellTracker.From(content));
            Texture2D openHand = null;
            Texture2D fist = null;
            bundle?.TryTexture(settings.OpenHandImage, out openHand);
            bundle?.TryTexture(settings.FistImage, out fist);
            View = TutorialView.Create(overlay.Root, overlay.LocalPoint, overlay.StripFraction, settings, cells.Labels, ColoursOf(cells), FontNames(content), overlay.SortingOrderBase, openHand, fist);
            View.HorizonViewport = overlay.HorizonViewport;
        }

        public TutorialView View { get; }
        /// <summary>What the director said in the latest <see cref="Tick"/>.</summary>
        public TutorialFrame Frame { get; private set; }
        /// <summary>What the hands said in the latest <see cref="Tick"/>.</summary>
        public TutorialHandSignals Signals => _signals;

        /// <param name="now">The time of the experience state machine (the audio clock).</param>
        /// <param name="idleSince">When the meadow became Idle (<see cref="ExperienceStateMachine.StateSince"/>).</param>
        /// <param name="inputNote">A note from the mouse or keyboard was played in this frame.</param>
        /// <param name="clockSeconds">A clock that runs all the time, for the breathing and the pops of the picture.</param>
        public void Tick(double now, ExperienceState state, double idleSince, IHandMotionView hands, bool inputNote, double clockSeconds)
        {
            _signals.Read(now, hands, inputNote);
            Frame = _director.Update(now, state, idleSince, _signals.OpenHandVisible, _signals.CellChanged, _signals.BothFists);
            View.OverlayFraction = _overlay.StripFraction;   // the strip can be hidden with Tab
            View.HorizonViewport = _overlay.HorizonViewport;
            View.Show(Frame, clockSeconds);
        }

        static Color[] ColoursOf(CellSettings cells)
        {
            var colours = new Color[cells.Colors.Length];
            for (var i = 0; i < colours.Length; i++)
            {
                if (!ColorHex.TryParse(cells.Colors[i], out var rgb)) throw new FormatException($"cells.colors[{i}] '{cells.Colors[i]}' is not a colour like #RRGGBB");
                colours[i] = new Color(rgb.R, rgb.G, rgb.B, 1f);
            }
            return colours;
        }

        static string[] FontNames(ContentDto content)
        {
            var names = content.record?.fontNames;
            return names != null && names.Length > 0 ? names : new RecordDto().fontNames;
        }
    }
}
