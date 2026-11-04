using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>When the Tab panel is open: Tab opens and closes it, and it closes by itself after <see cref="SettingsPanelState.IdleCloseSec"/> without a touch.</summary>
    public class SettingsPanelStateTests
    {
        const double Start = 100.0;

        [Test]
        public void Panel_ClosedUntilTab_ClosesAfterTenIdleSeconds()
        {
            var panel = new SettingsPanelState();
            Assert.IsFalse(panel.IsOpen, "closed until Tab");

            panel.ToggleOpen(Start);
            panel.Tick(Start + SettingsPanelState.IdleCloseSec - 0.1);
            Assert.IsTrue(panel.IsOpen, "still open just before the ten seconds");

            panel.Tick(Start + SettingsPanelState.IdleCloseSec);
            Assert.IsFalse(panel.IsOpen, "closed after ten idle seconds");
            Assert.AreEqual(10.0, SettingsPanelState.IdleCloseSec, "the spec says ten seconds");
        }

        [Test]
        public void Panel_Tab_OpensAndClosesIt()
        {
            var panel = new SettingsPanelState();

            panel.ToggleOpen(Start);
            Assert.IsTrue(panel.IsOpen);
            panel.ToggleOpen(Start + 1.0);
            Assert.IsFalse(panel.IsOpen, "Tab once more closes it");
            panel.ToggleOpen(Start + 2.0);
            Assert.IsTrue(panel.IsOpen, "and opens it again");
        }

        [Test]
        public void Panel_TouchKeepsItOpen()
        {
            var panel = new SettingsPanelState();
            panel.ToggleOpen(Start);

            panel.Touch(Start + 8.0);
            panel.Tick(Start + 17.9);
            Assert.IsTrue(panel.IsOpen, "ten seconds count from the last touch");
            panel.Tick(Start + 18.0);
            Assert.IsFalse(panel.IsOpen);
        }

        [Test]
        public void Panel_OpenedAgain_CountsTheTenSecondsFromThatTab()
        {
            var panel = new SettingsPanelState();
            panel.ToggleOpen(Start);
            panel.Tick(Start + SettingsPanelState.IdleCloseSec);

            panel.ToggleOpen(Start + 50.0);
            panel.Tick(Start + 59.0);

            Assert.IsTrue(panel.IsOpen, "the old touch does not close the new visit of the panel");
        }

        [Test]
        public void Panel_TouchWhileClosed_DoesNotOpenIt()
        {
            var panel = new SettingsPanelState();

            panel.Touch(Start);
            panel.Tick(Start + 1.0);

            Assert.IsFalse(panel.IsOpen);
        }
    }

    /// <summary>The words of the panel: each row names the state its switch is in now.</summary>
    public class SettingsPanelLabelTests
    {
        [Test]
        public void Labels_NameTheStateOfTheSwitch()
        {
            Assert.AreEqual("곡 모드 켜짐", SettingsPanelView.SongOnLabel);
            Assert.AreEqual("곡 모드 꺼짐", SettingsPanelView.SongOffLabel);
            Assert.AreEqual("기록 띠 보임", SettingsPanelView.StripShownLabel, "the strip is shown: not 'show', which would read as an action");
            Assert.AreEqual("기록 띠 숨김", SettingsPanelView.StripHiddenLabel);
        }

        [Test]
        public void SongRowLabel_WithSongs_IsTheStateOfTheSwitch()
        {
            Assert.AreEqual(SettingsPanelView.SongOnLabel, SettingsPanelView.SongRowLabel(true, true));
            Assert.AreEqual(SettingsPanelView.SongOffLabel, SettingsPanelView.SongRowLabel(true, false));
        }

        [Test]
        public void SongRowLabel_WithoutSongs_SaysSo_WhateverTheSwitchWas()
        {
            Assert.AreEqual("곡 모드: 곡 없음", SettingsPanelView.SongNoneLabel);
            Assert.AreEqual(SettingsPanelView.SongNoneLabel, SettingsPanelView.SongRowLabel(false, true), "no session: a 'on' that changes nothing would be a lie");
            Assert.AreEqual(SettingsPanelView.SongNoneLabel, SettingsPanelView.SongRowLabel(false, false));
        }
    }
}
