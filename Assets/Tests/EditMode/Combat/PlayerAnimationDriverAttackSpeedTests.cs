using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// PlayerAnimationDriver の攻撃速度オーバーライド機能の単体・契約テストです。
    /// </summary>
    public sealed class PlayerAnimationDriverAttackSpeedTests
    {
        private GameObject root;
        private Animator animator;
        private PlayerAnimationDriver driver;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("PlayerRoot");
            var child = new GameObject("ModelRoot");
            child.transform.SetParent(root.transform, false);
            animator = child.AddComponent<Animator>();
            driver = root.AddComponent<PlayerAnimationDriver>();
            driver.Construct(animator);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AttackSpeedOverrideControlsAnimatorSpeedAndClearsProperly()
        {
            driver.SetAttackSpeedMultiplier(1.8f);
            Assert.That(driver.IsAttackSpeedOverridden, Is.True);
            Assert.That(driver.CurrentAttackSpeedMultiplier, Is.EqualTo(1.8f).Within(0.01f));
            Assert.That(animator.speed, Is.EqualTo(1.8f).Within(0.01f));

            driver.ClearAttackSpeedMultiplier();
            Assert.That(driver.IsAttackSpeedOverridden, Is.False);
            Assert.That(driver.CurrentAttackSpeedMultiplier, Is.EqualTo(1.0f).Within(0.01f));
        }
    }
}
