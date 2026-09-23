using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// PlayerCombatController の踏み込み突進と攻撃速度オーバーライド連携のテストです。
    /// </summary>
    public sealed class PlayerCombatControllerKineticsTests
    {
        private GameObject player;
        private PlayerCombatController combat;
        private PlayerController playerController;
        private PlayerAnimationDriver animationDriver;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Knight_CombatKineticsTest");
            player.AddComponent<CharacterController>();
            player.AddComponent<InputReader>();
            player.AddComponent<CombatantMarker>();
            playerController = player.AddComponent<PlayerController>();
            var animatorObject = new GameObject("Animator");
            animatorObject.transform.SetParent(player.transform, false);
            var animator = animatorObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CharacterCombat.controller");
            animationDriver = player.AddComponent<PlayerAnimationDriver>();
            var hitboxObject = new GameObject("SwordHitbox");
            hitboxObject.transform.SetParent(player.transform, false);
            hitboxObject.AddComponent<BoxCollider>().isTrigger = true;
            hitboxObject.AddComponent<CombatHitbox>();
            combat = player.AddComponent<PlayerCombatController>();
            combat.SetFallbackGameplayState(GameplayState.Running);
            combat.Construct(
                damageService: null,
                gameFlowController: null,
                inputReader: player.GetComponent<InputReader>(),
                animationDriver: animationDriver,
                targetAnimator: animator,
                combatantMarker: player.GetComponent<CombatantMarker>(),
                swordHitbox: hitboxObject.GetComponent<CombatHitbox>(),
                playerController: playerController);
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null)
            {
                var inputReader = player.GetComponent<InputReader>();
                inputReader?.Dispose();
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void AttackInitiationDoesNotTriggerLungeAndAppliesSpeedOverride()
        {
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)).IsOk, Is.True);
            Assert.That(playerController.IsLunging, Is.False, "第一人称攻撃時にPlayerControllerの踏み込みが発生してはいけません（カメラ前移・めり込み防止）。");
            Assert.That(animationDriver.IsAttackSpeedOverridden, Is.True, "攻撃開始時にアニメーション速度オーバーライドが起動していません。");

            combat.AnimationEventCompleteAttack();
            Assert.That(animationDriver.IsAttackSpeedOverridden, Is.False, "攻撃完了後にアニメーション速度オーバーライドが復帰していません。");
        }

        [Test]
        public void CancelAttackResetsSpeedOverrideAndMaintainsNoLunge()
        {
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)).IsOk, Is.True);
            Assert.That(playerController.IsLunging, Is.False);
            Assert.That(animationDriver.IsAttackSpeedOverridden, Is.True);

            combat.CancelAttack();
            Assert.That(playerController.IsLunging, Is.False);
            Assert.That(animationDriver.IsAttackSpeedOverridden, Is.False, "攻撃キャンセル後にアニメーション速度オーバーライドが復帰していません。");
        }
    }
}
