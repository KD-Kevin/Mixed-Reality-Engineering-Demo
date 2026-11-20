using NUnit.Framework;

namespace EngineeringDemo.Tests
{
    public sealed class PistolTriggerTests
    {
        private const float Ready = 0.75f;
        private const float Fire = 0.5f;
        private const float ReleaseSeconds = 0.25f;

        [Test]
        public void FiresOncePerContinuousRelease()
        {
            var trigger = new PistolTrigger();
            var hand = new Hand();

            Assert.That(trigger.Update(hand, Fire, 0d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.Update(hand, Ready, 1d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.Update(hand, Ready, 1.125d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Waiting));
            Assert.That(trigger.Update(hand, Ready, 1.25d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Ready));

            Assert.That(trigger.Update(hand, 0.6f, 1.375d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.Update(hand, Fire, 1.5d, Ready, Fire, ReleaseSeconds), Is.True);
            Assert.That(trigger.Update(hand, Fire, 1.625d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.Update(hand, Ready, 2d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.Update(hand, Ready, 2.25d, Ready, Fire, ReleaseSeconds), Is.False);
            Assert.That(trigger.Update(hand, Fire, 2.5d, Ready, Fire, ReleaseSeconds), Is.True);
        }

        [TestCase(new object[] { null })]
        [TestCase(0.6f)]
        public void InterruptedReleaseStartsTheHoldOver(float? interruptedExtension)
        {
            var trigger = new PistolTrigger();
            var hand = new Hand();

            trigger.Update(hand, Ready, 0d, Ready, Fire, ReleaseSeconds);
            trigger.Update(hand, interruptedExtension, 0.125d, Ready, Fire, ReleaseSeconds);
            trigger.Update(hand, Ready, 0.25d, Ready, Fire, ReleaseSeconds);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Waiting));
            trigger.Update(hand, Ready, 0.5d, Ready, Fire, ReleaseSeconds);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Ready));
        }

        [Test]
        public void ChangingOrLosingHandClearsTheArmedTrigger()
        {
            var trigger = new PistolTrigger();
            var first = new Hand();
            var second = new Hand();

            trigger.Update(first, Ready, 0d, Ready, Fire, 0f);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Ready));
            Assert.That(trigger.Update(second, Fire, 1d, Ready, Fire, 0f), Is.False);
            Assert.That(trigger.Hand, Is.SameAs(second));
            trigger.Update(second, Ready, 2d, Ready, Fire, 0f);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Ready));

            Assert.That(trigger.Update(null, null, 3d, Ready, Fire, 0f), Is.False);
            Assert.That(trigger.Hand, Is.Null);
            Assert.That(trigger.State, Is.EqualTo(PistolTrigger.TriggerState.Waiting));
        }
    }
}
