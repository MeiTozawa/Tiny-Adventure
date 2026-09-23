using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// SampleScene における3段コンボ攻撃（横薙ぎ・縦斬り・突進刺突）の統合プレイモードテストです。
    /// 3段の連続入力によるステート遷移、各段の進行、およびタイムアウトによるリセットを検証します。
    /// </summary>
    public sealed class PlayerComboIntegrationTests
    {
        private const string SampleSceneName = "SampleScene";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = false;
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(SampleSceneName, LoadSceneMode.Single);
            while (!loadOp.isDone)
            {
                yield return null;
            }

            DisableAllSceneEnemies();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return null;
        }

        [UnityTest]
        public IEnumerator ComboSequence_TransitionsThroughAllThreeStates()
        {
            PlayerCombatController combat = UnityEngine.Object.FindAnyObjectByType<PlayerCombatController>();
            Assert.That(combat, Is.Not.Null, "SampleSceneにPlayerCombatControllerがありません。");
            PrepareRunningState(combat);
            Animator animator = combat.TargetAnimator;
            Assert.That(animator, Is.Not.Null, "PlayerCombatControllerにAnimatorがありません。");

            // 1段目: 横薙ぎ (Attack_Horizontal)
            Assert.That(combat.ComboIndex, Is.EqualTo(0));
            Assert.That(combat.StartAttack().IsOk, Is.True);

            bool reachedHorizontal = false;
            yield return WaitForState(animator, "Attack_Horizontal", 1.5f, val => reachedHorizontal = val);
            Assert.That(reachedHorizontal, Is.True, "1段目のAttack_Horizontal状態への遷移に失敗しました。");

            yield return WaitForAttackEnd(combat, 3.0f);
            Assert.That(combat.ComboIndex, Is.EqualTo(1), "1段目完了後にComboIndexが1へ進んでいません。");

            // 2段目: 縦斬り (Attack_Vertical)
            Assert.That(combat.StartAttack().IsOk, Is.True);

            bool reachedVertical = false;
            yield return WaitForState(animator, "Attack_Vertical", 1.5f, val => reachedVertical = val);
            Assert.That(reachedVertical, Is.True, "2段目のAttack_Vertical状態への遷移に失敗しました。");

            yield return WaitForAttackEnd(combat, 3.0f);
            Assert.That(combat.ComboIndex, Is.EqualTo(2), "2段目完了後にComboIndexが2へ進んでいません。");

            // 3段目: 突進刺突 (Attack_Thrust)
            Assert.That(combat.StartAttack().IsOk, Is.True);

            bool reachedThrust = false;
            yield return WaitForState(animator, "Attack_Thrust", 1.5f, val => reachedThrust = val);
            Assert.That(reachedThrust, Is.True, "3段目のAttack_Thrust状態への遷移に失敗しました。");

            yield return WaitForAttackEnd(combat, 3.0f);
            Assert.That(combat.ComboIndex, Is.EqualTo(0), "3段目完了後にComboIndexが0へ循環リセットされていません。");
        }

        [UnityTest]
        public IEnumerator ComboTimeout_ResetsToFirstAttack()
        {
            PlayerCombatController combat = UnityEngine.Object.FindAnyObjectByType<PlayerCombatController>();
            Assert.That(combat, Is.Not.Null, "SampleSceneにPlayerCombatControllerがありません。");
            PrepareRunningState(combat);
            Animator animator = combat.TargetAnimator;

            // 1段目実行
            Assert.That(combat.StartAttack().IsOk, Is.True);
            yield return WaitForState(animator, "Attack_Horizontal", 1.5f, _ => { });
            yield return WaitForAttackEnd(combat, 3.0f);
            Assert.That(combat.ComboIndex, Is.EqualTo(1), "1段目完了直後にComboIndexが1になっていません。");

            // コンボリセット猶予時間（0.45秒）以上待機
            float waitDuration = combat.AttackConfig is ComboAttackConfig comboConfig
                ? comboConfig.ComboResetTimeout + 0.15f
                : 0.6f;

            float elapsed = 0f;
            while (elapsed < waitDuration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.That(combat.ComboIndex, Is.EqualTo(0), "タイムアウト後にComboIndexが0へリセットされていません。");

            // 再度攻撃した際に1段目（Attack_Horizontal）から始まることを確認
            Assert.That(combat.StartAttack().IsOk, Is.True);
            bool reachedHorizontalAgain = false;
            yield return WaitForState(animator, "Attack_Horizontal", 1.5f, val => reachedHorizontalAgain = val);
            Assert.That(reachedHorizontalAgain, Is.True, "タイムアウト後の再攻撃でAttack_Horizontalへ遷移しませんでした。");
            yield return WaitForAttackEnd(combat, 3.0f);
        }

        private static void PrepareRunningState(PlayerCombatController combat)
        {
            if (combat.GameFlowController != null)
            {
                combat.GameFlowController.SetState(GameplayState.Running);
            }
            combat.SetFallbackGameplayState(GameplayState.Running);
        }

        private static void DisableAllSceneEnemies()
        {
            foreach (EnemyMeleeCombat meleeCombat in UnityEngine.Object.FindObjectsByType<EnemyMeleeCombat>())
            {
                meleeCombat.gameObject.SetActive(false);
            }
        }

        private static IEnumerator WaitForState(Animator animator, string stateName, float timeoutSeconds, Action<bool> result)
        {
            bool reached = false;
            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (animator == null)
                {
                    break;
                }

                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
                if (current.IsName(stateName))
                {
                    reached = true;
                    break;
                }

                if (animator.IsInTransition(0))
                {
                    AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                    if (next.IsName(stateName))
                    {
                        reached = true;
                        break;
                    }
                }
            }
            result(reached);
        }

        private static IEnumerator WaitForAttackEnd(PlayerCombatController combat, float timeoutSeconds = 3.0f)
        {
            yield return null;
            float elapsed = 0f;
            while (combat.IsAttacking && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.That(
                combat.IsAttacking,
                Is.False,
                $"攻撃が{timeoutSeconds}秒以内に完了しませんでした。(ComboIndex={combat.ComboIndex}, LastSequenceId={combat.LastAttackSequenceId})");
        }
    }
}
