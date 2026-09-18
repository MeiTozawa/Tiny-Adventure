using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatTimeSlowControllerTests
    {
        private GameObject controllerGo;
        private CombatTimeSlowController controller;
        private StubUnscaledTimeSource timeSource;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatTimeSlowController");
            controller = controllerGo.AddComponent<CombatTimeSlowController>();
            timeSource = new StubUnscaledTimeSource { CurrentTime = 100.0 };
            controller.ConfigureForTests(timeSource, 0.15f, 0.08f, 0.05f, 0.20f, 0.03f);
        }

        [TearDown]
        public void TearDown()
        {
            if (controller != null)
            {
                controller.ClearRuntimeState();
            }
            Time.timeScale = 1f;

            if (controllerGo != null)
            {
                Object.DestroyImmediate(controllerGo);
            }
        }

        private static CombatFeedbackRequest CreateRequest(CombatHitType hitType, bool isPlayerAttack)
        {
            return new CombatFeedbackRequest(
                hitType,
                null,
                null,
                default,
                Vector3.zero,
                Vector3.forward,
                isPlayerAttack,
                !isPlayerAttack,
                default);
        }

        [Test]
        public void NormalHit_SetsTimeScaleToConfiguredNormalValue_AndRestoresAfterDuration()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: true);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));

            // 推進到 100.04：仍處於減速持續期
            timeSource.CurrentTime = 100.04;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));

            // 推進到 100.095：進入平滑恢復過渡期（0.08 ~ 0.11）
            timeSource.CurrentTime = 100.095;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.GreaterThan(0.15f));
            Assert.That(Time.timeScale, Is.LessThan(1.0f));

            // 推進到 100.12：完全恢復結束
            timeSource.CurrentTime = 100.12;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void LethalHit_SetsTimeScaleToConfiguredLethalValue_AndRestoresAfterDuration()
        {
            var req = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 推進到 100.15：仍在致死減速期
            timeSource.CurrentTime = 100.15;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 推進到 100.25：完全恢復結束
            timeSource.CurrentTime = 100.25;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void NonPlayerAttack_DoesNotTriggerTimeSlow()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: false);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void ConsecutiveHits_ExtendDurationSafely_WithoutExceedingMaximum()
        {
            var req1 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true);
            controller.Play(req1);

            timeSource.CurrentTime = 100.05;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);

            // 再次命中，延長持續時間
            var req2 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true);
            controller.Play(req2);

            // 在 100.10（超越初次 100.08）時依然活躍
            timeSource.CurrentTime = 100.10;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);

            // 推進到第二次命中超時後完全恢復
            timeSource.CurrentTime = 100.20;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void ClearRuntimeState_And_OnDisable_ForcefullyRestoresTimeScaleToOne()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: true);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.LessThan(1.0f));

            controller.ClearRuntimeState();

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }
    }
}
