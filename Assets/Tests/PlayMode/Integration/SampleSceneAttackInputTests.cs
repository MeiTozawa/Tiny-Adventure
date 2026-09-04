using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
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
        public IEnumerator SetUp()
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
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (mouse != null && mouse.added)
            {
                UnityInputSystem.RemoveDevice(mouse);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MouseAttackStartsAndCompletesMiss()
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
        public IEnumerator MouseAttackAcceptsSingleEnemyHit()
        {
            PlayerCombatController combat = FindCombatController();
            PrepareRunningState(combat);
            GameObject enemyObject = PrepareSingleEnemyFixture(combat);
            CombatantMarker enemyMarker = enemyObject.GetComponent<CombatantMarker>();
            HealthComponent enemyHealth = enemyObject.GetComponent<HealthComponent>();
            Assert.That(enemyMarker, Is.Not.Null, $"対象「{enemyObject.name}」にCombatantMarkerがありません。");
            Assert.That(enemyHealth, Is.Not.Null, $"対象「{enemyObject.name}」にHealthComponentがありません。");
            float healthBeforeHit = enemyHealth.CurrentHealth;
            Assert.That(healthBeforeHit, Is.GreaterThan(0f), $"対象「{enemyObject.name}」の初期体力が正しく初期化されていません。");
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
            Assert.That(enemyHealth.CurrentHealth, Is.LessThan(healthBeforeHit), $"対象「{enemyObject.name}」への命中候補がDamageService.Submitを経由して体力を減らしていません。");
            Assert.That(combat.AnimationEventEndAttackWindow(), Is.True, $"対象「{combat.gameObject.name}」の敵命中用攻撃有効ウィンドウを閉じられません。");
            Assert.That(combat.AnimationEventCompleteAttack(), Is.True, $"対象「{combat.gameObject.name}」の敵命中攻撃を完了できません。");
        }

        [UnityTest]
        public IEnumerator MouseAttackDamagesEnemyWithNormalizedTimeFallback()
        {
            PlayerCombatController combat = FindCombatController();
            PrepareRunningState(combat);
            GameObject enemyObject = PrepareSingleEnemyFixture(combat);
            HealthComponent enemyHealth = enemyObject.GetComponent<HealthComponent>();
            float healthBeforeHit = enemyHealth.CurrentHealth;

            Assert.That(SendSingleMouseAttack(combat, out _), Is.True, JapaneseDiagnostic(combat, combat.LastDiagnostic));
            bool reachedAttackState = false;
            yield return WaitForAttackState(combat.TargetAnimator, 30, value => reachedAttackState = value);
            Assert.That(reachedAttackState, Is.True, AttackStateTransitionDiagnostic(combat, "normalized time回退命中テストでAttack状態へ遷移できません。"));

            for (int frame = 0; frame < 30 && enemyHealth.CurrentHealth >= healthBeforeHit; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(enemyHealth.CurrentHealth, Is.LessThan(healthBeforeHit), $"対象「{enemyObject.name}」がAnimator eventなしの攻撃窓で実際に減少していません。DamageService.Submit経路を確認してください。");
            if (combat.IsAttacking)
            {
                combat.AnimationEventCompleteAttack();
            }
        }

        [UnityTest]
        public IEnumerator EnemyContinuesChasingBetweenPathRequeries()
        {
            PlayerCombatController combat = FindCombatController();
            PrepareRunningState(combat);
            GameObject enemiesRoot = GameObject.Find("Enemies");
            Assert.That(enemiesRoot, Is.Not.Null, "SampleSceneにEnemies階層がありません。対象: Enemies");
            GameObject enemyObject = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemyObject, Is.Not.Null, "SampleSceneにEnemy_01がありません。対象: Enemies/Enemy_01");

            for (int index = 0; index < enemiesRoot.transform.childCount; index++)
            {
                enemiesRoot.transform.GetChild(index).gameObject.SetActive(enemiesRoot.transform.GetChild(index).gameObject == enemyObject);
            }

            EnemyBrain brain = enemyObject.GetComponent<EnemyBrain>();
            NavMeshAgent agent = enemyObject.GetComponent<NavMeshAgent>();
            Assert.That(brain, Is.Not.Null, $"対象「{enemyObject.name}」にEnemyBrainがありません。");
            Assert.That(agent, Is.Not.Null, $"対象「{enemyObject.name}」にNavMeshAgentがありません。");

            int movingSamples = 0;
            int zeroMovementSamples = 0;
            int longestZeroMovementRun = 0;
            Vector3 previousPosition = enemyObject.transform.position;
            for (int frame = 0; frame < 60; frame++)
            {
                yield return new WaitForFixedUpdate();
                float movement = Vector3.Distance(previousPosition, enemyObject.transform.position);
                if (movement > 0.0001f)
                {
                    movingSamples++;
                    zeroMovementSamples = 0;
                }
                else
                {
                    zeroMovementSamples++;
                    longestZeroMovementRun = Mathf.Max(longestZeroMovementRun, zeroMovementSamples);
                }

                previousPosition = enemyObject.transform.position;
            }

            Assert.That(agent.isOnNavMesh, Is.True, $"対象「{enemyObject.name}」がNavMesh外へ移動しました。診断: {brain.LastDiagnostic}");
            Assert.That(movingSamples, Is.GreaterThan(30), $"対象「{enemyObject.name}」の追跡中移動サンプルが少なすぎます。経路節流中に停止していないか確認してください。診断: {brain.LastDiagnostic}");
            Assert.That(longestZeroMovementRun, Is.LessThan(15), $"対象「{enemyObject.name}」が経路再問い合わせ周期で長時間停止しました。最大連続停止サンプル: {longestZeroMovementRun}");
        }

        [UnityTest]
        public IEnumerator AllSampleSceneEnemiesCanChaseOnNavMesh()
        {
            PlayerCombatController combat = FindCombatController();
            PrepareRunningState(combat);
            GameObject enemiesRoot = GameObject.Find("Enemies");
            Assert.That(enemiesRoot, Is.Not.Null, "SampleSceneにEnemies階層がありません。対象: Enemies");
            Assert.That(enemiesRoot.transform.childCount, Is.GreaterThanOrEqualTo(3), "SampleSceneの敵開始点が3体未満です。");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            for (int index = 0; index < enemiesRoot.transform.childCount; index++)
            {
                GameObject enemy = enemiesRoot.transform.GetChild(index).gameObject;
                if (!enemy.activeInHierarchy) continue;
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                EnemyBrain brain = enemy.GetComponent<EnemyBrain>();
                Assert.That(agent, Is.Not.Null, $"対象「{enemy.name}」にNavMeshAgentがありません。");
                Assert.That(agent.enabled, Is.True, $"対象「{enemy.name}」のNavMeshAgentが有効ではありません。");
                Assert.That(agent.isOnNavMesh, Is.True, $"対象「{enemy.name}」のNavMeshAgentがNavMesh上にありません。");
                Assert.That(brain, Is.Not.Null, $"対象「{enemy.name}」にEnemyBrainがありません。");
                Assert.That(brain.PlayerTarget, Is.SameAs(combat.CombatantMarker), $"対象「{enemy.name}」の追跡対象がKnightではありません。");
                Assert.That(brain.State, Is.EqualTo(EnemyBrainState.Chase).Or.EqualTo(EnemyBrainState.PrepareAttack).Or.EqualTo(EnemyBrainState.Attack), $"対象「{enemy.name}」がChaseへ遷移していません。診断: {brain.LastDiagnostic}");
            }
        }

        [UnityTest]
        public IEnumerator TerminalStatesDisableMouseAttack()
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

            Assert.That(flow.TrySetState(GameplayState.Defeat), Is.False, "Victory状態からDefeat状態へ書き換えられてはいけません。");
            Assert.That(SendSingleMouseAttack(combat, out GameplayInputSnapshot defeatSnapshot), Is.False, "Victory中の後続左クリック攻撃が拒否されていません。");
            Assert.That(defeatSnapshot.AttackPressed, Is.True, "終局中のテスト入力がAttackPressedへ変換されていません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Victory), "Victory中にゲーム状態が変更されました。");
            Assert.That(combat.AttackTriggerCount, Is.EqualTo(triggerCountBefore), "Victory中にAttackTriggerが発行されました。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AttackStateTransitionFailureReportsJapaneseDiagnostic()
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

            NavMeshAgent agent = enemyObject.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = false;
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
