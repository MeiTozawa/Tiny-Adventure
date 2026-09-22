using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class HitStopControllerTests
    {
        private GameObject controllerGo;
        private HitStopController controller;
        private StubUnscaledTimeSource timeSource;
        private CombatFeedbackProfile profile;
        private RecordingHitStopParticipant participant;
        private StubHitStopParticipantRegistry registry;

        private static CombatFeedbackRequest CreateRequest(
            CombatHitType hitType,
            Vector3 direction,
            bool isPlayerTarget = false,
            bool isPlayerAttack = true)
        {
            return new CombatFeedbackRequest(
                hitType,
                null,
                null,
                default,
                Vector3.zero,
                direction,
                isPlayerAttack,
                isPlayerTarget,
                default);
        }

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("HitStopController");
            controller = controllerGo.AddComponent<HitStopController>();

            timeSource = new StubUnscaledTimeSource { CurrentTime = 100.0 };
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            participant = new RecordingHitStopParticipant();
            registry = new StubHitStopParticipantRegistry();

            controller.SetDependencies(timeSource, registry, profile);
            controller.RegisterParticipant(participant);
        }

        [TearDown]
        public void TearDown()
        {
            if (controllerGo != null)
            {
                Object.DestroyImmediate(controllerGo);
            }

            if (profile != null)
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PlayNormalHit_SetsDurationCorrectly()
        {
            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);

            controller.Play(request);

            Assert.That(controller.IsActive, Is.True);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));
            Assert.That(participant.BeginTokens.Count, Is.EqualTo(1));
            Assert.That(participant.EndTokens.Count, Is.EqualTo(0));

            // 0.02秒まで進行
            timeSource.CurrentTime += 0.02;
            controller.Tick();
            Assert.That(controller.IsActive, Is.True);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.02f).Within(0.001f));

            // 0.04秒まで進行
            timeSource.CurrentTime += 0.02;
            controller.Tick();
            Assert.That(controller.IsActive, Is.False);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0f));
            Assert.That(participant.EndTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlayLethalHit_SetsDurationCorrectly()
        {
            var request = CreateRequest(CombatHitType.Lethal, Vector3.forward);

            controller.Play(request);

            Assert.That(controller.IsActive, Is.True);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.08f).Within(0.001f));

            // 0.08 秒まで進行
            timeSource.CurrentTime += 0.08;
            controller.Tick();
            Assert.That(controller.IsActive, Is.False);
            Assert.That(participant.EndTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConsecutiveHits_MergeDurationWithoutExceedingMaximum()
        {
            var normalRequest = CreateRequest(CombatHitType.Normal, Vector3.forward);
            var lethalRequest = CreateRequest(CombatHitType.Lethal, Vector3.forward);

            // t = 100.0: 通常ヒット 0.04s、終了予定は 100.04
            controller.Play(normalRequest);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));

            // t = 100.02: 致命ヒット (0.08s) を重畳。本来は 100.02 + 0.08 = 100.10 で上限 100.0 + 0.12 = 100.12 を超えない
            timeSource.CurrentTime = 100.02;
            controller.Play(lethalRequest);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.08f).Within(0.001f));

            // t = 100.08: 再度致命ヒット (0.08s) を重畳。100.08 + 0.08 = 100.16 だが、上限 100.0 + 0.12 = 100.12 で切り捨てられる
            timeSource.CurrentTime = 100.08;
            controller.Play(lethalRequest);
            // 残り時間は 100.12 - 100.08 = 0.04s に切り捨てられる
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));

            // 100.11 まで進行：依然としてヒットストップ中
            timeSource.CurrentTime = 100.11;
            controller.Tick();
            Assert.That(controller.IsActive, Is.True);

            // 100.12 まで進行：ヒットストップ終了
            timeSource.CurrentTime = 100.12;
            controller.Tick();
            Assert.That(controller.IsActive, Is.False);
        }

        [Test]
        public void NeverModifiesTimeScale()
        {
            float initialTimeScale = Time.timeScale;

            var request = CreateRequest(CombatHitType.Lethal, Vector3.forward);
            controller.Play(request);

            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale), "Hit Stop 発火中に Time.timeScale を変更することは厳禁です。");

            timeSource.CurrentTime += 0.08;
            controller.Tick();

            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale), "Hit Stop 復帰後に Time.timeScale を変更することは厳禁です。");
        }

        [Test]
        public void ClearRuntimeState_ImmediatelyRestoresParticipants()
        {
            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);
            controller.Play(request);

            Assert.That(controller.IsActive, Is.True);
            Assert.That(participant.BeginTokens.Count, Is.EqualTo(1));

            controller.ClearRuntimeState();

            Assert.That(controller.IsActive, Is.False);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0f));
            Assert.That(participant.EndTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void RegistryParticipant_ReceivesCallbacks()
        {
            var regParticipant = new RecordingHitStopParticipant();
            registry.List.Add(regParticipant);

            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);
            controller.Play(request);

            Assert.That(regParticipant.BeginTokens.Count, Is.EqualTo(1));

            timeSource.CurrentTime += 0.04;
            controller.Tick();

            Assert.That(regParticipant.EndTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void ParticipantException_DoesNotCrashController()
        {
            var faultingParticipant = new RecordingHitStopParticipant { ThrowOnBegin = true };
            controller.RegisterParticipant(faultingParticipant);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Hit Stop.*"));

            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);
            Assert.DoesNotThrow(() => controller.Play(request));

            // もう一方の正常な参加者は正常に受信
            Assert.That(participant.BeginTokens.Count, Is.EqualTo(1));
        }

        [Test]
        public void HitStopParticipantComponent_PausesAndRestoresAnimator()
        {
            var actorGo = new GameObject("Actor");
            var animator = actorGo.AddComponent<Animator>();
            var hitParticipant = actorGo.AddComponent<HitStopParticipant>();

            animator.speed = 1.25f;

            var token = new HitStopToken(1, 0, 0.04);
            hitParticipant.BeginHitStop(token);

            Assert.That(animator.speed, Is.EqualTo(0f));
            Assert.That(hitParticipant.IsPaused, Is.True);

            hitParticipant.EndHitStop(token);

            Assert.That(animator.speed, Is.EqualTo(1.25f));
            Assert.That(hitParticipant.IsPaused, Is.False);

            Object.DestroyImmediate(actorGo);
        }
    }
}
