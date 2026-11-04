using System;
using System.Collections;
using System.Collections.Generic;
using Downbeat.App;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class GuardedRoutineTests
    {
        static IEnumerator Counting(List<string> log, string name, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                log.Add($"{name}{i}");
                yield return name + i;
            }
        }

        static IEnumerator ThrowsAfter(int yields, string message)
        {
            for (var i = 0; i < yields; i++) yield return i;
            throw new InvalidOperationException(message);
        }

        static IEnumerator Parent(List<string> log, IEnumerator child)
        {
            log.Add("start");
            yield return child;          // a nested routine, as Boot does with the content load
            log.Add("end");
        }

        static List<object> Drain(IEnumerator routine)
        {
            var yielded = new List<object>();
            while (routine.MoveNext()) yielded.Add(routine.Current);
            return yielded;
        }

        [Test]
        public void ARoutineThatFinishes_YieldsWhatItYields_AndReportsNothing()
        {
            var log = new List<string>();
            Exception caught = null;

            var yielded = Drain(GuardedRoutine.Run(Counting(log, "a", 3), e => caught = e));

            CollectionAssert.AreEqual(new object[] { "a0", "a1", "a2" }, yielded);
            Assert.IsNull(caught);
        }

        [Test]
        public void AThrowInTheRoutineItself_IsReported_AndTheGuardEnds()
        {
            Exception caught = null;

            var yielded = Drain(GuardedRoutine.Run(ThrowsAfter(2, "boom"), e => caught = e));

            Assert.AreEqual(2, yielded.Count, "what came before the throw is still passed on");
            Assert.IsInstanceOf<InvalidOperationException>(caught);
            Assert.AreEqual("boom", caught.Message);
        }

        [Test]
        public void ANestedRoutine_IsRunToTheEnd_BeforeTheParentGoesOn_AndItsYieldsAreHandedOn()
        {
            var log = new List<string>();

            var yielded = Drain(GuardedRoutine.Run(Parent(log, Counting(log, "c", 2)), _ => Assert.Fail("nothing throws")));

            CollectionAssert.AreEqual(new[] { "start", "c0", "c1", "end" }, log);
            CollectionAssert.AreEqual(new object[] { "c0", "c1" }, yielded, "the engine only ever sees the real waits, never the nested routine itself");
        }

        [Test]
        public void AThrowInANestedRoutine_IsReported_AndTheParentDoesNotGoOn()
        {
            var log = new List<string>();
            Exception caught = null;

            Drain(GuardedRoutine.Run(Parent(log, ThrowsAfter(1, "inner")), e => caught = e));

            Assert.AreEqual("inner", caught?.Message, "Unity would stop the outer coroutine without a word; here it is told");
            CollectionAssert.AreEqual(new[] { "start" }, log, "the parent never got to 'end'");
        }

        [Test]
        public void SeveralLevelsOfNesting_AreAllGuarded()
        {
            var log = new List<string>();
            Exception caught = null;

            Drain(GuardedRoutine.Run(Parent(log, Parent(log, ThrowsAfter(0, "deep"))), e => caught = e));

            Assert.AreEqual("deep", caught?.Message);
        }
    }
}
