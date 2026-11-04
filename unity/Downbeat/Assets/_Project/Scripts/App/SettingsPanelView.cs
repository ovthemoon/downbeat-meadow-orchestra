using System;
using Downbeat.Core;
using Downbeat.Record;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// The small settings panel that the Tab key opens at the top of the window (the state is <see cref="SettingsPanelState"/>): the switch of the song mode, "곡 모드 켜짐 / 꺼짐" (or, when the app has no song to play, the plain
    /// words "곡 모드: 곡 없음", which is no switch), and, on one screen
    /// with the record strip, the switch of the strip, "기록 띠 보임 / 숨김" (what Tab alone did before). A switch is clicked with the mouse; each row names the state it is in now.
    /// The panel is drawn with the immediate mode GUI (<c>OnGUI</c>, like the latency square), in an operating-system font that has Korean (<c>record.fontNames</c>).
    /// <para>
    /// The panel is for the operator and must not reach the visitors' record: <c>OnGUI</c> is not drawn by <c>camera.Render()</c>, so the card thumbnails, <see cref="ScreensCapture"/> and <see cref="FrameDump"/> never show it.
    /// The recorders that film the window itself (<see cref="RunAutomation"/>, <see cref="RunRecorder"/>, <see cref="WindowCompare"/>) show it while it is open, and only then (it closes by itself after ten idle seconds).
    /// A hand cannot touch it, and a mouse click on it plays no note (<c>DebugPointerInput.IsBlockedAt</c> is <see cref="IsOver"/>).
    /// </para>
    /// </summary>
    public sealed class SettingsPanelView : MonoBehaviour
    {
        public const string SongOnLabel = "곡 모드 켜짐";
        public const string SongOffLabel = "곡 모드 꺼짐";
        public const string SongNoneLabel = "곡 모드: 곡 없음";
        public const string StripShownLabel = "기록 띠 보임";
        public const string StripHiddenLabel = "기록 띠 숨김";

        const float RowHeightOfScreen = 0.05f;
        const float MinRowHeight = 32f;
        const float PanelWidthInRows = 7.5f;
        const float PaddingInRows = 0.25f;
        const float GapInRows = 0.15f;
        const float TopMarginOfScreen = 0.02f;
        const float FontSizeInRows = 0.45f;
        const float TrackWidthInRows = 1.0f;
        const float TrackHeightInRows = 0.5f;
        const float KnobInsetInRows = 0.05f;
        const float LabelGapInRows = 0.3f;
        const int FontLoadSize = 32;

        static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.11f, 0.88f);
        static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color RowHoverColor = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color OnColor = new Color(0.36f, 0.78f, 0.45f, 1f);
        static readonly Color OffColor = new Color(0.45f, 0.48f, 0.52f, 1f);
        static readonly Color TextColor = new Color(0.96f, 0.97f, 0.98f, 1f);

        readonly SettingsPanelState _state = new SettingsPanelState();
        SongModeStore _store;
        string[] _fontNames;
        Font _font;
        GUIStyle _labelStyle;
        Func<bool> _stripShown;
        Action _toggleStrip;

        /// <summary>The song mode switch was clicked: the new value. The panel has saved it by then; the song session follows it from the next visit.</summary>
        public event Action<bool> SongModeChanged;

        /// <summary>The state the song switch shows. The app sets it once when it starts (setting it raises no event); a click changes it.</summary>
        public bool SongModeOn { get; set; } = true;

        /// <summary>The app has a song session. The app sets it once when it starts; without one (no song in the content, every song left out, <c>--latency-mode</c>) the row only says so: it takes no click and saves nothing.</summary>
        public bool HasSongs { get; set; } = true;

        /// <summary>The words on the song row now.</summary>
        public string SongLabel => SongRowLabel(HasSongs, SongModeOn);

        public bool IsOpen => _state.IsOpen;

        /// <summary>How many times the open panel was drawn (a repaint of <c>OnGUI</c> that drew it). It stays 0 in batch mode, where Unity does not run <c>OnGUI</c> at all; a test that runs with a window can see the drawing here.</summary>
        public int DrawCount { get; private set; }

        /// <summary>The strip row is there: one screen with the record strip.</summary>
        public bool HasStripSwitch => _toggleStrip != null;

        /// <summary>Whether the record strip is shown now (false without a strip switch).</summary>
        public bool StripShown => _stripShown != null && _stripShown();

        /// <summary>Where the open panel is, in the coordinates of the immediate mode GUI (pixels, the origin is the top left corner of the window); an empty rectangle while it is closed.</summary>
        public Rect ScreenRect => _state.IsOpen ? PanelRect(Screen.width, Screen.height, HasStripSwitch ? 2 : 1) : default;

        /// <param name="fontNames">The operating-system fonts to try in order (<c>record.fontNames</c>).</param>
        /// <param name="store">Where a click on the song switch keeps its value.</param>
        public void Configure(string[] fontNames, SongModeStore store)
        {
            _fontNames = fontNames != null && fontNames.Length > 0 ? fontNames : new RecordDto().fontNames;
            _store = store;
            Disposal.Release(_font);
            _labelStyle = null;   // it holds the font that was just released: the next draw makes a style with the new one
            _font = CreatePanelFont(_fontNames);
        }

        /// <summary>
        /// The immediate mode GUI draws no text at all when the first name it is given is not installed (it does not try the later ones, unlike the text meshes), so the font is made from the installed names only
        /// (<see cref="FontChoice"/> also knows the Korean spelling of a family, as a Korean Windows lists it).
        /// One info line says which names were used. When none of them is installed the list is passed as it is (the words stay blank, as they would have), and one warning says so and shows the installed names that look Korean.
        /// </summary>
        static Font CreatePanelFont(string[] wanted)
        {
            var installedNames = Font.GetOSInstalledFontNames();
            var installed = FontChoice.InstalledFirst(wanted, installedNames);
            if (installed.Length > 0)
            {
                Debug.Log(FontChoice.UsedFontsLine(installed));   // the first log of a new machine shows which spelling was chosen, also when the panel turns out blank
                return Font.CreateDynamicFontFromOSFont(installed, FontLoadSize);
            }
            Debug.LogWarning(FontChoice.NoneInstalledWarning(wanted, installedNames));
            return Font.CreateDynamicFontFromOSFont(wanted, FontLoadSize);
        }

        /// <summary>The names the panel's font was made from, in order: the installed spellings of <c>record.fontNames</c>, each followed by the wanted spelling when that differs (the wanted list itself when none is installed); null before <see cref="Configure"/>.</summary>
        public string[] FontNamesInUse => _font != null ? _font.fontNames : null;

        /// <summary>Adds the second row: <paramref name="isShown"/> says whether the strip is shown, <paramref name="toggle"/> hides or shows it.</summary>
        public void AddStripSwitch(Func<bool> isShown, Action toggle)
        {
            _stripShown = isShown ?? throw new ArgumentNullException(nameof(isShown));
            _toggleStrip = toggle ?? throw new ArgumentNullException(nameof(toggle));
        }

        void Awake() => enabled = false;   // a closed panel has no OnGUI to run: the immediate mode GUI costs something every frame, also when it draws nothing

        void OnDestroy()
        {
            Disposal.Release(_font);   // the operating-system font that was made for the labels
        }

        /// <summary>Tab. <paramref name="now"/> is <c>Time.realtimeSinceStartupAsDouble</c>, the clock of every touch of the panel.</summary>
        public void ToggleOpen(double now)
        {
            _state.ToggleOpen(now);
            enabled = _state.IsOpen;
        }

        /// <summary>Once a frame: closes the panel that was left alone.</summary>
        public void Tick(double now)
        {
            _state.Tick(now);
            if (enabled != _state.IsOpen) enabled = _state.IsOpen;
        }

        /// <summary>Whether the mouse (the position that <c>Input.mousePosition</c> gives: the origin is the bottom left corner) is on the open panel.</summary>
        public bool IsOver(Vector2 mousePosition) => _state.IsOpen && ScreenRect.Contains(new Vector2(mousePosition.x, Screen.height - mousePosition.y));

        /// <summary>What a click on the song switch does: keeps the value, writes it and the local time in the log and tells the listeners. The same value again changes nothing, and neither does anything without songs.</summary>
        public void SetSongMode(bool on)
        {
            if (!HasSongs || on == SongModeOn) return;
            SongModeOn = on;
            _state.Touch(Time.realtimeSinceStartupAsDouble);
            _store?.Save(on);
            Debug.Log(SongModeSetting.SwitchLogLine(on, DateTime.Now));
            SongModeChanged?.Invoke(on);
        }

        /// <summary>What a click on the strip switch does: hides the strip that is shown and shows the one that is hidden (nothing without a strip switch).</summary>
        public void ToggleStrip()
        {
            if (_toggleStrip == null) return;
            _state.Touch(Time.realtimeSinceStartupAsDouble);
            _toggleStrip();
        }

        /// <summary>The panel for a window of this size: centred at the top, as wide as seven and a half rows.</summary>
        public static Rect PanelRect(float screenWidth, float screenHeight, int rows)
        {
            var row = RowHeight(screenHeight);
            var width = Mathf.Min(PanelWidthInRows * row, screenWidth);
            var height = rows * row + (rows - 1) * GapInRows * row + 2f * PaddingInRows * row;
            return new Rect((screenWidth - width) * 0.5f, screenHeight * TopMarginOfScreen, width, height);
        }

        /// <summary>The words of the song row: the state of the switch, or "no song" when there is no session whatever the switch was.</summary>
        public static string SongRowLabel(bool hasSongs, bool on) => !hasSongs ? SongNoneLabel : on ? SongOnLabel : SongOffLabel;

        static float RowHeight(float screenHeight) => Mathf.Max(MinRowHeight, screenHeight * RowHeightOfScreen);

        void OnGUI()
        {
            if (!_state.IsOpen || _fontNames == null) return;
            if (Event.current.type == EventType.Repaint) DrawCount++;
            var panel = ScreenRect;
            var row = RowHeight(Screen.height);
            Fill(panel, PanelColor, PaddingInRows * row);
            var rowRect = new Rect(panel.x + PaddingInRows * row, panel.y + PaddingInRows * row, panel.width - 2f * PaddingInRows * row, row);
            if (DrawSwitch(rowRect, HasSongs && SongModeOn, SongLabel, HasSongs)) SetSongMode(!SongModeOn);
            if (!HasStripSwitch) return;
            rowRect.y += row * (1f + GapInRows);
            var shown = StripShown;
            if (DrawSwitch(rowRect, shown, shown ? StripShownLabel : StripHiddenLabel)) ToggleStrip();
        }

        /// <summary>One row: a switch (green and to the right when on) and its words; true when it was clicked (a row that is not <paramref name="clickable"/> only shows).</summary>
        bool DrawSwitch(Rect rowRect, bool on, string label, bool clickable = true)
        {
            var row = rowRect.height;
            Fill(rowRect, clickable && rowRect.Contains(Event.current.mousePosition) ? RowHoverColor : RowColor, GapInRows * row);
            var track = new Rect(rowRect.x + PaddingInRows * row, rowRect.y + (row - TrackHeightInRows * row) * 0.5f, TrackWidthInRows * row, TrackHeightInRows * row);
            Fill(track, on ? OnColor : OffColor, track.height * 0.5f);
            var knobSize = track.height - 2f * KnobInsetInRows * row;
            var knobX = on ? track.xMax - knobSize - KnobInsetInRows * row : track.x + KnobInsetInRows * row;
            Fill(new Rect(knobX, track.y + KnobInsetInRows * row, knobSize, knobSize), Color.white, knobSize * 0.5f);
            GUI.Label(new Rect(track.xMax + LabelGapInRows * row, rowRect.y, rowRect.xMax - track.xMax - LabelGapInRows * row, row), label, LabelStyle(row));
            return clickable && GUI.Button(rowRect, GUIContent.none, GUIStyle.none);
        }

        GUIStyle LabelStyle(float rowHeight)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, font = _font };
                _labelStyle.normal.textColor = TextColor;
            }
            _labelStyle.fontSize = Mathf.RoundToInt(rowHeight * FontSizeInRows);
            return _labelStyle;
        }

        static void Fill(Rect rect, Color color, float cornerRadius) =>
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, cornerRadius);
    }
}
