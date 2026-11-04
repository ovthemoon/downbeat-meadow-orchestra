using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class ContentValidatorTests
    {
        [Test]
        public void The_shipped_content_json_is_valid()
        {
            var errors = ContentValidator.Validate(TestContent.LoadShipped());
            Assert.IsEmpty(errors, string.Join("; ", errors));
        }

        [Test]
        public void Null_content_is_reported()
        {
            Assert.IsNotEmpty(ContentValidator.Validate(null));
        }

        [Test]
        public void A_duplicate_sample_id_is_reported()
        {
            var content = TestContent.LoadShipped();
            content.instruments[1].samples[0].id = content.instruments[0].samples[0].id;
            Assert.IsTrue(ContentValidator.Validate(content).Any(e => e.Contains("duplicated")));
        }

        [Test]
        public void An_unknown_part_and_a_bad_bpm_are_reported()
        {
            var content = TestContent.LoadShipped();
            content.instruments[0].part = "brass";
            content.song.bpm = 500f;
            var errors = ContentValidator.Validate(content);
            Assert.IsTrue(errors.Any(e => e.Contains("brass")));
            Assert.IsTrue(errors.Any(e => e.Contains("bpm")));
        }

        [Test]
        public void A_scale_that_cannot_be_played_with_the_given_samples_is_reported()
        {
            var content = TestContent.LoadShipped();
            foreach (var instrument in content.instruments) instrument.maxPitchShift = 0;
            Assert.IsTrue(ContentValidator.Validate(content).Any(e => e.Contains("maxPitchShift")));
        }

        [Test]
        public void A_content_file_without_the_meadow_is_reported()
        {
            var content = TestContent.LoadShipped();
            content.meadow = null;

            Assert.IsTrue(ContentValidator.Validate(content).Any(e => e.Contains("meadow is missing")));
        }

        [Test]
        public void The_shipped_content_has_the_hand_tracker_link_settings()
        {
            var hand = TestContent.LoadShipped().hand;

            Assert.AreEqual(47800, hand.udpPort);
            Assert.AreEqual(2f, hand.trackerTimeoutSec);
            Assert.IsTrue(hand.mirrorX);
        }

        [TestCase(80)]
        [TestCase(70000)]
        public void A_bad_udp_port_is_reported(int port)
        {
            var content = TestContent.LoadShipped();
            content.hand.udpPort = port;

            Assert.IsTrue(ContentValidator.Validate(content).Any(e => e.Contains("hand.udpPort")));
        }

        [TestCase(0.1f)]
        [TestCase(60f)]
        public void A_bad_tracker_timeout_is_reported(float seconds)
        {
            var content = TestContent.LoadShipped();
            content.hand.trackerTimeoutSec = seconds;

            Assert.IsTrue(ContentValidator.Validate(content).Any(e => e.Contains("hand.trackerTimeoutSec")));
        }

        [Test]
        public void Out_of_range_audio_settings_are_reported()
        {
            var content = TestContent.LoadShipped();
            content.audio.maxVoices = 0;
            content.audio.minLeadSec = 0.0;
            content.audio.masterGainDb = 3f;
            Assert.GreaterOrEqual(ContentValidator.Validate(content).Count, 3);
        }

        [Test]
        public void The_shipped_content_has_hand_settings_and_a_file_without_them_still_validates()
        {
            var content = TestContent.LoadShipped();
            Assert.IsNotNull(content.hand);

            content.hand = null;
            Assert.IsEmpty(ContentValidator.Validate(content));
        }

        [Test]
        public void Nonsense_hand_settings_are_reported()
        {
            var content = TestContent.LoadShipped();
            content.hand.triggerSpeed = 0f;
            content.hand.rearmSpeed = 20f;
            content.hand.fullSpeed = 0f;
            content.hand.minScore = 2f;

            var errors = ContentValidator.Validate(content);

            Assert.IsTrue(errors.Any(e => e.Contains("hand.triggerSpeed")));
            Assert.IsTrue(errors.Any(e => e.Contains("hand.rearmSpeed")));
            Assert.IsTrue(errors.Any(e => e.Contains("hand.fullSpeed")));
            Assert.IsTrue(errors.Any(e => e.Contains("hand.minScore")));
        }
    }
}
