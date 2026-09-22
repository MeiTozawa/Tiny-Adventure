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

            controller.SetDependencies(timeSource, null, profile);
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

            // t = 0: 通常ヒット 0.04s
            controller.Play(CreateRequest(CombatHitType.Normal));
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));

            // t = 0.01: 致命ヒットを連続して複数回トリガー
            for (int i = 0; i < 10; i++)
            {
                timeSource.CurrentTime = 0.01 + i * 0.01;
                controller.Play(CreateRequest(CombatHitType.Lethal));

                // いかなる時点でも計算された総終了時刻が startedAt (0) + maxSeconds (0.12) を超えてはならない
                double maxAllowedRemaining = 0.12 - timeSource.CurrentTime;
                Assert.That(controller.RemainingUnscaledSeconds, Is.LessThanOrEqualTo(maxAllowedRemaining + 0.001f));
            }

            // 0.12s の期限に到達
            timeSource.CurrentTime = 0.12;
            controller.Tick();
            Assert.That(controller.IsActive, Is.False, "最大上限 0.12s 到達後は HitStop が完全に終了する必要があります。");
            Assert.That(participant.EndTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void Property4_TimeScaleIntegrityPreservedUnderAllConditions()
        {
            float expectedTimeScale = Time.timeScale;

            // 1. 通常ヒットをトリガー
            controller.Play(CreateRequest(CombatHitType.Normal));
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));

            // 2. Unscaled 時間を進める
            timeSource.CurrentTime += 0.02;
            controller.Tick();
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));

            // 3. 致命ヒットを重畳
            controller.Play(CreateRequest(CombatHitType.Lethal));
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));

            // 4. ランタイム状態をクリア
            controller.ClearRuntimeState();
            Assert.That(Time.timeScale, Is.EqualTo(expectedTimeScale));
        }
    }
}
