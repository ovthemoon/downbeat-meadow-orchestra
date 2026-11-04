using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SecondsListTests
    {
        [Test]
        public void Parse_ReadsCommaSeparatedSeconds_InOrder()
        {
            CollectionAssert.AreEqual(new[] { 3.0, 8.5, 20.0 }, SecondsList.Parse("20, 3 ,8.5"));
        }

        [Test]
        public void Parse_SkipsWhatIsNotAPositiveNumber()
        {
            CollectionAssert.AreEqual(new[] { 5.0 }, SecondsList.Parse("x,-2,,NaN,5,Infinity"));
        }

        [Test]
        public void Parse_GivesNothingForNullOrEmpty()
        {
            Assert.IsEmpty(SecondsList.Parse(null));
            Assert.IsEmpty(SecondsList.Parse("  "));
        }

        [Test]
        public void Parse_UsesADotWhateverTheCulture()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                CollectionAssert.AreEqual(new[] { 2.5 }, SecondsList.Parse("2.5"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
