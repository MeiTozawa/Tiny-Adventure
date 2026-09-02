using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// SampleSceneの左クリック攻撃入口、攻撃状態、空振り完了、終局拒否をPlay Modeで検証します。
    /// </summary>
    public sealed class SampleSceneAttackInputTests
    {
        private Mouse mouse;

        [UnitySetUp]
        public IEnumerator セットアップ()
        {
            SceneManager.LoadScene(0);
            yield return null;
            mouse = UnityInputSystem.AddDevice<Mouse>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator 後始末()
        {
            if (mouse != null && mouse.added)
            {
                UnityInputSystem.RemoveDevice(mouse);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator 左クリック一回でAttack状態と攻撃ウィンドウを開始し空振りを完了する()
        {
            PlayerCombatController combat = FindCombatController();
            InputReader inputReader = combat.InputReader;
            Assert.That(inputReader.TryInitialize(), Is.True, inputReader.LastDiagnostic);
            Assert.That(inputReader.IsGameplayMapEnabled, Is.True, "Gameplayアクションマップが有効になっていません。");
            Assert.That(inputReader.IsAttackActionEnabled, Is.True, "Gameplay/Attackアクションが有効になっていません。");
            Assert.That(inputReader.HasMouseAttackBinding, Is.True, "Gameplay/Attackに<Mouse>/leftButtonバインドがありません。");

            UnityInputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            UnityInputSystem.Update();
            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            Assert.That(snapshot.AttackPressed, Is.True, "左クリックがAttackPressedへ変換されていません。");
            Assert.That(combat.ProcessInput(snapshot), Is.True, combat.LastDiagnostic);
            yield return null;

            Animator animator = combat.TargetAnimator;
            bool reachedAttackState = false;
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
                if (animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                {
                    reachedAttackState = true;
                    break;
                }
            }

            Assert.That(reachedAttackState, Is.True, "KnightのAttack状態へ遷移できません。対象: Player");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1), "AttackTriggerが一回の入力で一度だけ発行されていません。");
            Assert.That(combat.AnimationEventBeginAttackWindow(), Is.True, "攻撃有効ウィンドウを開始できません。");
            Assert.That(combat.CurrentAttackSequence.IsWindowOpen, Is.True, "攻撃有効ウィンドウが開いていません。");
            Assert.That(combat.AnimationEventEndAttackWindow(), Is.True, "空振り攻撃の有効ウィンドウを閉じられません。");
            Assert.That(combat.AnimationEventCompleteAttack(), Is.True, "空振り攻撃系列を完了できません。");
            Assert.That(combat.IsAttacking, Is.False, "空振り完了後も攻撃状態が残っています。");
            bool returnedToLocomotion = false;
            for (int frame = 0; frame < 120; frame++)
            {
                yield return null;
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Idle") || state.IsName("Locomotion"))
                {
                    returnedToLocomotion = true;
                    break;
                }
            }

            AnimatorStateInfo finalState = animator.GetCurrentAnimatorStateInfo(0);
            string finalClip = animator.GetCurrentAnimatorClipInfo(0).Length > 0 ? animator.GetCurrentAnimatorClipInfo(0)[0].clip.name : "clipなし";
            Assert.That(returnedToLocomotion, Is.True, $"攻撃完了後にIdleまたはLocomotionへ戻れません。対象: Player / 状態ハッシュ: {finalState.shortNameHash} / 正規化時刻: {finalState.normalizedTime:F2} / clip: {finalClip}");
        }

        [UnityTest]
        public IEnumerator 攻撃ウィンドウ中の敵接触は一つの命中候補だけを受理する()
        {
            PlayerCombatController combat = FindCombatController();
            var enemy = new GameObject("Enemy_Test");
            enemy.AddComponent<CombatantMarker>();
            var enemyCollider = enemy.AddComponent<BoxCollider>();
            var rigidbody = enemy.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            enemy.transform.position = combat.SwordHitbox.transform.position;
            Physics.SyncTransforms();

            int acceptedCandidates = 0;
            combat.HitCandidateAccepted += (_, _) => acceptedCandidates++;
            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.True, combat.LastDiagnostic);
            yield return null;
            Assert.That(combat.AnimationEventBeginAttackWindow(), Is.True, "攻撃有効ウィンドウを開始できません。");
            yield return new WaitForFixedUpdate();
            Assert.That(acceptedCandidates, Is.EqualTo(1), "同一攻撃系列の敵接触が一回の命中候補へ重複排除されていません。");
            combat.AnimationEventEndAttackWindow();
            combat.AnimationEventCompleteAttack();
            Object.Destroy(enemy);
        }

        [UnityTest]
        public IEnumerator 終局中の左クリックは攻撃を拒否する()
        {
            PlayerCombatController combat = FindCombatController();
            GameFlowController flow = combat.GameFlowController;
            Assert.That(flow, Is.Not.Null, "PlayerCombatControllerのGameFlow参照がありません。対象: Player");
            flow.TrySetState(GameplayState.Victory);
            int triggerCountBefore = combat.AttackTriggerCount;

            Assert.That(combat.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, true, false, false)), Is.False, "終局中の攻撃入力が拒否されていません。");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(triggerCountBefore));
            yield return null;
        }

        private static PlayerCombatController FindCombatController()
        {
            PlayerCombatController combat = Object.FindAnyObjectByType<PlayerCombatController>();
            Assert.That(combat, Is.Not.Null, "SampleSceneにPlayerCombatControllerがありません。対象: Player");
            Assert.That(combat.gameObject.activeInHierarchy, Is.True, "Knightが非アクティブです。対象: Player");
            return combat;
        }
    }
}
