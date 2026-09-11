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

            controller.ConfigureForTests(timeSource, registry, profile);
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

            // 前进到 0.02 秒
            timeSource.CurrentTime += 0.02;
            controller.Tick();
            Assert.That(controller.IsActive, Is.True);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.02f).Within(0.001f));

            // 前进到 0.04 秒
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

            // 前进到 0.08 秒
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

            // t = 100.0: 普通命中 0.04s，预计到 100.04
            controller.Play(normalRequest);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));

            // t = 100.02: 命中叠加致死命中 (0.08s)，原本 100.02 + 0.08 = 100.10，未超过 100.0 + 0.12 = 100.12
            timeSource.CurrentTime = 100.02;
            controller.Play(lethalRequest);
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.08f).Within(0.001f));

            // t = 100.08: 再次叠加致死命中 (0.08s)，100.08 + 0.08 = 100.16，但受上限 100.0 + 0.12 = 100.12 截断
            timeSource.CurrentTime = 100.08;
            controller.Play(lethalRequest);
            // 剩余时间应被截断为 100.12 - 100.08 = 0.04s
            Assert.That(controller.RemainingUnscaledSeconds, Is.EqualTo(0.04f).Within(0.001f));

            // 推进到 100.11：仍然在顿挫中
            timeSource.CurrentTime = 100.11;
            controller.Tick();
            Assert.That(controller.IsActive, Is.True);

            // 推进到 100.12：顿挫结束
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

            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale), "Hit Stop 触发期间绝对严禁修改 Time.timeScale。");

            timeSource.CurrentTime += 0.08;
            controller.Tick();

            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale), "Hit Stop 恢复后绝对严禁修改 Time.timeScale。");
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

            // 另一个正常参与者仍然正常接收
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
