using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class VelocityMapperTests
    {
        [Test]
        public void Slow_clicks_are_medium_and_fast_clicks_are_loud()
        {
            Assert.AreEqual(0.5f, VelocityMapper.FromClickInterval(1.0), 1e-6);
            Assert.AreEqual(0.5f, VelocityMapper.FromClickInterval(double.NaN), 1e-6);
            Assert.AreEqual(1.0f, VelocityMapper.FromClickInterval(0.05), 1e-6);
            Assert.AreEqual(0.75f, VelocityMapper.FromClickInterval(0.35), 1e-6);
        }

        [Test]
        public void Velocity_never_decreases_as_clicks_get_faster()
        {
            var previous = 0f;
            for (var interval = 0.7; interval >= 0.05; interval -= 0.01)
            {
                var v = VelocityMapper.FromClickInterval(interval);
                Assert.GreaterOrEqual(v, previous - 1e-6f);
                previous = v;
            }
        }

        [Test]
        public void Gain_is_audible_for_the_softest_note_and_one_for_the_loudest()
        {
            Assert.AreEqual(0.3f, VelocityMapper.ToGain(0f), 1e-6);
            Assert.AreEqual(1.0f, VelocityMapper.ToGain(1f), 1e-6);
            Assert.AreEqual(1.0f, VelocityMapper.ToGain(7f), 1e-6);
        }

        [Test]
        public void Strike_speed_maps_from_the_minimum_at_the_trigger_to_full_strength_at_full_speed()
        {
            Assert.AreEqual(0.3f, VelocityMapper.FromStrikeSpeed(3f, 3f, 9f, 0.3f), 1e-6f);
            Assert.AreEqual(0.65f, VelocityMapper.FromStrikeSpeed(6f, 3f, 9f, 0.3f), 1e-6f);
            Assert.AreEqual(1f, VelocityMapper.FromStrikeSpeed(9f, 3f, 9f, 0.3f), 1e-6f);
            Assert.AreEqual(1f, VelocityMapper.FromStrikeSpeed(40f, 3f, 9f, 0.3f), 1e-6f);
            Assert.AreEqual(0.3f, VelocityMapper.FromStrikeSpeed(1f, 3f, 9f, 0.3f), 1e-6f);
        }
    }
}
