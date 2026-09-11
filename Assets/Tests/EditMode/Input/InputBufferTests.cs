using NUnit.Framework;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class InputBufferTests
    {
        [Test]
        public void ActionIsBufferedAndConsumableWithinDuration()
        {
            var buffer = new InputBuffer(0.25f);
            buffer.BufferAction(InputBuffer.ActionAttack, 10.0);

            Assert.That(buffer.HasBufferedAction(InputBuffer.ActionAttack, 10.1), Is.True);
            Assert.That(buffer.ConsumeAction(InputBuffer.ActionAttack, 10.1), Is.True);
            Assert.That(buffer.HasBufferedAction(InputBuffer.ActionAttack, 10.1), Is.False);
        }

        [Test]
        public void ExpiredActionIsNotConsumable()
        {
            var buffer = new InputBuffer(0.25f);
            buffer.BufferAction(InputBuffer.ActionAttack, 10.0);

            // 0.3s later > 0.25s duration
            Assert.That(buffer.HasBufferedAction(InputBuffer.ActionAttack, 10.3), Is.False);
            Assert.That(buffer.ConsumeAction(InputBuffer.ActionAttack, 10.3), Is.False);
        }

        [Test]
        public void ClearRemovesAllBufferedActions()
        {
            var buffer = new InputBuffer(0.25f);
            buffer.BufferAction(InputBuffer.ActionAttack, 10.0);
            buffer.Clear();

            Assert.That(buffer.HasBufferedAction(InputBuffer.ActionAttack, 10.05), Is.False);
        }

        [Test]
        public void CustomMaxAgeOverridesDefaultDuration()
        {
            var buffer = new InputBuffer(0.25f);
            buffer.BufferAction(InputBuffer.ActionAttack, 10.0);

            // With custom maxAge 0.1s, 10.15 is expired
            Assert.That(buffer.HasBufferedAction(InputBuffer.ActionAttack, 10.15, 0.1f), Is.False);
            // With custom maxAge 0.5s, 10.35 is still valid
            Assert.That(buffer.HasBufferedAction(InputBuffer.ActionAttack, 10.35, 0.5f), Is.True);
        }
    }
}
