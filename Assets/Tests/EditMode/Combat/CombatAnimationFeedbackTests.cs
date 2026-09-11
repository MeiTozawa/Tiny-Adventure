using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatAnimationFeedbackTests
    {
        private GameObject feedbackGo;
        private CombatAnimationFeedback feedback;
        private GameObject targetGo;
        private CombatantMarker target;
        private GameObject sourceGo;
        private CombatantMarker source;

        [SetUp]
        public void SetUp()
        {
            feedbackGo = new GameObject("CombatAnimationFeedback");
            feedback = feedbackGo.AddComponent<CombatAnimationFeedback>();

            sourceGo = new GameObject("Source");
            source = sourceGo.AddComponent<CombatantMarker>();
            source.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");

            targetGo = new GameObject("Target");
            target = targetGo.AddComponent<CombatantMarker>();
            target.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
        }

        [TearDown]
        public void TearDown()
        {
            if (feedbackGo != null) Object.DestroyImmediate(feedbackGo);
            if (targetGo != null) Object.DestroyImmediate(targetGo);
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
        }

        [Test]
        public void NormalHit_WithoutAnimator_ReportsDiagnosticAndDoesNotThrow()
        {
            string reportedDiag = string.Empty;
            feedback.DiagnosticReported += diag => reportedDiag = diag;

            var key = new FeedbackDeduplicationKey(source, target, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                source,
                target,
                default,
                target.transform.position,
                Vector3.forward,
                true,
                false,
                key);

            Assert.DoesNotThrow(() => feedback.Play(request));
            StringAssert.Contains("未找到", reportedDiag, "应该报告未找到动画驱动器的中文诊断。");
        }

        [Test]
        public void LethalHit_DoesNotTriggerHitAnimation()
        {
            string reportedDiag = string.Empty;
            feedback.DiagnosticReported += diag => reportedDiag = diag;

            var key = new FeedbackDeduplicationKey(source, target, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Lethal,
                source,
                target,
                default,
                target.transform.position,
                Vector3.forward,
                true,
                false,
                key);

            Assert.DoesNotThrow(() => feedback.Play(request));
            Assert.That(string.IsNullOrEmpty(reportedDiag), Is.True, "致死命中不应触发普通受击逻辑或报错。");
        }

        [Test]
        public void NullTarget_HandledGracefully()
        {
            string reportedDiag = string.Empty;
            feedback.DiagnosticReported += diag => reportedDiag = diag;

            var key = new FeedbackDeduplicationKey(source, null, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                source,
                null,
                default,
                Vector3.zero,
                Vector3.forward,
                true,
                false,
                key);

            Assert.DoesNotThrow(() => feedback.Play(request));
            StringAssert.Contains("null", reportedDiag);
        }

        [Test]
        public void ClearRuntimeState_ClearsDiagnostic()
        {
            feedback.ClearRuntimeState();
            Assert.That(string.IsNullOrEmpty(feedback.LastDiagnostic), Is.True);
        }
    }
}
