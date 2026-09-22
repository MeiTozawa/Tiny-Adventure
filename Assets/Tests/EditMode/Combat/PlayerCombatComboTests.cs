using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// PlayerCombatController の3段コンボ進行・タイマーリセット・段別Kinetics適用のテストです。
    /// </summary>
    public sealed class PlayerCombatComboTests
    {
        private GameObject player;
        private PlayerCombatController combat;
        private PlayerController playerController;
        private PlayerAnimationDriver animationDriver;
        private Animator animator;
        private ComboAttackConfig comboConfig;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Knight_ComboTest");
            player.AddComponent<CharacterController>();
            player.AddComponent<InputReader>();
            player.AddComponent<CombatantMarker>();
            playerController = player.AddComponent<PlayerController>();

            var animatorObject = new GameObject("Animator");
            animatorObject.transform.SetParent(player.transform, false);
            animator = animatorObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CharacterCombat.controller");

            animationDriver = player.AddComponent<PlayerAnimationDriver>();

            var hitboxObject = new GameObject("SwordHitbox");
            hitboxObject.transform.SetParent(player.transform, false);
            hitboxObject.AddComponent<BoxCollider>().isTrigger = true;
            hitboxObject.AddComponent<CombatHitbox>();

            combat = player.AddComponent<PlayerCombatController>();
            combat.SetFallbackGameplayState(GameplayState.Running);

            comboConfig = ScriptableObject.CreateInstance<ComboAttackConfig>();
            comboConfig.ComboResetTimeout = 0.45f;
            comboConfig.SetSteps(new[]
            {
                new AttackConfigStep
                {
                    Damage = 20f,
                    Range = 2.2f,
                    SpeedMultiplier = 1.7f,
                    WindowCloseNormalizedTime = 0.50f,
                    CompletionNormalizedTime = 0.65f
                },
                new AttackConfigStep
                {
                    Damage = 25f,
                    Range = 2.2f,
                    SpeedMultiplier = 1.6f,
                    WindowCloseNormalizedTime = 0.55f,
                    CompletionNormalizedTime = 0.70f
                },
                new AttackConfigStep
                {
                    Damage = 40f,
                    Range = 2.8f,
                    SpeedMultiplier = 1.5f,
                    WindowCloseNormalizedTime = 0.60f,
                    CompletionNormalizedTime = 0.75f
                }
            });
            combat.AttackConfig = comboConfig;

            combat.SetDependencies(
                player.GetComponent<InputReader>(),
                animationDriver,
                animator,
                player.GetComponent<CombatantMarker>(),
                null,
                hitboxObject.GetComponent<CombatHitbox>(),
                null,
                playerController);
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null)
            {
                player.GetComponent<InputReader>()?.Dispose();
                Object.DestroyImmediate(player);
            }

            if (comboConfig != null)
            {
                Object.DestroyImmediate(comboConfig);
            }
        }

        [Test]
        public void InitialAttack_StartsAtComboIndexZero()
        {
            Assert.That(combat.ComboIndex, Is.EqualTo(0));
            bool started = combat.TryStartAttack(out string diagnostic);
            Assert.That(started, Is.True, diagnostic);
            Assert.That(combat.ComboIndex, Is.EqualTo(0));
            Assert.That(combat.AttackDamage, Is.EqualTo(20f));
            Assert.That(animator.GetInteger("ComboIndex"), Is.EqualTo(0));
        }

        [Test]
        public void ConsecutiveAttacks_AdvanceComboIndexCyclically()
        {
            // 1段目 (横薙ぎ)
            Assert.That(combat.TryStartAttack(out _), Is.True);
            Assert.That(combat.ComboIndex, Is.EqualTo(0));
            Assert.That(combat.AttackDamage, Is.EqualTo(20f));
            combat.CompleteAttack();

            // 完了後は2段目待機
            Assert.That(combat.ComboIndex, Is.EqualTo(1));

            // 2段目 (縦斬り)
            Assert.That(combat.TryStartAttack(out _), Is.True);
            Assert.That(combat.AttackDamage, Is.EqualTo(25f));
            Assert.That(animator.GetInteger("ComboIndex"), Is.EqualTo(1));
            combat.CompleteAttack();

            // 完了後は3段目待机
            Assert.That(combat.ComboIndex, Is.EqualTo(2));

            // 3段目 (突刺フィニッシャー)
            Assert.That(combat.TryStartAttack(out _), Is.True);
            Assert.That(combat.AttackDamage, Is.EqualTo(40f));
            Assert.That(animator.GetInteger("ComboIndex"), Is.EqualTo(2));
            combat.CompleteAttack();

            // フィニッシャー完了後は0に循環
            Assert.That(combat.ComboIndex, Is.EqualTo(0));

            // 循環後、初段（0）へ再遷移可能（無限ループ）
            Assert.That(combat.TryStartAttack(out _), Is.True);
            Assert.That(combat.AttackDamage, Is.EqualTo(20f));
            Assert.That(animator.GetInteger("ComboIndex"), Is.EqualTo(0));
            combat.CompleteAttack();
            Assert.That(combat.ComboIndex, Is.EqualTo(1));
        }

        [Test]
        public void CancelAttack_ResetsComboIndexToZero()
        {
            Assert.That(combat.TryStartAttack(out _), Is.True);
            combat.CompleteAttack();
            Assert.That(combat.ComboIndex, Is.EqualTo(1));

            combat.CancelAttack();
            Assert.That(combat.ComboIndex, Is.EqualTo(0));
            Assert.That(animator.GetInteger("ComboIndex"), Is.EqualTo(0));
        }

        [Test]
        public void ComboExpiration_ResetsComboIndexToZero()
        {
            Assert.That(combat.TryStartAttack(out _), Is.True);
            combat.CompleteAttack();
            Assert.That(combat.ComboIndex, Is.EqualTo(1));

            combat.ResetCombo();
            Assert.That(combat.ComboIndex, Is.EqualTo(0));
        }
    }
}
