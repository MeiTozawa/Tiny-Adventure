using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 実際のSampleSceneとPrefabを使い、敵の追跡、近接攻撃、命中、死亡、終局門禁、
    /// 空振り、無経路時の安全待機を検証します。HealthComponentを直接変更しません。
    /// </summary>
    public sealed class SampleSceneEnemyCombatTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.SceneManagement.SceneManager.LoadScene("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            yield return null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyAttackDamagesPlayerWithSingleWindow()
        {
            PlayerCombatController player = FindPlayer();
            GameFlowController flow = FindFlow();
            EnemyMeleeCombat melee = PrepareSingleEnemyNearPlayer(player);
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            DamageService damage = FindDamageService();
            CombatantMarker enemyMarker = melee.GetComponent<CombatantMarker>();

            Assert.That(damage.CombatantRegistry.IsRegistered(player.CombatantMarker), Is.True, "実シーンのKnightがDamageServiceへ登録されていません。");
            Assert.That(damage.CombatantRegistry.IsRegistered(enemyMarker), Is.True, "実シーンのEnemyがDamageServiceへ登録されていません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Running), "実シーンがRunning状態ではありません。");

            float healthBefore = playerHealth.CurrentHealth;
            int started = 0;
            int opened = 0;
            int closed = 0;
            melee.AttackStarted += _ => started++;
            melee.CurrentAttackSequence.WindowOpened += _ => opened++;
            melee.CurrentAttackSequence.WindowClosed += _ => closed++;

            yield return WaitForCondition(
                () => playerHealth.CurrentHealth < healthBefore,
                240,
                "実シーンのEnemy攻撃後もKnightの体力が減少しません。EnemyHitbox、CombatHitbox、DamageService.Submitを確認してください。");
            yield return WaitForCondition(
                () => !melee.IsAttacking,
                120,
                "実シーンのEnemy攻撃系列が完了しません。");

            Assert.That(started, Is.GreaterThanOrEqualTo(1), "実シーンのEnemyMeleeCombatが攻撃を開始していません。");
            Assert.That(opened, Is.EqualTo(1), "実シーンの一回のEnemy攻撃で攻撃窓が一度だけ開いていません。");
            Assert.That(closed, Is.EqualTo(1), "実シーンの一回のEnemy攻撃で攻撃窓が一度だけ閉じていません。");
            Assert.That(playerHealth.CurrentHealth, Is.LessThan(healthBefore), "Enemy攻撃がDamageService.Submit経由でKnightのHealthComponentを減らしていません。");
        }

        [UnityTest]
        public IEnumerator EnemyChaseAndAttack_MaintainsSafetyDistanceWithoutModelOverlap()
        {
            PlayerCombatController player = FindPlayer();
            GameFlowController flow = FindFlow();
            GameObject enemy = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemy, Is.Not.Null, "実シーンにEnemy_01がありません。");
            DisableOtherEnemies(enemy);
            EnemyBrain brain = enemy.GetComponent<EnemyBrain>();
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            EnemyMeleeCombat melee = enemy.GetComponent<EnemyMeleeCombat>();

            Vector3 startPos = player.transform.position + Vector3.forward * 3.5f;
            if (NavMesh.SamplePosition(startPos, out NavMeshHit hit, 2f, agent.areaMask))
            {
                agent.Warp(hit.position);
            }
            else
            {
                enemy.transform.position = startPos;
            }

            brain.enabled = true;
            agent.enabled = true;
            melee.enabled = true;
            Physics.SyncTransforms();

            float minObservedDistance = float.MaxValue;
            yield return WaitForCondition(
                () =>
                {
                    float d = Vector3.Distance(
                        new Vector3(enemy.transform.position.x, 0f, enemy.transform.position.z),
                        new Vector3(player.transform.position.x, 0f, player.transform.position.z));
                    if (d < minObservedDistance)
                    {
                        minObservedDistance = d;
                    }

                    return brain.State == EnemyBrainState.Attack || melee.IsAttacking;
                },
                240,
                $"敵が接近して攻撃へ遷移しませんでした。現在の状態={brain.State}, 診断={brain.LastDiagnostic}");

            Assert.That(minObservedDistance, Is.GreaterThanOrEqualTo(1.8f),
                $"敵の攻撃接近距離（{minObservedDistance:F2}m）が近すぎてプレイヤーモデルと重なっています。2.0m前後の安全間距を維持してください。");
        }

        [UnityTest]
        public IEnumerator PlayerAttackDamagesEnemyAndDeduplicatesHit()
        {
            PlayerCombatController player = FindPlayer();
            GameFlowController flow = FindFlow();
            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyForPlayerAttack(player);
            HealthComponent enemyHealth = enemyMelee.GetComponent<HealthComponent>();
            Assert.That(flow.TrySetState(GameplayState.Running) || flow.CurrentState == GameplayState.Running, Is.True, "実シーンをRunning状態にできませんでした。");

            int opened = 0;
            int closed = 0;
            int accepted = 0;
            player.CurrentAttackSequence.WindowOpened += _ => opened++;
            player.CurrentAttackSequence.WindowClosed += _ => closed++;
            player.HitCandidateAccepted += (_, _) => accepted++;

            float healthBefore = enemyHealth.CurrentHealth;
            player.enabled = false;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "実シーンのPlayer攻撃窓を開始できません。");
            yield return new WaitForFixedUpdate();
            Assert.That(player.AnimationEventEndAttackWindow(), Is.True, "実シーンのPlayer攻撃窓を終了できません。");
            Assert.That(player.AnimationEventCompleteAttack(), Is.True, "実シーンのPlayer攻撃系列を完了できません。");
            Assert.That(player.IsAttacking, Is.False, "実シーンのPlayer攻撃系列が完了後も残っています。");

            Assert.That(opened, Is.EqualTo(1), "実シーンのPlayer攻撃窓が一度だけ開いていません。");
            Assert.That(closed, Is.EqualTo(1), "実シーンのPlayer攻撃窓が一度だけ閉じていません。");
            Assert.That(accepted, Is.EqualTo(1), "同一Player攻撃系列が同じ敵へ命中候補を二重登録していません。攻撃窓とAttackSequenceIdの重複排除を確認してください。");
            Assert.That(enemyHealth.CurrentHealth, Is.LessThan(healthBefore), "Player攻撃がDamageService.Submit経由で実シーンの敵のHealthComponentを減らしていません。");
        }

        [UnityTest]
        public IEnumerator PlayerAttackDoesNotDamageEnemyBeyondAttackRange()
        {
            PlayerCombatController player = FindPlayer();
            GameFlowController flow = FindFlow();
            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyForPlayerAttack(player);
            HealthComponent enemyHealth = enemyMelee.GetComponent<HealthComponent>();
            CombatantMarker enemyMarker = enemyMelee.GetComponent<CombatantMarker>();
            DamageService damage = FindDamageService();
            Assert.That(flow.TrySetState(GameplayState.Running) || flow.CurrentState == GameplayState.Running, Is.True, "実シーンをRunning状態にできませんでした。");

            enemyMelee.transform.position = player.transform.position + player.transform.forward * 3.5f;
            Physics.SyncTransforms();
            float healthBefore = enemyHealth.CurrentHealth;
            int accepted = 0;
            damage.DamageAccepted += request =>
            {
                if (request.Target == enemyMarker)
                {
                    accepted++;
                }
            };

            player.enabled = false;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "攻撃範囲外テストの攻撃窓を開始できません。");
            yield return new WaitForFixedUpdate();
            Assert.That(player.AnimationEventEndAttackWindow(), Is.True, "攻撃範囲外テストの攻撃窓を終了できません。");
            Assert.That(player.AnimationEventCompleteAttack(), Is.True, "攻撃範囲外テストの攻撃系列を完了できません。");

            Assert.That(accepted, Is.EqualTo(0), "AttackRange外の敵がDamageService.Submitへ到達しました。");
            Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(healthBefore), "AttackRange外の敵の体力が変化しました。");
        }

        [UnityTest]
        public IEnumerator LethalHitTransitionsToDeathAndRemovesEnemy()
        {
            PlayerCombatController player = FindPlayer();
            FindFlow().TrySetState(GameplayState.Running);
            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyForPlayerAttack(player);
            HealthComponent enemyHealth = enemyMelee.GetComponent<HealthComponent>();
            EnemyLifecycle lifecycle = enemyMelee.GetComponent<EnemyLifecycle>();
            Assert.That(lifecycle, Is.Not.Null, "実シーンの敵にEnemyLifecycleがありません。");
            Assert.That(enemyHealth.Configure(player.AttackDamage, out string healthDiagnostic), Is.True, healthDiagnostic);
            Assert.That(CountActiveEnemies(), Is.EqualTo(1), "致死テスト開始時の実シーン敵数が1ではありません。");

            player.enabled = false;
            float damageStart = Time.time;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "致死テストのPlayer攻撃窓を開始できません。");
            yield return new WaitForFixedUpdate();
            Assert.That(enemyHealth.State, Is.EqualTo(HealthState.DeathTransition), "致死命中後にEnemyが死亡遷移へ入りません。");
            Assert.That(Time.time - damageStart, Is.LessThanOrEqualTo(0.1f), "致死命中からEnemy死亡遷移までが0.1秒を超えました。");
            Assert.That(player.AnimationEventEndAttackWindow(), Is.True, "致死テストのPlayer攻撃窓を終了できません。");
            Assert.That(player.AnimationEventCompleteAttack(), Is.True, "致死テストのPlayer攻撃系列を完了できません。");

            yield return WaitForCondition(
                () => lifecycle.IsRemoved && !enemyMelee.gameObject.activeInHierarchy,
                240,
                "実シーンの敵がDeath clip完了後にRemovedへ遷移しません。");
            Assert.That(CountActiveEnemies(), Is.EqualTo(0), "Death clip完了後も実シーンの活動敵数が減少していません。");
            Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(0f), "除去後の敵体力が0ではありません。");
        }

        [UnityTest]
        public IEnumerator PlayerContinuousAttacksKillEnemyAndEnterVictory()
        {
            PlayerCombatController player = FindPlayer();
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = UnityEngine.Object.FindAnyObjectByType<SceneReferenceRegistry>();
            DamageService damage = FindDamageService();
            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyForPlayerAttack(player);
            CombatantMarker enemyMarker = enemyMelee.GetComponent<CombatantMarker>();
            HealthComponent enemyHealth = enemyMelee.GetComponent<HealthComponent>();
            EnemyLifecycle lifecycle = enemyMelee.GetComponent<EnemyLifecycle>();

            Assert.That(registry, Is.Not.Null, "実シーンにSceneReferenceRegistryがありません。");
            Assert.That(enemyHealth.MaximumHealth, Is.EqualTo(100f), "Enemy_Melee.prefabの最大体力が設計値100ではありません。");
            Assert.That(player.AttackDamage, Is.GreaterThan(0f), "Player攻撃ダメージが設定されていません。");

            // 無効化した他の敵を登録簿から外し、実シーンと同じ登録/解除/勝利経路を一体で検証します。
            registry.ClearRuntimeRegistrations();
            Assert.That(registry.Register(player.CombatantMarker), Is.True, "Knightを実行時登録簿へ再登録できませんでした。");
            Assert.That(registry.Register(enemyMarker), Is.True, "Enemy_01を実行時登録簿へ再登録できませんでした。");
            Assert.That(damage.CombatantRegistry.IsRegistered(player.CombatantMarker), Is.True, "KnightがDamageServiceの登録簿にありません。");
            Assert.That(damage.CombatantRegistry.IsRegistered(enemyMarker), Is.True, "Enemy_01がDamageServiceの登録簿にありません。");
            Assert.That(registry.ActiveEnemyCount, Is.EqualTo(1), "連続攻撃テストの開始時敵数が1ではありません。");
            Assert.That(flow.TrySetState(GameplayState.Running) || flow.CurrentState == GameplayState.Running, Is.True, "実シーンをRunning状態にできませんでした。");

            var acceptedSequences = new System.Collections.Generic.List<int>();
            damage.DamageAccepted += request =>
            {
                acceptedSequences.Add(request.AttackSequenceId);
                Debug.Log($"[戦闘回帰診断] DamageAccepted seq={request.AttackSequenceId} amount={request.Amount} target={request.Target.CombatantId} health={enemyHealth.CurrentHealth}/{enemyHealth.MaximumHealth} state={enemyHealth.State}");
            };
            enemyHealth.HealthChanged += (current, maximum) => Debug.Log($"[戦闘回帰診断] EnemyHealth current={current}/{maximum} state={enemyHealth.State}");
            enemyHealth.StateChanged += state => Debug.Log($"[戦闘回帰診断] EnemyHealthState={state}");
            lifecycle.DeathStarted += () => Debug.Log("[戦闘回帰診断] EnemyLifecycle DeathTransition started");
            lifecycle.Removed += () => Debug.Log($"[戦闘回帰診断] EnemyLifecycle Removed activeEnemyCount={registry.ActiveEnemyCount} gameFlow={flow.CurrentState}");
            registry.ActiveEnemyCountChanged += count => Debug.Log($"[戦闘回帰診断] ActiveEnemyCount={count} gameFlow={flow.CurrentState}");
            flow.StateChanged += state => Debug.Log($"[戦闘回帰診断] GameFlow={state}");

            player.enabled = false;
            for (int attackIndex = 0; attackIndex < 4; attackIndex++)
            {
                float healthBefore = enemyHealth.CurrentHealth;
                Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
                int sequenceId = player.LastAttackSequenceId;
                Assert.That(sequenceId, Is.GreaterThan(0), "Player攻撃系列IDが発行されていません。");
                Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, $"連続攻撃{attackIndex + 1}の攻撃窓を開けません。");
                yield return new WaitForFixedUpdate();

                Debug.Log($"[戦闘回帰診断] attack={attackIndex + 1} seq={sequenceId} health={enemyHealth.CurrentHealth}/{enemyHealth.MaximumHealth} state={enemyHealth.State}");
                Assert.That(enemyHealth.CurrentHealth, Is.LessThan(healthBefore), $"連続攻撃{attackIndex + 1}がEnemy_01の体力を減らしていません。seq={sequenceId}");
                Assert.That(player.AnimationEventEndAttackWindow(), Is.True, $"連続攻撃{attackIndex + 1}の攻撃窓を閉じられません。");
                Assert.That(player.AnimationEventCompleteAttack(), Is.True, $"連続攻撃{attackIndex + 1}を完了できません。");
            }

            Assert.That(acceptedSequences, Is.EqualTo(new[] { 1, 2, 3, 4 }), "連続攻撃のDamageAcceptedが各系列一回ずつ発生していません。");
            Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(0f), "連続攻撃後のEnemy体力が0ではありません。");
            Assert.That(enemyHealth.State, Is.EqualTo(HealthState.DeathTransition), "致死攻撃後にEnemyがDeathTransitionへ入りません。");

            yield return WaitForCondition(
                () => lifecycle.IsRemoved && !enemyMelee.gameObject.activeInHierarchy,
                240,
                "連続攻撃で致死させたEnemyがDeath clip完了後にRemovedへ遷移しません。");

            Assert.That(enemyHealth.State, Is.EqualTo(HealthState.Removed), "EnemyLifecycle Removed後のHealthStateがRemovedではありません。");
            Assert.That(registry.ActiveEnemyCount, Is.EqualTo(0), "Enemy Removed後にActiveEnemyCountが0へ減少していません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Victory), "最後のEnemy Removed後にGameFlowがVictoryへ遷移していません。");
        }

        [UnityTest]
        public IEnumerator MissedAttackCompletesNormally()
        {
            PlayerCombatController player = FindPlayer();
            DisableAllEnemies();
            GameFlowController flow = FindFlow();
            flow.TrySetState(GameplayState.Running);

            int opened = 0;
            int closed = 0;
            player.CurrentAttackSequence.WindowOpened += _ => opened++;
            player.CurrentAttackSequence.WindowClosed += _ => closed++;

            player.enabled = false;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "実シーンの空振り攻撃窓を開始できません。");
            yield return new WaitForFixedUpdate();
            Assert.That(player.AnimationEventEndAttackWindow(), Is.True, "実シーンの空振り攻撃窓を終了できません。");
            Assert.That(player.AnimationEventCompleteAttack(), Is.True, "実シーンの空振り攻撃系列を完了できません。");
            Assert.That(player.IsAttacking, Is.False, "空振り後もPlayer攻撃系列が残っています。");

            Assert.That(opened, Is.EqualTo(1), "空振り攻撃の有効窓が一度だけ開いていません。");
            Assert.That(closed, Is.EqualTo(1), "空振り攻撃の有効窓が一度だけ閉じていません。");
            Assert.That(player.IsAttacking, Is.False, "空振り後もPlayer攻撃系列が残っています。");
        }

        [UnityTest]
        public IEnumerator TerminalStateBlocksSubsequentDamage()
        {
            PlayerCombatController player = FindPlayer();
            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyNearPlayer(player);
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            HealthComponent enemyHealth = enemyMelee.GetComponent<HealthComponent>();
            GameFlowController flow = FindFlow();
            Assert.That(flow.TrySetState(GameplayState.Victory), Is.True, "実シーンをVictory状態へ遷移できませんでした。");

            float playerHealthBefore = playerHealth.CurrentHealth;
            float enemyHealthBefore = enemyHealth.CurrentHealth;
            Assert.That(player.TryStartAttack(out _), Is.False, "Victory中にPlayer攻撃が開始されました。");
            yield return new WaitForSeconds(1.0f);

            Assert.That(enemyMelee.IsAttacking, Is.False, "Victory中もEnemy攻撃が継続しています。");
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(playerHealthBefore), "Victory中にEnemy攻撃でKnightの体力が変化しました。");
            Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealthBefore), "Victory中にPlayer攻撃で敵の体力が変化しました。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Victory), "終局状態が後続入力で変更されました。");
        }

        [UnityTest]
        public IEnumerator EnemyWaitsSafelyWhenPathIsInvalid()
        {
            PlayerCombatController player = FindPlayer();
            FindFlow().TrySetState(GameplayState.Running);
            GameObject enemy = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemy, Is.Not.Null, "実シーンにEnemy_01がありません。");
            DisableOtherEnemies(enemy);

            EnemyBrain brain = enemy.GetComponent<EnemyBrain>();
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            Assert.That(brain, Is.Not.Null, "実シーンのEnemy_01にEnemyBrainがありません。");
            Assert.That(agent, Is.Not.Null, "実シーンのEnemy_01にNavMeshAgentがありません。");
            Vector3 positionBefore = enemy.transform.position;
            agent.areaMask = 0;

            yield return new WaitForSeconds(1.0f);

            Assert.That(Vector3.Distance(positionBefore, enemy.transform.position), Is.LessThan(0.1f), "無経路時に敵が最後の安全位置から移動しました。");
            Assert.That(agent.isStopped || !agent.isOnNavMesh, Is.True, "無経路時に敵が停止または安全状態へ移行していません。");
            Assert.That(brain.LastDiagnostic, Does.Contain("経路"), "無経路診断が日本語で記録されていません。");
            Assert.That(player.GetComponent<HealthComponent>().CurrentHealth, Is.GreaterThan(0f), "無経路待機中にKnightへ予期しないダメージが発生しました。");
        }

        private static PlayerCombatController FindPlayer()
        {
            PlayerCombatController player = UnityEngine.Object.FindAnyObjectByType<PlayerCombatController>();
            Assert.That(player, Is.Not.Null, "実シーンにPlayerCombatControllerがありません。");
            Assert.That(player.HealthComponent, Is.Not.Null, "実シーンのKnightにHealthComponentがありません。");
            return player;
        }

        private static GameFlowController FindFlow()
        {
            GameFlowController flow = UnityEngine.Object.FindAnyObjectByType<GameFlowController>();
            Assert.That(flow, Is.Not.Null, "実シーンにGameFlowControllerがありません。");
            return flow;
        }

        private static DamageService FindDamageService()
        {
            DamageService damage = UnityEngine.Object.FindAnyObjectByType<DamageService>();
            Assert.That(damage, Is.Not.Null, "実シーンにDamageServiceがありません。");
            return damage;
        }

        private static EnemyMeleeCombat PrepareSingleEnemyNearPlayer(PlayerCombatController player)
        {
            GameObject enemy = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemy, Is.Not.Null, "実シーンにEnemy_01がありません。");
            DisableOtherEnemies(enemy);
            EnemyBrain brain = enemy.GetComponent<EnemyBrain>();
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            Assert.That(agent, Is.Not.Null, "実シーンのEnemy_01にNavMeshAgentがありません。");
            Vector3 desired = player.transform.position + Vector3.forward * 2.10f;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 2f, agent.areaMask))
            {
                agent.Warp(hit.position);
            }
            else
            {
                enemy.transform.position = desired;
            }

            brain.enabled = true;
            agent.enabled = true;
            Physics.SyncTransforms();
            return enemy.GetComponent<EnemyMeleeCombat>();
        }

        private static EnemyMeleeCombat PrepareSingleEnemyForPlayerAttack(PlayerCombatController player)
        {
            GameObject enemy = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemy, Is.Not.Null, "実シーンにEnemy_01がありません。");
            DisableOtherEnemies(enemy);
            EnemyBrain brain = enemy.GetComponent<EnemyBrain>();
            EnemyMeleeCombat melee = enemy.GetComponent<EnemyMeleeCombat>();
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            brain.enabled = false;
            melee.enabled = false;
            agent.enabled = false;
            enemy.transform.position = player.transform.position + player.transform.forward * 1.15f;
            Physics.SyncTransforms();
            return melee;
        }

        private static void DisableAllEnemies()
        {
            GameObject enemies = GameObject.Find("Enemies");
            Assert.That(enemies, Is.Not.Null, "実シーンにEnemies階層がありません。");
            enemies.SetActive(false);
        }

        private static void DisableOtherEnemies(GameObject activeEnemy)
        {
            GameObject enemies = GameObject.Find("Enemies");
            Assert.That(enemies, Is.Not.Null, "実シーンにEnemies階層がありません。");
            enemies.SetActive(true);
            for (int index = 0; index < enemies.transform.childCount; index++)
            {
                GameObject child = enemies.transform.GetChild(index).gameObject;
                child.SetActive(child == activeEnemy);
            }
        }

        private static int CountActiveEnemies()
        {
            GameObject enemies = GameObject.Find("Enemies");
            Assert.That(enemies, Is.Not.Null, "実シーンにEnemies階層がありません。");
            int count = 0;
            for (int index = 0; index < enemies.transform.childCount; index++)
            {
                if (enemies.transform.GetChild(index).gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static IEnumerator WaitForCondition(Func<bool> condition, int maxFrames, string failureMessage)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (condition())
                {
                    yield break;
                }

                yield return new WaitForFixedUpdate();
            }

            Assert.Fail(failureMessage);
        }
    }
}
