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
        public void SetUp()
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
            combat.SetDependencies(
                player.GetComponent<InputReader>(),
                player.GetComponent<PlayerAnimationDriver>(),
                animator,
                player.GetComponent<CombatantMarker>(),
                null,
                hitboxObject.GetComponent<CombatHitbox>());
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
        public void SingleAttackInputEmitsOneSequenceAndTrigger()
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
        public void TenSequentialAttacksCanStartAfterCompletion()
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
        public void RepeatedInputDuringAttackDoesNotCreateSecondSequence()
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
        public void TerminalOrDeadStateBlocksAttack()
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
        public void MissingPlayerCombatControllerReportsJapaneseDiagnostic()
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
        public void MissingRequiredReferencesReportSpecifiedJapaneseDiagnostics()
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

        [Test]
        public void AttackInput_WhenAnimatorStillInAttackState_IsRejectedWithJapaneseDiagnostic()
        {
            var animator = combat.TargetAnimator;
            animator.Play("Attack", 0, 0.75f);
            animator.Update(0f);

            int startedCount = 0;
            combat.AttackSequenceStarted += _ => startedCount++;

            bool started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));

            Assert.That(started, Is.False, "Animator が Attack 状態のときは新たな攻撃の開始を拒否する必要があります。");
            Assert.That(startedCount, Is.EqualTo(0), "Animator が Attack 状態のときは AttackSequenceStarted を発火してはなりません。");
            StringAssert.Contains("動作復帰中のため、再入力を無視しました。", combat.LastDiagnostic);
        }

        [Test]
        public void ProcessInput_WhenAttackStarted_BufferDoesNotCauseImmediateSecondSequenceInSameFrame()
        {
            combat.BufferAttack();

            bool started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));
            Assert.That(started, Is.True);
            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));

            // While attacking, buffered attack should not start another sequence
            if (!combat.IsAttacking && combat.HasBufferedAttack)
            {
                combat.TryStartAttack(out _);
            }

            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
        }
    }
}
