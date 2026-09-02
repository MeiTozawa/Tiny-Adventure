using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 左クリック攻撃入口の系列一意性、終局門禁、参照診断を編集モードで検証します。
    /// </summary>
    public sealed class PlayerCombatControllerContractTests
    {
        private GameObject player;
        private PlayerCombatController combat;

        [SetUp]
        public void セットアップ()
        {
            player = new GameObject("Knight攻撃検証");
            player.AddComponent<CharacterController>();
            player.AddComponent<InputReader>();
            player.AddComponent<CombatantMarker>();
            player.AddComponent<PlayerController>();
            var animatorObject = new GameObject("Animator");
            animatorObject.transform.SetParent(player.transform, false);
            var animator = animatorObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CharacterCombat.controller");
            player.AddComponent<PlayerAnimationDriver>();
            var hitboxObject = new GameObject("SwordHitbox");
            hitboxObject.transform.SetParent(player.transform, false);
            hitboxObject.AddComponent<BoxCollider>().isTrigger = true;
            hitboxObject.AddComponent<CombatHitbox>();
            combat = player.AddComponent<PlayerCombatController>();
            combat.SetFallbackGameplayState(GameplayState.Running);
            combat.ConfigureForTests(
                player.GetComponent<InputReader>(),
                player.GetComponent<PlayerAnimationDriver>(),
                animator,
                player.GetComponent<CombatantMarker>(),
                null,
                hitboxObject.GetComponent<CombatHitbox>());
        }

        [TearDown]
        public void 後始末()
        {
            if (player != null)
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void AttackPressed一回は一つの攻撃系列とAttackTriggerだけを発行する()
        {
            bool started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));

            Assert.That(started, Is.True, combat.LastDiagnostic);
            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1));
            Assert.That(combat.IsAttacking, Is.True);
        }

        [Test]
        public void 攻撃中の再入力は二重系列を作らない()
        {
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.True);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);

            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1));
        }

        [Test]
        public void 終局状態と死亡状態では攻撃を開始しない()
        {
            combat.SetFallbackGameplayState(GameplayState.Victory);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(0));

            combat.SetFallbackGameplayState(GameplayState.Running);
            combat.SetDead(true);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(0));
        }

        [Test]
        public void KnightにPlayerCombatControllerがない場合は日本語診断を返す()
        {
            var invalidObject = new GameObject("PlayerCombatController欠落Knight");
            try
            {
                LogAssert.Expect(LogType.Error, "[PlayerCombatController診断] KnightにPlayerCombatControllerがありません。");
                Assert.That(PlayerCombatController.ValidateKnightObject(invalidObject, out IReadOnlyList<string> diagnostics), Is.False);
                StringAssert.Contains("KnightにPlayerCombatControllerがありません。", string.Join("\n", diagnostics));
            }
            finally
            {
                Object.DestroyImmediate(invalidObject);
            }
        }

        [Test]
        public void 必須参照不足は指定された日本語診断を返す()
        {
            var invalidObject = new GameObject("参照不足Knight");
            try
            {
                invalidObject.SetActive(false);
                var invalidCombat = invalidObject.AddComponent<PlayerCombatController>();
                LogAssert.Expect(LogType.Error, "[PlayerCombatController診断] PlayerCombatControllerのInputReader参照がありません。");
                Assert.That(invalidCombat.ValidateRequiredReferences(out IReadOnlyList<string> diagnostics), Is.False);
                StringAssert.Contains("PlayerCombatControllerのInputReader参照がありません。", string.Join("\n", diagnostics));
                StringAssert.Contains("PlayerCombatControllerのAnimator参照がありません。", string.Join("\n", diagnostics));
                StringAssert.Contains("PlayerCombatControllerのGameFlow参照がありません。", string.Join("\n", diagnostics));
            }
            finally
            {
                Object.DestroyImmediate(invalidObject);
            }
        }
    }
}
