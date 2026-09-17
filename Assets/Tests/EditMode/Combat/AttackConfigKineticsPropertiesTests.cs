using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// AttackConfigSO の速度感・踏み込みパラメータの初期値および範囲制約を検証するテストです。
    /// </summary>
    public sealed class AttackConfigKineticsPropertiesTests
    {
        [Test]
        public void NewKineticsParametersHaveSafeDefaultsAndClampProperly()
        {
            var config = ScriptableObject.CreateInstance<AttackConfigSO>();
            Assert.That(config.AttackSpeedMultiplier, Is.EqualTo(1.6f).Within(0.01f));
            Assert.That(config.AttackWindowCloseNormalizedTime, Is.EqualTo(0.55f).Within(0.01f));
            Assert.That(config.AttackCompletionNormalizedTime, Is.EqualTo(0.70f).Within(0.01f));

            config.AttackSpeedMultiplier = -1f;
            Assert.That(config.AttackSpeedMultiplier, Is.GreaterThanOrEqualTo(0.5f));
            config.AttackSpeedMultiplier = 10f;
            Assert.That(config.AttackSpeedMultiplier, Is.LessThanOrEqualTo(3.0f));
        }
    }
}
