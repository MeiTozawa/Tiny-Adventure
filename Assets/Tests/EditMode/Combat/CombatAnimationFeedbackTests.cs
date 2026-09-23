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
            source.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            targetGo = new GameObject("Target");
            target = targetGo.AddComponent<CombatantMarker>();
            target.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
        }

        [TearDown]
        public void TearDown()
        {
            if (feedbackGo != null) Object.DestroyImmediate(feedbackGo);
            if (targetGo != null) Object.DestroyImmediate(targetGo);
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
        }

        [Test]
        public void NormalHit_WithoutAnimator_DoesNotThrow()
        {
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
        }

        [Test]
        public void LethalHit_DoesNotTriggerHitAnimation()
        {
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
        }

        [Test]
        public void NullTarget_HandledGracefully()
        {
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
        }

        [Test]
        public void ClearRuntimeState_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => feedback.ClearRuntimeState());
        }
    }
}
