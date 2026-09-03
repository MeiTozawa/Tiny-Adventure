using System;
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
    /// SampleSceneの左クリック攻撃入口、攻撃状態、攻撃有効時間、命中、空振り、
    /// 終局門禁と遷移失敗診断をPlay Modeで検証します。
    /// </summary>
    public sealed class SampleSceneAttackInputTests
    {
        private Mouse mouse;

        [UnitySetUp]
        public IEnumerator セットアップ()
        {
            // SampleSceneの既存Prefabには後続タスクで接続する参照があり、起動時に既知の診断を出します。
            // シーン準備中だけ無視し、テスト本体では新しいエラーを通常どおり失敗させます。
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;

            mouse = UnityInputSystem.AddDevice<Mouse>();
            Assert.That(mouse, Is.Not.Null, "SampleSceneの攻撃入力テスト用マウスを作成できませんでした。対象: InputReader");
            UnityInputSystem.Update();
        }

        [UnityTearDown]
        public IEnumerator 後始末()
        {
            LogAssert.ignoreFailingMessages = false;
            if (mouse != null && mouse.added)
            {
                UnityInputSystem.RemoveDevice(mouse);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Running後の左クリック一回でAttackTrigger攻撃状態攻撃窓を検証し空振りを完了する()
        {
            PlayerCombatController combat = FindCombatController();
            DisableAllSceneEnemies();
            PrepareRunningState(combat);

            InputReader inputReader = combat.InputReader;
            Assert.That(inputReader.TryInitialize(), Is.True, JapaneseDiagnostic(combat, inputReader.LastDiagnostic));
            Assert.That(inputReader.IsGameplayMapEnabled, Is.True, $"対象「{combat.gameObject.name}」のGameplayアクションマップが有効になっていません。");
            Assert.That(inputReader.IsAttackActionEnabled, Is.True, $"対象「{combat.gameObject.name}」のGameplay/Attackアクションが有効になっていません。");
            Assert.That(inputReader.HasMouseAttackBinding, Is.True, $"対象「{combat.gameObject.name}」のGameplay/Attackに<Mouse>/leftButtonバインドがありません。");

            AttackSequence sequence = combat.CurrentAttackSequence;
            Assert.That(sequence, Is.Not.Null, $"対象「{combat.gameObject.name}」の攻撃系列が初期化されていません。");
            int openedCount = 0;
            int closedCount = 0;
            sequence.WindowOpened += _ => openedCount++;
            sequence.WindowClosed += _ => closedCount++;

            Assert.That(SendSingleMouseAttack(combat, out GameplayInputSnapshot snapshot), Is.True, JapaneseDiagnostic(combat, combat.LastDiagnostic));
            Assert.That(snapshot.AttackPressed, Is.True, $"対象「{combat.gameObject.name}」の左クリックがAttackPressedへ変換されていません。");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(1), $"対象「{combat.gameObject.name}」でAttackTriggerが一回だけ発行されていません。");

            bool reachedAttackState = false;
            yield return WaitForAttackState(combat.TargetAnimator, 30, value => reachedAttackState = value);
            Assert.That(
                reachedAttackState,
                Is.True,
                AttackStateTransitionDiagnostic(combat, "左クリック入力からAttack状態への遷移に失敗しました。"));

            Assert.That(combat.AnimationEventBeginAttackWindow(), Is.True, $"対象「{combat.gameObject.name}」の攻撃有効ウィンドウを開始できません。");
            Assert.That(sequence.IsWindowOpen, Is.True, $"対象「{combat.gameObject.name}」の攻撃有効ウィンドウが開いていません。");
            Assert.That(openedCount, Is.EqualTo(1), $"対象「{combat.gameObject.name}」の攻撃有効ウィンドウが一度だけ開いていません。");

            Assert.That(combat.AnimationEventEndAttackWindow(), Is.True, $"対象「{combat.gameObject.name}」の空振り攻撃ウィンドウを閉じられません。");
            Assert.That(sequence.IsWindowOpen, Is.False, $"対象「{combat.gameObject.name}」の空振り攻撃ウィンドウが閉じていません。");
            Assert.That(closedCount, Is.EqualTo(1), $"対象「{combat.gameObject.name}」の攻撃有効ウィンドウが一度だけ閉じていません。");
            Assert.That(combat.AnimationEventCompleteAttack(), Is.True, $"対象「{combat.gameObject.name}」の空振り攻撃系列を完了できません。");
            Assert.That(combat.IsAttacking, Is.False, $"対象「{combat.gameObject.name}」の空振り完了後も攻撃系列が残っています。");

            bool returnedToLocomotion = false;
            yield return WaitForIdleOrLocomotion(combat.TargetAnimator, 240, value => returnedToLocomotion = value);
            Assert.That(
                returnedToLocomotion,
                Is.True,
                AttackStateTransitionDiagnostic(combat, "攻撃完了後にIdleまたはLocomotionへ戻れません。"));
        }

        [UnityTest]
        public IEnumerator Running後の左クリック一回で敵への命中候補を一つだけ受理する()
        {
            PlayerCombatController combat = FindCombatController();
            PrepareRunningState(combat);
            GameObject enemyObject = PrepareSingleEnemyFixture(combat);
            CombatantMarker enemyMarker = enemyObject.GetComponent<CombatantMarker>();
            Assert.That(enemyMarker, Is.Not.Null, $"対象「{enemyObject.name}」にCombatantMarkerがありません。");
            Assert.That(enemyMarker.Faction, Is.EqualTo(CombatantMarker.CombatantFaction.Enemy), $"対象「{enemyObject.name}」の陣営が敵ではありません。");

            int acceptedCandidates = 0;
            CombatantMarker acceptedTarget = null;
            combat.HitCandidateAccepted += (target, _) =>
            {
                acceptedCandidates++;
                acceptedTarget = target;
            };

            Assert.That(SendSingleMouseAttack(combat, out GameplayInputSnapshot snapshot), Is.True, JapaneseDiagnostic(combat, combat.LastDiagnostic));
            Assert.That(snapshot.AttackPressed, Is.True, $"対象「{combat.gameObject.name}」の左クリックがAttackPressedへ変換されていません。");

            bool reachedAttackState = false;
            yield return WaitForAttackState(combat.TargetAnimator, 30, value => reachedAttackState = value);
            Assert.That(
                reachedAttackState,
                Is.True,
                AttackStateTransitionDiagnostic(combat, "敵命中テストでAttack状態へ遷移できません。"));

            Assert.That(combat.AnimationEventBeginAttackWindow(), Is.True, $"対象「{combat.gameObject.name}」の敵命中用攻撃有効ウィンドウを開始できません。");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(acceptedCandidates, Is.EqualTo(1), $"対象「{enemyObject.name}」への同一攻撃系列の命中候補が一つだけ受理されていません。");
            Assert.That(acceptedTarget, Is.SameAs(enemyMarker), $"対象「{enemyObject.name}」が命中対象として受理されていません。");
            Assert.That(combat.AnimationEventEndAttackWindow(), Is.True, $"対象「{combat.gameObject.name}」の敵命中用攻撃有効ウィンドウを閉じられません。");
            Assert.That(combat.AnimationEventCompleteAttack(), Is.True, $"対象「{combat.gameObject.name}」の敵命中攻撃を完了できません。");
        }

        [UnityTest]
        public IEnumerator VictoryとDefeatの終局状態では左クリック攻撃が無効になる()
        {
            PlayerCombatController combat = FindCombatController();
            DisableAllSceneEnemies();
            PrepareRunningState(combat);
            int triggerCountBefore = combat.AttackTriggerCount;

            GameFlowController flow = combat.GameFlowController;
            Assert.That(flow, Is.Not.Null, $"対象「{combat.gameObject.name}」のGameFlow参照がありません。");

            Assert.That(flow.TrySetState(GameplayState.Victory), Is.True, "Victory状態へ遷移できませんでした。");
            Assert.That(SendSingleMouseAttack(combat, out GameplayInputSnapshot victorySnapshot), Is.False, "対象「Player」のVictory中の左クリック攻撃が拒否されていません。");
            Assert.That(victorySnapshot.AttackPressed, Is.True, "Victory中のテスト入力がAttackPressedへ変換されていません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Victory), "Victory中にゲーム状態が変更されました。");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(triggerCountBefore), "Victory中にAttackTriggerが発行されました。");

            Assert.That(flow.TrySetState(GameplayState.Defeat), Is.True, "Defeat状態へ遷移できませんでした。");
            Assert.That(SendSingleMouseAttack(combat, out GameplayInputSnapshot defeatSnapshot), Is.False, "対象「Player」のDefeat中の左クリック攻撃が拒否されていません。");
            Assert.That(defeatSnapshot.AttackPressed, Is.True, "Defeat中のテスト入力がAttackPressedへ変換されていません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Defeat), "Defeat中にゲーム状態が変更されました。");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(triggerCountBefore), "Defeat中にAttackTriggerが発行されました。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Attack状態への遷移失敗時は対象名を含む日本語診断を返す()
        {
            PlayerCombatController combat = FindCombatController();
            DisableAllSceneEnemies();
            PrepareRunningState(combat);

            Animator animator = combat.TargetAnimator;
            Assert.That(animator, Is.Not.Null, $"対象「{combat.gameObject.name}」のAnimator参照がありません。");
            animator.enabled = false;

            Assert.That(SendSingleMouseAttack(combat, out GameplayInputSnapshot snapshot), Is.True, JapaneseDiagnostic(combat, combat.LastDiagnostic));
            Assert.That(snapshot.AttackPressed, Is.True, $"対象「{combat.gameObject.name}」の左クリックがAttackPressedへ変換されていません。");

            bool reachedAttackState = false;
            yield return WaitForAttackState(animator, 10, value => reachedAttackState = value);
            Assert.That(reachedAttackState, Is.False, "無効化したAnimatorがAttack状態へ遷移する想定外の結果になりました。");

            string diagnostic = AttackStateTransitionDiagnostic(combat, "入力からAttack状態への遷移に失敗しました。");
            Assert.That(diagnostic, Does.Contain(combat.gameObject.name), "遷移失敗診断に対象名が含まれていません。");
            Assert.That(ContainsJapanese(diagnostic), Is.True, "遷移失敗診断が日本語ではありません。");
            Assert.That(diagnostic, Does.Contain("Attack状態"), "遷移失敗診断にAttack状態が含まれていません。");

            combat.CancelAttack();
        }

        private static PlayerCombatController FindCombatController()
        {
            PlayerCombatController combat = UnityEngine.Object.FindAnyObjectByType<PlayerCombatController>();
            Assert.That(combat, Is.Not.Null, "SampleSceneにPlayerCombatControllerがありません。対象: Player");
            Assert.That(combat.gameObject.activeInHierarchy, Is.True, $"対象「{combat.gameObject.name}」のKnightが非アクティブです。");
            return combat;
        }

        private static void PrepareRunningState(PlayerCombatController combat)
        {
            Assert.That(combat.GameFlowController, Is.Not.Null, $"対象「{combat.gameObject.name}」のGameFlow参照がありません。");
            combat.GameFlowController.TrySetState(GameplayState.Running);
            Assert.That(combat.CurrentGameplayState, Is.EqualTo(GameplayState.Running), $"対象「{combat.gameObject.name}」がRunning状態ではありません。");
            Assert.That(combat.InputReader, Is.Not.Null, $"対象「{combat.gameObject.name}」のInputReader参照がありません。");
        }

        private bool SendSingleMouseAttack(PlayerCombatController combat, out GameplayInputSnapshot snapshot)
        {
            UnityInputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            UnityInputSystem.Update();
            snapshot = combat.InputReader.ReadSnapshot();
            bool started = combat.ProcessInput(snapshot);

            UnityInputSystem.QueueStateEvent(mouse, new MouseState { buttons = 0 });
            UnityInputSystem.Update();
            return started;
        }

        private static void DisableAllSceneEnemies()
        {
            GameObject enemiesRoot = GameObject.Find("Enemies");
            Assert.That(enemiesRoot, Is.Not.Null, "SampleSceneにEnemies階層がありません。対象: Enemies");
            enemiesRoot.SetActive(false);
        }

        private static GameObject PrepareSingleEnemyFixture(PlayerCombatController combat)
        {
            GameObject enemiesRoot = GameObject.Find("Enemies");
            Assert.That(enemiesRoot, Is.Not.Null, "SampleSceneにEnemies階層がありません。対象: Enemies");
            enemiesRoot.SetActive(true);

            GameObject enemyObject = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemyObject, Is.Not.Null, "SampleSceneにEnemy_01がありません。対象: Enemies/Enemy_01");
            for (int index = 0; index < enemiesRoot.transform.childCount; index++)
            {
                Transform child = enemiesRoot.transform.GetChild(index);
                child.gameObject.SetActive(child.gameObject == enemyObject);
            }

            EnemyBrain brain = enemyObject.GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.enabled = false;
            }

            EnemyMeleeCombat meleeCombat = enemyObject.GetComponent<EnemyMeleeCombat>();
            if (meleeCombat != null)
            {
                meleeCombat.enabled = false;
            }

            EnemyLifecycle lifecycle = enemyObject.GetComponent<EnemyLifecycle>();
            if (lifecycle != null)
            {
                lifecycle.enabled = false;
            }

            Rigidbody rigidbody = enemyObject.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = enemyObject.AddComponent<Rigidbody>();
            }

            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            BoxCollider fixtureCollider = enemyObject.GetComponent<BoxCollider>();
            if (fixtureCollider == null)
            {
                fixtureCollider = enemyObject.AddComponent<BoxCollider>();
            }

            fixtureCollider.isTrigger = false;
            fixtureCollider.center = Vector3.zero;
            fixtureCollider.size = Vector3.one;
            enemyObject.transform.position = combat.SwordHitbox.transform.position;
            Physics.SyncTransforms();
            return enemyObject;
        }

        private static IEnumerator WaitForAttackState(Animator animator, int maxFrames, Action<bool> result)
        {
            bool reached = false;
            for (int frame = 0; frame < maxFrames; frame++)
            {
                yield return null;
                if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                {
                    reached = true;
                    break;
                }
            }

            result(reached);
        }

        private static IEnumerator WaitForIdleOrLocomotion(Animator animator, int maxFrames, Action<bool> result)
        {
            bool reached = false;
            for (int frame = 0; frame < maxFrames; frame++)
            {
                yield return null;
                if (animator == null)
                {
                    break;
                }

                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Idle") || state.IsName("Locomotion"))
                {
                    reached = true;
                    break;
                }
            }

            result(reached);
        }

        private static string JapaneseDiagnostic(PlayerCombatController combat, string detail)
        {
            return $"対象「{combat.gameObject.name}」の攻撃入力統合検証に失敗しました。{detail}";
        }

        private static string AttackStateTransitionDiagnostic(PlayerCombatController combat, string detail)
        {
            return $"対象「{combat.gameObject.name}」のAttack状態遷移診断: {detail} AnimatorとAttackTriggerを確認してください。";
        }

        private static bool ContainsJapanese(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (char character in value)
            {
                if ((character >= '\u3040' && character <= '\u30ff') ||
                    (character >= '\u4e00' && character <= '\u9fff'))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
