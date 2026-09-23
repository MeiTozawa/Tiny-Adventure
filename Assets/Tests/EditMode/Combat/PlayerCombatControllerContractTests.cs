using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

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
            combat.Construct(
                damageService: null,
                gameFlowController: null,
                inputReader: player.GetComponent<InputReader>(),
                animationDriver: player.GetComponent<PlayerAnimationDriver>(),
                targetAnimator: animator,
                combatantMarker: player.GetComponent<CombatantMarker>(),
                swordHitbox: hitboxObject.GetComponent<CombatHitbox>());
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

            Result started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));

            Assert.That(started.IsOk, Is.True, combat.LastDiagnostic);
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
                    combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)).IsOk,
                    Is.True,
                    $"攻撃系列{sequenceId}を開始できません。診断: {combat.LastDiagnostic}");
                Assert.That(combat.LastAttackSequenceId, Is.EqualTo(sequenceId));
                Assert.That(combat.AnimationEventBeginAttackWindow().IsOk, Is.True, $"攻撃系列{sequenceId}のウィンドウを開けません。");
                Assert.That(combat.AnimationEventEndAttackWindow().IsOk, Is.True, $"攻撃系列{sequenceId}のウィンドウを閉じられません。");
                Assert.That(combat.AnimationEventCompleteAttack().IsOk, Is.True, $"攻撃系列{sequenceId}を完了できません。");
                Assert.That(combat.IsAttacking, Is.False, $"攻撃系列{sequenceId}完了後もIsAttackingが残っています。");
            }

            Assert.That(combat.AttackTriggerCount, Is.EqualTo(10));
        }

        [Test]
        public void RepeatedInputDuringAttackDoesNotCreateSecondSequence()
        {
            int startedCount = 0;
            combat.AttackSequenceStarted += _ => startedCount++;

            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)).IsOk, Is.True);
            Result secondResult = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));
            Assert.That(secondResult.IsErr, Is.True);
            Assert.That(secondResult.Error, Is.EqualTo(GameError.ActionInProgress));

            Assert.That(startedCount, Is.EqualTo(1), "攻撃中の再入力で二つ目の攻撃系列が開始されました。");
            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1));
        }

        [Test]
        public void TerminalOrDeadStateBlocksAttack()
        {
            int startedCount = 0;
            combat.AttackSequenceStarted += _ => startedCount++;

            combat.SetFallbackGameplayState(GameplayState.Victory);
            Result victoryResult = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));
            Assert.That(victoryResult.IsErr, Is.True);
            Assert.That(victoryResult.Error, Is.EqualTo(GameError.StateAlreadyTerminal));

            combat.SetFallbackGameplayState(GameplayState.Defeat);
            Result defeatResult = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));
            Assert.That(defeatResult.IsErr, Is.True);
            Assert.That(defeatResult.Error, Is.EqualTo(GameError.StateAlreadyTerminal));
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(0));

            combat.SetFallbackGameplayState(GameplayState.Running);
            combat.SetDead(true);
            Result deadResult = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));
            Assert.That(deadResult.IsErr, Is.True);
            Assert.That(deadResult.Error, Is.EqualTo(GameError.TargetDead));
            Assert.That(startedCount, Is.EqualTo(0), "終局または死亡状態で攻撃系列が開始されました。");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(0));
        }

        [Test]
        public void AttackInput_WhenAnimatorStillInAttackState_IsRejectedWithJapaneseDiagnostic()
        {
            var animator = combat.TargetAnimator;
            animator.Play("Attack", 0, 0.75f);
            animator.Update(0f);

            int startedCount = 0;
            combat.AttackSequenceStarted += _ => startedCount++;

            Result started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));

            Assert.That(started.IsErr, Is.True, "Animator が Attack 状態のときは新たな攻撃の開始を拒否する必要があります。");
            Assert.That(started.Error, Is.EqualTo(GameError.RecoveryInProgress));
            Assert.That(startedCount, Is.EqualTo(0), "Animator が Attack 状態のときは AttackSequenceStarted を発火してはなりません。");
        }

        [Test]
        public void ProcessInput_WhenAttackStarted_BufferDoesNotCauseImmediateSecondSequenceInSameFrame()
        {
            combat.BufferAttack();

            Result started = combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false));
            Assert.That(started.IsOk, Is.True);
            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));

            // While attacking, buffered attack should not start another sequence
            if (!combat.IsAttacking && combat.HasBufferedAttack)
            {
                combat.StartAttack();
            }

            Assert.That(combat.LastAttackSequenceId, Is.EqualTo(1));
        }

        [Test]
        public void VContainer_CanInjectPlayerCombatController_WithDirectResolution()
        {
            var builder = new VContainer.ContainerBuilder();
            var dummyDamageService = player.AddComponent<DamageService>();
            var dummyFlow = player.AddComponent<GameFlowController>();
            builder.RegisterComponent(dummyDamageService);
            builder.RegisterComponent(dummyFlow);
            var container = builder.Build();

            container.Inject(combat);

            Assert.That(combat.DamageService, Is.SameAs(dummyDamageService));
            Assert.That(combat.GameFlowController, Is.SameAs(dummyFlow));
        }
    }
}
