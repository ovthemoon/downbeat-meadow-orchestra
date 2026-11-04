using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Controls;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>The Tab panel in the real scene: the song switch (event and saved file), the strip switch (what Tab alone did before), and a click on the panel that plays no note.</summary>
    public class SettingsPanelPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const double ClickTime = 1.0;

        string _dir;
        string _settingsFile;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "downbeat-panel-" + Guid.NewGuid().ToString("N"));
            _settingsFile = Path.Combine(_dir, "settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        IEnumerator Start(Action<DownbeatLifetimeScope, DownbeatApp> ready)
        {
            var args = new[] { "Downbeat", "--input", "mouse", "--mute", "--no-tutorial", "--songs", "on", "--settings-file", _settingsFile };   // on: the tests of the switch start from the shipped answer (the assembly default is off)
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var scope = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment: " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            ready(scope, app);
        }

        /// <summary>The middle of the open panel as the mouse reports it (the origin of the mouse is the bottom left corner, the origin of the panel rectangle the top left).</summary>
        static Vector2 MouseInside(SettingsPanelView panel) => new Vector2(panel.ScreenRect.center.x, Screen.height - panel.ScreenRect.center.y);

        [UnityTest]
        public IEnumerator Panel_SongSwitch_RaisesTheEventAndSaves()
        {
            DownbeatApp app = null;
            yield return Start((s, a) => app = a);
            var panel = app.SettingsPanel;
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.SongModeOn, "the song mode starts on until the app says otherwise");
            var raised = new System.Collections.Generic.List<bool>();
            panel.SongModeChanged += raised.Add;

            LogAssert.Expect(LogType.Log, new Regex(@"^\[songs\] switch off \(tab\) at \d{4}-\d\d-\d\d \d\d:\d\d:\d\d$"));
            panel.SetSongMode(false);
            Assert.AreEqual(new[] { false }, raised.ToArray(), "the event carries the new value");
            Assert.IsFalse(panel.SongModeOn);
            Assert.AreEqual("{\"songMode\":false}", File.ReadAllText(_settingsFile), "--settings-file says where the choice is kept");

            panel.SetSongMode(false);
            Assert.AreEqual(1, raised.Count, "the same value again is no change");

            LogAssert.Expect(LogType.Log, new Regex(@"^\[songs\] switch on \(tab\) at \d{4}-\d\d-\d\d \d\d:\d\d:\d\d$"));
            panel.SetSongMode(true);
            Assert.AreEqual(new[] { false, true }, raised.ToArray());
            Assert.AreEqual(true, new SongModeStore(_settingsFile).Load(), "and the next start of the app finds it");
        }

        [UnityTest]
        public IEnumerator Panel_StripSwitch_HidesTheStrip()
        {
            DownbeatApp app = null;
            yield return Start((s, a) => app = a);
            var panel = app.SettingsPanel;
            var overlay = UnityEngine.Object.FindFirstObjectByType<MeadowStage>().Overlay;
            var strip = overlay.StripFraction;
            Assert.AreEqual(DisplayMode.SingleOverlay, app.Display.Mode, "the editor has one screen");
            Assert.IsTrue(panel.HasStripSwitch, "one screen with a strip: the panel has the second switch");
            Assert.IsTrue(panel.StripShown);
            Assert.Greater(strip, 0f);

            panel.ToggleStrip();
            Assert.IsFalse(app.Display.StripShown, "what Tab alone did before the panel");
            Assert.IsFalse(app.Record.View.Visible);
            Assert.AreEqual(0f, overlay.StripFraction, "the cells and the hands use the whole picture");
            Assert.IsFalse(panel.StripShown);

            panel.ToggleStrip();
            Assert.IsTrue(app.Display.StripShown);
            Assert.IsTrue(app.Record.View.Visible);
            Assert.AreEqual(strip, overlay.StripFraction, 1e-6f, "and back");
        }

        [UnityTest]
        public IEnumerator Panel_HasNoStripSwitch_WhenThereIsNoRecord()
        {
            var args = new[] { "Downbeat", "--input", "mouse", "--mute", "--no-tutorial", "--no-record", "--settings-file", _settingsFile };
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var app = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>().Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment: " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);

            Assert.IsNotNull(app.SettingsPanel, "the song switch is there without a record screen");
            Assert.IsFalse(app.SettingsPanel.HasStripSwitch);
        }

        [UnityTest]
        public IEnumerator Panel_ClickInside_PlaysNoNote()
        {
            DownbeatApp app = null;
            DebugPointerInput pointer = null;
            yield return Start((s, a) => { app = a; pointer = s.Container.Resolve<DebugPointerInput>(); });
            var panel = app.SettingsPanel;
            var outside = new Vector2(Screen.width * 0.15f, Screen.height * 0.55f);

            Assert.IsTrue(pointer.Click(outside, ClickTime), "a click on the meadow plays a note");
            Assert.IsTrue(pointer.TryDequeue(out _));

            panel.ToggleOpen(Time.realtimeSinceStartupAsDouble);
            Assert.IsTrue(panel.IsOpen);
            Assert.Greater(panel.ScreenRect.width, 0f);
            Assert.IsFalse(pointer.Click(MouseInside(panel), ClickTime + 1.0), "a click inside the open panel plays no note");
            Assert.IsFalse(pointer.TryDequeue(out _), "and nothing is queued");
            Assert.IsTrue(pointer.Click(outside, ClickTime + 2.0), "outside the panel the meadow still plays");
            Assert.IsTrue(pointer.TryDequeue(out _));

            var inside = MouseInside(panel);
            panel.ToggleOpen(Time.realtimeSinceStartupAsDouble);
            Assert.IsFalse(panel.IsOpen);
            Assert.IsFalse(panel.IsOver(inside), "a closed panel is nowhere");
            Assert.IsTrue(pointer.Click(inside, ClickTime + 3.0), "and takes no clicks");
            Assert.IsTrue(pointer.TryDequeue(out _));
        }

        [UnityTest]
        public IEnumerator Panel_Open_IsASmallPanelAtTheTop_AndClosesWhenLeftAlone()
        {
            DownbeatApp app = null;
            yield return Start((s, a) => app = a);
            var panel = app.SettingsPanel;
            Assert.IsFalse(panel.IsOpen);
            Assert.IsFalse(panel.enabled, "closed: there is no OnGUI to run");
            Assert.AreEqual(default(Rect), panel.ScreenRect);
            yield return null;
            yield return null;
            Assert.AreEqual(0, panel.DrawCount, "a closed panel is not drawn");

            var openedAt = Time.realtimeSinceStartupAsDouble;
            panel.ToggleOpen(openedAt);
            Assert.IsTrue(panel.enabled);
            yield return null;
            yield return null;
            // Unity does not run OnGUI in batch mode (a probe saw no call at all), so there only the geometry below is checked; in the editor with a window the panel is really drawn
            if (!Application.isBatchMode) Assert.Greater(panel.DrawCount, 0, "OnGUI drew the open panel");
            var rect = panel.ScreenRect;
            Assert.IsTrue(panel.IsOver(MouseInside(panel)));
            Assert.IsFalse(panel.IsOver(Vector2.zero), "the bottom left corner of the window is not the panel");
            Assert.Less(rect.height, Screen.height * 0.5f, "a small panel");
            Assert.Less(rect.y, Screen.height * 0.25f, "at the top of the window");
            Assert.GreaterOrEqual(rect.x, 0f);
            Assert.LessOrEqual(rect.xMax, Screen.width, "inside the window");

            panel.Tick(openedAt + SettingsPanelState.IdleCloseSec + 0.1);
            Assert.IsFalse(panel.IsOpen, "left alone, it closes");
            Assert.IsFalse(panel.enabled);
        }

        [UnityTest]
        public IEnumerator Panel_WhenDestroyed_DestroysTheFontItMadeForTheLabels()
        {
            var fontsBefore = Resources.FindObjectsOfTypeAll<Font>().Length;
            var panelObject = new GameObject("PanelFontTest");
            var panel = panelObject.AddComponent<SettingsPanelView>();

            panel.Configure(null, null);   // the fonts of the shipped content are the defaults of the record
            Assert.Greater(Resources.FindObjectsOfTypeAll<Font>().Length, fontsBefore, "the panel made a font for its labels");

            UnityEngine.Object.Destroy(panelObject);
            yield return null;

            Assert.AreEqual(fontsBefore, Resources.FindObjectsOfTypeAll<Font>().Length, "and destroying the panel destroys it again");
        }
    }
}
