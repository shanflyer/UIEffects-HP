using NUnit.Framework;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class BakeStaggerTests
    {
        [Test] public void SequentialPhasesSpreadAcrossTheWholePeriod()
        {
            Assert.AreEqual(0, BakeStagger.Phase(0)); Assert.AreEqual(.5, BakeStagger.Phase(1));
            Assert.AreEqual(.25, BakeStagger.Phase(2)); Assert.AreEqual(.75, BakeStagger.Phase(3));
        }
        [Test] public void TwentyEffectsAtHalfRateSplitAcrossFramesWithoutLosingTime()
        {
            var clocks = new EffectUpdateClock[20];
            var simulated = new double[20];
            for (int i = 0; i < clocks.Length; ++i)
                clocks[i].AdvanceScheduled(0, 0, 0, 50, BakeStagger.Phase((uint)i), false, out _, out _);
            int total = 0, peak = 0;
            for (int frame = 1; frame <= 120; ++frame)
            {
                int count = 0;
                for (int i = 0; i < clocks.Length; ++i)
                    if (clocks[i].AdvanceScheduled(1f / 120, 1f / 60, frame, 50,
                        BakeStagger.Phase((uint)i), false, out float scaled, out _))
                    { ++count; simulated[i] += scaled; }
                peak = System.Math.Max(peak, count); total += count;
            }
            Assert.AreEqual(10, peak); Assert.AreEqual(1200, total);
            for (int i = 0; i < clocks.Length; ++i)
            {
                clocks[i].AdvanceEveryFrame(0, 0, out float pending, out _);
                Assert.AreEqual(1, simulated[i] + pending, .00001);
            }
        }
        [TestCase(1)] [TestCase(25)] [TestCase(33)] [TestCase(50)] [TestCase(75)] [TestCase(100)]
        public void PercentageTracksActualFramesRegardlessOfFrameDuration(int percentage)
        {
            foreach (int firstFrame in new[] { 0, 2000000000 })
            {
                var clock = new EffectUpdateClock(); int updates = 0;
                double total = 0, scaledTotal = 0, simulated = 0, unscaledSimulated = 0;
                clock.AdvanceScheduled(0, 0, firstFrame, percentage, .25, false, out _, out _);
                for (int i = 1; i <= 200; i++)
                {
                    // Includes frame-rate drops, recovery and paused game time.
                    float delta = i <= 50 ? 1f / 120 : i <= 100 ? 1f / 15 : 1f / 60;
                    float scaled = i <= 100 ? delta * .5f : 0;
                    total += delta; scaledTotal += scaled;
                    if (clock.AdvanceScheduled(scaled, delta, firstFrame + i, percentage, .25, false, out float s, out float u))
                    { updates++; simulated += s; unscaledSimulated += u; }
                }
                clock.AdvanceEveryFrame(0, 0, out float pending, out float unscaledPending);
                Assert.AreEqual(percentage * 2, updates);
                Assert.AreEqual(scaledTotal, simulated + pending, .00001);
                Assert.AreEqual(total, unscaledSimulated + unscaledPending, .0001);
            }
        }
        [Test] public void DisabledStaggerUsesCommonFrames()
        {
            var a = new EffectUpdateClock(); var b = new EffectUpdateClock();
            for (int frame = 0; frame < 20; ++frame)
                Assert.AreEqual(a.AdvanceScheduled(.01f, .02f, frame, 50, 0, false, out _, out _),
                    b.AdvanceScheduled(.01f, .02f, frame, 50, 0, false, out _, out _));
        }
        [Test] public void ForcedRefreshConsumesPendingDeltaOnlyOnce()
        {
            var clock = new EffectUpdateClock();
            clock.AdvanceScheduled(0, 0, 0, 10, 0, false, out _, out _);
            Assert.IsFalse(clock.AdvanceScheduled(.01f, .02f, 1, 10, 0, false, out _, out _));
            Assert.IsTrue(clock.AdvanceScheduled(.01f, .02f, 2, 10, 0, true, out float delta, out _));
            Assert.AreEqual(.02f, delta, .00001f);
            Assert.IsFalse(clock.AdvanceScheduled(0, 0, 2, 10, 0, false, out _, out _));
            clock.AdvanceEveryFrame(0, 0, out delta, out _); Assert.AreEqual(0, delta);
        }
        [Test] public void PercentageAndPhaseChangesDrainPendingSimulationTime()
        {
            var clock = new EffectUpdateClock();
            clock.AdvanceScheduled(0, 0, 0, 25, 0, false, out _, out _);
            Assert.IsFalse(clock.AdvanceScheduled(.01f, .02f, 1, 25, 0, false, out _, out _));
            Assert.IsTrue(clock.AdvanceScheduled(.01f, .02f, 2, 50, 0, false, out float delta, out _));
            Assert.AreEqual(.02f, delta, .00001);
            Assert.IsTrue(clock.AdvanceScheduled(.01f, .02f, 3, 50, .5, false, out delta, out _));
            Assert.AreEqual(.01f, delta, .00001);
        }
    }
}
