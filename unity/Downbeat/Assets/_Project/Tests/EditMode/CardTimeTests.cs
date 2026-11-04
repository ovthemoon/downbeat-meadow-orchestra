using System;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The time of day on each card (item 11): cards of the same performance, replayed, would otherwise sit side by side looking exactly alike.</summary>
    public class CardTimeTests
    {
        static readonly string[] Korean = { "오전", "오후" };
        const string Format = "{0} {1}:{2:00}";

        static string Time(int hour, int minute, int second = 0) => CardText.TimeOfDay(new DateTime(2026, 10, 30, hour, minute, second), Format, Korean);

        [Test]
        public void TheMorning_IsAnteMeridiem_WithoutALeadingZeroOnTheHour()
        {
            Assert.AreEqual("오전 9:07", Time(9, 7));
            Assert.AreEqual("오전 11:59", Time(11, 59));
        }

        [Test]
        public void TheAfternoon_IsPostMeridiem_OnATwelveHourClock()
        {
            Assert.AreEqual("오후 3:42", Time(15, 42));
            Assert.AreEqual("오후 11:59", Time(23, 59));
        }

        [Test]
        public void Noon_IsTwelveInTheAfternoon_AndTwelveOhFiveIsStillAfternoon()
        {
            Assert.AreEqual("오후 12:00", Time(12, 0));
            Assert.AreEqual("오후 12:05", Time(12, 5));
        }

        [Test]
        public void Midnight_IsTwelveInTheMorning()
        {
            Assert.AreEqual("오전 12:00", Time(0, 0));
            Assert.AreEqual("오전 12:30", Time(0, 30));
        }

        [Test]
        public void TheSeconds_AreNotShown()
        {
            Assert.AreEqual(Time(15, 42), Time(15, 42, 59));
        }

        [Test]
        public void TheWordsAndTheOrder_ComeFromTheContentFormat()
        {
            var text = CardText.TimeOfDay(new DateTime(2026, 10, 30, 15, 42, 0), "{1}:{2:00} {0}", new[] { "AM", "PM" });

            Assert.AreEqual("3:42 PM", text);
        }

        [Test]
        public void TheTime_UsesTheInvariantCulture()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");   // other digits
                Assert.AreEqual("오후 3:42", Time(15, 42));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void ACardThatWasStoredBeforeTheTimeWasShown_StillLoads_AndShowsItsTime()
        {
            // exactly the file an older app wrote: version 1, the local start time, numbers and a score line; nothing was added for the time of day
            const string old = "{\"version\":1,\"id\":\"20260901-143005\",\"startedLocal\":\"2026-09-01T14:30:05\",\"durationSec\":71.6,\"noteCount\":12,\"maxLevel\":3,"
                + "\"scoreLine\":[{\"t\":0.5,\"part\":\"strings\",\"step\":3,\"v\":0.8},{\"t\":9.0,\"part\":\"perc\",\"step\":5,\"v\":0.6}],\"thumbnailFile\":\"20260901-143005.png\"}";

            var loaded = new JsonCardCodec().TryDecode(old, out var card, out var problem);

            Assert.IsTrue(loaded, problem);
            Assert.AreEqual(2, card.ScoreLine.Count);
            Assert.AreEqual("오후 2:30", CardText.TimeOfDay(card.StartedLocal, Format, Korean));
        }

        [Test]
        public void TheTimeSurvivesTheWayToDiskAndBack()
        {
            var card = new SessionCard("20261030-154230", new DateTime(2026, 10, 30, 15, 42, 30), 60, 5, 2, Array.Empty<ScoreMark>(), string.Empty);
            var codec = new JsonCardCodec();

            Assert.IsTrue(codec.TryDecode(codec.Encode(card), out var back, out var problem), problem);

            Assert.AreEqual(card.StartedLocal, back.StartedLocal);
            Assert.AreEqual("오후 3:42", CardText.TimeOfDay(back.StartedLocal, Format, Korean));
        }

        [Test]
        public void ACardWithoutAUsableTime_IsRefusedByTheLoader_NotShownWithAMadeUpOne()
        {
            var codec = new JsonCardCodec();

            Assert.IsFalse(codec.TryDecode("{\"version\":1,\"id\":\"x\",\"durationSec\":1,\"noteCount\":3,\"maxLevel\":1,\"scoreLine\":[],\"thumbnailFile\":\"\"}", out _, out var problem));
            StringAssert.Contains("time", problem);
        }
    }
}
