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
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SampleScene_CombatFeedbackControllers_InitializedAndWired()
        {
            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot, Is.Not.Null, "SampleScene 内に GameRoot が見つかりません。");

            var feedbackController = gameRoot.GetComponent<CombatFeedbackController>();
            Assert.That(feedbackController, Is.Not.Null, "GameRoot に CombatFeedbackController が不足しています。");

            var animationFeedback = gameRoot.GetComponent<CombatAnimationFeedback>();
            Assert.That(animationFeedback, Is.Not.Null, "GameRoot に CombatAnimationFeedback が不足しています。");

            var vfxController = gameRoot.GetComponent<CombatVfxController>();
            Assert.That(vfxController, Is.Not.Null, "GameRoot に CombatVfxController が不足しています。");

            var audioController = gameRoot.GetComponent<CombatAudioController>();
            Assert.That(audioController, Is.Not.Null, "GameRoot に CombatAudioController が不足しています。");

            var deathRouter = gameRoot.GetComponent<CombatDeathAudioRouter>();
            Assert.That(deathRouter, Is.Not.Null, "GameRoot に CombatDeathAudioRouter が不足しています。");

            var hitStopController = gameRoot.GetComponent<HitStopController>();
            Assert.That(hitStopController, Is.Not.Null, "GameRoot に HitStopController が不足しています。");

            var cameraFeedback = gameRoot.GetComponent<CombatCameraFeedback>();
            Assert.That(cameraFeedback, Is.Not.Null, "GameRoot に CombatCameraFeedback が不足しています。");

            var timeSlowController = gameRoot.GetComponent<CombatTimeSlowController>();
            Assert.That(timeSlowController, Is.Not.Null, "GameRoot に CombatTimeSlowController が不足しています。");

            Assert.That(feedbackController.ProfileProvider, Is.Not.Null, "CombatFeedbackController には有効な ProfileProvider が必要です。");

            var cmCam = GameObject.Find("CM_FirstPerson");
            Assert.That(cmCam, Is.Not.Null, "CM_FirstPerson が見つかりません。");
            var impulseListener = cmCam.GetComponent<Unity.Cinemachine.CinemachineImpulseListener>();
            Assert.That(impulseListener, Is.Not.Null, $"{cmCam.name} には CinemachineImpulseListener が必要です。");

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
            Assert.That(enemyHealth.CurrentHealth, Is.GreaterThan(25f), "初期HPは通常ダメージ値より大きい必要があります。");

            CombatFeedbackRequest dispatchedRequest = default;
            bool wasDispatched = false;
            feedbackController.FeedbackDispatched += req =>
            {
                dispatchedRequest = req;
                wasDispatched = true;
            };

            player.enabled = false;
            Assert.That(player.TryStartAttack(out string startDiagnostic), Is.True, startDiagnostic);
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "攻撃ウィンドウを開始できません。");

            yield return new WaitForFixedUpdate();

            Assert.That(wasDispatched, Is.True, "プレイヤー攻撃が敵に命中した際、CombatFeedbackController が命中フィードバックをディスパッチする必要があります。");
            Assert.That(dispatchedRequest.HitType, Is.EqualTo(CombatHitType.Normal), "通常ダメージは通常フィードバックをトリガーする必要があります。");
            Assert.That(dispatchedRequest.IsPlayerAttack, Is.True);

            var timeSlow = GameObject.Find("GameRoot").GetComponent<CombatTimeSlowController>();
            Assert.That(timeSlow != null && timeSlow.IsSlowActive, Is.False, "通常命中時は60FPSと滑らかなカメラ追従を維持するためタイムスローは無効（False）である必要があります。");

            var enemyAnimator = enemyMelee.GetComponentInChildren<Animator>();
            Assert.That(enemyAnimator, Is.Not.Null, "EnemyMelee には Animator が必要です。");
            bool hitTriggered = enemyAnimator.GetBool("HitTrigger") ||
                                enemyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Hit") ||
                                (enemyAnimator.IsInTransition(0) && enemyAnimator.GetNextAnimatorStateInfo(0).IsName("Hit"));
            Assert.That(hitTriggered, Is.True, "プレイヤー攻撃が敵に命中した後、敵 Animator に HitTrigger が設定されているか Hit 状態へ遷移/進入中である必要があります。");

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

            // HPを20fに設定（プレイヤーの単発ダメージ25fで必ず致命打）
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
            Assert.That(player.AnimationEventBeginAttackWindow(), Is.True, "攻撃ウィンドウを開始できません。");

            yield return new WaitForFixedUpdate();

            Assert.That(wasDispatched, Is.True, "致命ダメージは命中フィードバックをトリガーする必要があります。");
            Assert.That(dispatchedRequest.HitType, Is.EqualTo(CombatHitType.Lethal), "致命ダメージは CombatHitType.Lethal とマークされる必要があります。");

            var timeSlow = GameObject.Find("GameRoot").GetComponent<CombatTimeSlowController>();
            Assert.That(timeSlow != null && timeSlow.IsSlowActive, Is.True, "致命撃破命中時は映画的タイムスローを有効化する必要があります。");

            player.AnimationEventEndAttackWindow();
            player.AnimationEventCompleteAttack();

            // 敵の死亡状態遷移を待機
            yield return null;

            Assert.That(deathRouter.HandledDeathCount, Is.GreaterThan(deathCountBefore), "DeathAudioRouter は敵の死亡を記録する必要があります。");

            yield return null;
        }

        private static EnemyMeleeCombat PrepareSingleEnemyForPlayerAttack(PlayerCombatController player)
        {
            GameObject enemy = GameObject.Find("Enemies/Enemy_01");
            Assert.That(enemy, Is.Not.Null, "実シーン内に Enemy_01 が見つかりません。");
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
            Assert.That(enemies, Is.Not.Null, "実シーン内に Enemies ルートノードが見つかりません。");
            enemies.SetActive(true);
            for (int index = 0; index < enemies.transform.childCount; index++)
            {
                GameObject child = enemies.transform.GetChild(index).gameObject;
                child.SetActive(child == activeEnemy);
            }
        }
    }
}
