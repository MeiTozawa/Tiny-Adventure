using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    [TestFixture]
    public sealed class CombatFeedbackIntegrationTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
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
        public IEnumerator SampleScene_CombatFeedbackControllers_InitializedAndWired()
        {
            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot, Is.Not.Null, "SampleScene 中未找到 GameRoot。");

            var feedbackController = gameRoot.GetComponent<CombatFeedbackController>();
            Assert.That(feedbackController, Is.Not.Null, "GameRoot 缺少 CombatFeedbackController。");

            var vfxController = gameRoot.GetComponent<CombatVfxController>();
            Assert.That(vfxController, Is.Not.Null, "GameRoot 缺少 CombatVfxController。");

            var audioController = gameRoot.GetComponent<CombatAudioController>();
            Assert.That(audioController, Is.Not.Null, "GameRoot 缺少 CombatAudioController。");

            var deathRouter = gameRoot.GetComponent<CombatDeathAudioRouter>();
            Assert.That(deathRouter, Is.Not.Null, "GameRoot 缺少 CombatDeathAudioRouter。");

            var hitStopController = gameRoot.GetComponent<HitStopController>();
            Assert.That(hitStopController, Is.Not.Null, "GameRoot 缺少 HitStopController。");

            var cameraFeedback = gameRoot.GetComponent<CombatCameraFeedback>();
            Assert.That(cameraFeedback, Is.Not.Null, "GameRoot 缺少 CombatCameraFeedback。");

            Assert.That(feedbackController.ProfileProvider, Is.Not.Null, "CombatFeedbackController 必须具有有效的 ProfileProvider。");

            var cmThirdPerson = GameObject.Find("CM_ThirdPerson");
            Assert.That(cmThirdPerson, Is.Not.Null, "未找到 CM_ThirdPerson。");
            var impulseListener = cmThirdPerson.GetComponent<Unity.Cinemachine.CinemachineImpulseListener>();
            Assert.That(impulseListener, Is.Not.Null, "CM_ThirdPerson 必须具有 CinemachineImpulseListener。");

            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerMeleeAttack_HitsEnemy_AndDispatchesCombatFeedback()
        {
            var playerGo = GameObject.Find("Player");
            Assert.That(playerGo, Is.Not.Null);
            var player = playerGo.GetComponent<PlayerCombatController>();
            Assert.That(player, Is.Not.Null);

            var feedbackController = GameObject.FindAnyObjectByType<CombatFeedbackController>();
            Assert.That(feedbackController, Is.Not.Null);

            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyForPlayerAttack(player);
            var enemyHealth = enemyMelee.GetComponent<HealthComponent>();
            Assert.That(enemyHealth.CurrentHealth, Is.GreaterThan(25f), "初始血量必须大于普通伤害值。");

            CombatFeedbackRequest dispatchedRequest = default;
            bool wasDispatched = false;
            feedbackController.FeedbackDispatched += req =>
            {
                dispatchedRequest = req;
                wasDispatched = true;
            };

            player.enabled = false;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "无法开启攻击窗口。");

            yield return new WaitForFixedUpdate();

            Assert.That(wasDispatched, Is.True, "玩家攻击命中敌人后，CombatFeedbackController 必须分发命中反馈。");
            Assert.That(dispatchedRequest.HitType, Is.EqualTo(CombatHitType.Normal), "普通伤害应触发普通反馈。");
            Assert.That(dispatchedRequest.IsPlayerAttack, Is.True);

            player.AnimationEventEndAttackWindow();
            player.AnimationEventCompleteAttack();

            yield return null;
        }

        [UnityTest]
        public IEnumerator LethalHit_DispatchesLethalFeedback_AndDeathAudioRouterRecordsDeath()
        {
            var playerGo = GameObject.Find("Player");
            Assert.That(playerGo, Is.Not.Null);
            var player = playerGo.GetComponent<PlayerCombatController>();
            Assert.That(player, Is.Not.Null);

            var feedbackController = GameObject.FindAnyObjectByType<CombatFeedbackController>();
            var deathRouter = GameObject.FindAnyObjectByType<CombatDeathAudioRouter>();
            Assert.That(feedbackController, Is.Not.Null);
            Assert.That(deathRouter, Is.Not.Null);

            EnemyMeleeCombat enemyMelee = PrepareSingleEnemyForPlayerAttack(player);
            var enemyHealth = enemyMelee.GetComponent<HealthComponent>();

            // 将血量设置为 20f（玩家单次伤害 25f，必定致死）
            Assert.That(enemyHealth.Configure(20f, out string healthDiag), Is.True, healthDiag);

            CombatFeedbackRequest dispatchedRequest = default;
            bool wasDispatched = false;
            feedbackController.FeedbackDispatched += req =>
            {
                dispatchedRequest = req;
                wasDispatched = true;
            };

            int deathCountBefore = deathRouter.HandledDeathCount;

            player.enabled = false;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "无法开启攻击窗口。");

            yield return new WaitForFixedUpdate();

            Assert.That(wasDispatched, Is.True, "致死伤害必须触发命中反馈。");
            Assert.That(dispatchedRequest.HitType, Is.EqualTo(CombatHitType.Lethal), "致死伤害应标记为 CombatHitType.Lethal。");

            player.AnimationEventEndAttackWindow();
            player.AnimationEventCompleteAttack();

            // 等待敌人死亡状态转换
            yield return null;

            Assert.That(deathRouter.HandledDeathCount, Is.GreaterThan(deathCountBefore), "DeathAudioRouter 必须记录敌人死亡。");

            yield return null;
        }

        private static EnemyMeleeCombat PrepareSingleEnemyForPlayerAttack(PlayerCombatController player)
        {
            GameObject enemy = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemy, Is.Not.Null, "实场景中未找到 Enemy_01。");
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

        private static void DisableOtherEnemies(GameObject activeEnemy)
        {
            GameObject enemies = GameObject.Find("Enemies");
            Assert.That(enemies, Is.Not.Null, "实场景中未找到 Enemies 根节点。");
            enemies.SetActive(true);
            for (int index = 0; index < enemies.transform.childCount; index++)
            {
                GameObject child = enemies.transform.GetChild(index).gameObject;
                child.SetActive(child == activeEnemy);
            }
        }
    }
}
