using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class HitStopControllerPropertiesTests
    {
        private GameObject controllerGo;
        private HitStopController controller;
        private StubUnscaledTimeSource timeSource;
        private CombatFeedbackProfile profile;
        private RecordingHitStopParticipant participant;

        private static CombatFeedbackRequest CreateRequest(CombatHitType hitType)
        {
            return new CombatFeedbackRequest(
                hitType,
                null,
                null,
                default,
                Vector3.zero,
                Vector3.forward,
                true,
                false,
                default);
        }

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("HitStopController");
            controller = controllerGo.AddComponent<HitStopController>();

            timeSource = new StubUnscaledTimeSource { CurrentTime = 0.0 };
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            participant = new RecordingHitStopParticipant();

            controller.ConfigureForTests(timeSource, null, profile);
            controller.RegisterParticipant(participant);
        }

        [TearDown]
        public void TearDown()
        {
            if (controllerGo != null) Object.DestroyImmediate(controllerGo);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void Property4_HitStopDurationMerging_NeverExceedsMaximum()
        {
            float maxSeconds = profile.HitStop.maximumSeconds; // 0.12s
            Assert.That(maxSeconds, Is.EqualTo(0.12f));

            // t = 0: 普通命中 0.04s
            controller.Play(CreateRequest(CombatHitType.Normal));
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));

            // t = 0.01: 连续多次致死命中
            for (int i = 0; i < 10; i++)
            {
                timeSource.CurrentTime = 0.01 + i * 0.01;
                controller.Play(CreateRequest(CombatHitType.Lethal));

                // 任何时刻计算的总截止时间不得超过 startedAt (0) + maxSeconds (0.12)
                double maxAllowedRemaining = 0.12 - timeSource.CurrentTime;
                Assert.That(controller.RemainingUnscaledSeconds, Is.LessThanOrEqualTo(maxAllowedRemaining + 0.001f));
            }

            // 到达 0.12s 截止点
            timeSource.CurrentTime = 0.12;
            controller.Tick();
            Assert.That(controller.IsActive, Is.False, "达到最大上限 0.12s 后 HitStop 必须完全结束。");
            Assert.That(participant.EndTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void Property4_TimeScaleIntegrityPreservedUnderAllConditions()
        {
            float expectedTimeScale = Time.timeScale;

            // 1. 触发普通命中
            controller.Play(CreateRequest(CombatHitType.Normal));
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));

            // 2. 步进未缩放时间
            timeSource.CurrentTime += 0.02;
            controller.Tick();
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));

            // 3. 叠加致死命中
            controller.Play(CreateRequest(CombatHitType.Lethal));
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));

            // 4. 清理运行时
            controller.ClearRuntimeState();
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));
        }
    }
}
