using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// ComboAttackConfig の多段攻撃設定・値検証・安全アクセスのテストです。
    /// </summary>
    public sealed class ComboAttackConfigTests
    {
        private ComboAttackConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ComboAttackConfig>();
            config.ComboResetTimeout = 0.45f;
            config.SetSteps(new[]
            {
                new AttackConfigStep
                {
                    Damage = 20f,
                    Range = 2.2f,
                    SpeedMultiplier = 1.7f,
                    WindowCloseNormalizedTime = 0.50f,
                    CompletionNormalizedTime = 0.65f
                },
                new AttackConfigStep
                {
                    Damage = 25f,
                    Range = 2.2f,
                    SpeedMultiplier = 1.6f,
                    WindowCloseNormalizedTime = 0.55f,
                    CompletionNormalizedTime = 0.70f
                },
                new AttackConfigStep
                {
                    Damage = 40f,
                    Range = 2.8f,
                    SpeedMultiplier = 1.5f,
                    WindowCloseNormalizedTime = 0.60f,
                    CompletionNormalizedTime = 0.75f
                }
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (config != null)
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void StepCount_ReturnsThree()
        {
            Assert.That(config.StepCount, Is.EqualTo(3), "コンボ段数が3ではありません。");
        }

        [Test]
        public void GetStep_ReturnsCorrectConfigPerStep()
        {
            var step0 = config.GetStep(0);
            Assert.That(step0.Damage, Is.EqualTo(20f));
            Assert.That(step0.Range, Is.EqualTo(2.2f));
            Assert.That(step0.SpeedMultiplier, Is.EqualTo(1.7f));

            var step1 = config.GetStep(1);
            Assert.That(step1.Damage, Is.EqualTo(25f));
            Assert.That(step1.Range, Is.EqualTo(2.2f));
            Assert.That(step1.SpeedMultiplier, Is.EqualTo(1.6f));

            var step2 = config.GetStep(2);
            Assert.That(step2.Damage, Is.EqualTo(40f));
            Assert.That(step2.Range, Is.EqualTo(2.8f));
            Assert.That(step2.SpeedMultiplier, Is.EqualTo(1.5f));
        }

        [Test]
        public void GetStep_OutOfBounds_ReturnsClampedStep()
        {
            var underflow = config.GetStep(-1);
            Assert.That(underflow.Damage, Is.EqualTo(20f), "負のインデックスで第0段にクランプされていません。");

            var overflow = config.GetStep(99);
            Assert.That(overflow.Damage, Is.EqualTo(40f), "超過インデックスで最終段にクランプされていません。");
        }

        [Test]
        public void ComboResetTimeout_ClampedToSensibleRange()
        {
            config.ComboResetTimeout = 0.45f;
            Assert.That(config.ComboResetTimeout, Is.EqualTo(0.45f));

            config.ComboResetTimeout = -1f;
            Assert.That(config.ComboResetTimeout, Is.GreaterThanOrEqualTo(0.1f));
        }
    }
}
