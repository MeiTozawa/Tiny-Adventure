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
        public void 攻撃入力一回は一つの攻撃系列と攻撃トリガーだけを発行する()
        {
            int startedCount = 0;
            int startedSequenceId = 0;
            combat.AttackSequenceStarted += sequenceId =>
            {
                startedCount++;
                startedSequenceId = sequenceId;
            };

            bool started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));

            Assert.That(started, Is.True, combat.LastDiagnostic);
            Assert.That(startedCount, Is.EqualTo(1), "一回の攻撃入力で攻撃系列開始イベントが一つだけ発行されていません。");
            Assert.That(startedSequenceId, Is.EqualTo(1));
            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1));
            Assert.That(combat.IsAttacking, Is.True);
        }

        [Test]
        public void 連続10回の攻撃系列は完了後に次の攻撃を開始できる()
        {
            for (int sequenceId = 1; sequenceId <= 10; sequenceId++)
            {
                Assert.That(
                    combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)),
                    Is.True,
                    $"攻撃系列{sequenceId}を開始できません。診断: {combat.LastDiagnostic}");
                Assert.That(combat.LastAttackSequenceId, Is.EqualTo(sequenceId));
                Assert.That(combat.AnimationEventBeginAttackWindow(), Is.True, $"攻撃系列{sequenceId}のウィンドウを開けません。");
                Assert.That(combat.AnimationEventEndAttackWindow(), Is.True, $"攻撃系列{sequenceId}のウィンドウを閉じられません。");
                Assert.That(combat.AnimationEventCompleteAttack(), Is.True, $"攻撃系列{sequenceId}を完了できません。");
                Assert.That(combat.IsAttacking, Is.False, $"攻撃系列{sequenceId}完了後もIsAttackingが残っています。");
            }

            Assert.That(combat.AttackTriggerCount, Is.EqualTo(10));
        }

        [Test]
        public void 攻撃中の再入力は二重系列を作らない()
        {
            int startedCount = 0;
            combat.AttackSequenceStarted += _ => startedCount++;

            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.True);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);

            Assert.That(startedCount, Is.EqualTo(1), "攻撃中の再入力で二つ目の攻撃系列が開始されました。");
            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1));
            StringAssert.Contains("攻撃系列1が進行中のため、再入力を無視しました。", combat.LastDiagnostic);
        }

        [Test]
        public void 終局状態と死亡状態では攻撃を開始しない()
        {
            int startedCount = 0;
            combat.AttackSequenceStarted += _ => startedCount++;

            combat.SetFallbackGameplayState(GameplayState.Victory);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);
            combat.SetFallbackGameplayState(GameplayState.Defeat);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(0));

            combat.SetFallbackGameplayState(GameplayState.Running);
            combat.SetDead(true);
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False);
            Assert.That(startedCount, Is.EqualTo(0), "終局または死亡状態で攻撃系列が開始されました。");
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
                CollectionAssert.Contains(diagnostics, "PlayerCombatControllerのInputReader参照がありません。");
                CollectionAssert.Contains(diagnostics, "PlayerCombatControllerのAnimator参照がありません。");
                CollectionAssert.Contains(diagnostics, "PlayerCombatControllerのPlayerAnimationDriver参照がありません。");
                CollectionAssert.Contains(diagnostics, "PlayerCombatControllerのGameFlow参照がありません。");
                CollectionAssert.Contains(diagnostics, "PlayerCombatControllerのCombatantMarker参照がありません。");
                CollectionAssert.Contains(diagnostics, "PlayerCombatControllerのSwordHitbox参照がありません。");
            }
            finally
            {
                Object.DestroyImmediate(invalidObject);
            }
        }
    }
}
