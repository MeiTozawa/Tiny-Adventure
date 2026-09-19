using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class EnemyMotorKnockbackTests
    {
        private GameObject enemyGo;
        private EnemyMotor motor;

        [SetUp]
        public void SetUp()
        {
            enemyGo = new GameObject("TestEnemy");
            motor = enemyGo.AddComponent<EnemyMotor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (enemyGo != null)
            {
                Object.DestroyImmediate(enemyGo);
            }
        }

        [Test]
        public void ApplyKnockback_WithNullOrDisabledAgent_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                motor.ApplyKnockback(Vector3.forward, 0.15f);
                motor.ApplyKnockback(Vector3.zero, 0.15f);
                motor.ApplyKnockback(Vector3.back, -0.1f);
            });
        }
    }
}
