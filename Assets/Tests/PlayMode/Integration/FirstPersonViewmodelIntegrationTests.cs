using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// SampleSceneにおける第一人称視口武器（First-Person Viewmodel Sword）の
    /// プレイモード統合テストです。
    /// 視線仰俯角（Pitch）変更時にも画面内の指定領域を維持すること、および攻撃連動を検証します。
    /// </summary>
    public sealed class FirstPersonViewmodelIntegrationTests
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
        public IEnumerator ViewmodelSword_RemainsInScreenView_AcrossPitchRange()
        {
            var player = GameObject.Find("Player");
            Assert.That(player, Is.Not.Null, "SampleSceneにPlayerオブジェクトがありません。");

            var viewmodel = player.GetComponentInChildren<FirstPersonViewmodelController>(true);
            Assert.That(viewmodel, Is.Not.Null, "PlayerにFirstPersonViewmodelControllerがありません。");

            var camController = Object.FindAnyObjectByType<FirstPersonCameraController>();
            Assert.That(camController, Is.Not.Null, "FirstPersonCameraControllerがありません。");

            Camera mainCam = Camera.main;
            Assert.That(mainCam, Is.Not.Null, "Camera.mainがありません。");

            Transform swordSocket = player.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "SwordSocket");
            Assert.That(swordSocket, Is.Not.Null, "SwordSocketが見つかりません。");

            // 安定化待機
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            // 1. 平視状態での視口座標検証
            Vector3 vpCenter = mainCam.WorldToViewportPoint(swordSocket.position);
            Assert.That(vpCenter.z, Is.GreaterThan(0.1f), "剣はカメラの前方に存在する必要があります。");
            Assert.That(vpCenter.x, Is.InRange(0.40f, 0.95f), "平視時、剣は画面右半面に存在する必要があります。");
            Assert.That(vpCenter.y, Is.InRange(0.00f, 0.60f), "平視時、剣は画面下部に存在する必要があります。");

            // 2. 仰視状態（上を大きく見上げる）
            camController.ApplyLookInput(new Vector2(0f, 1500f));
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Vector3 vpUp = mainCam.WorldToViewportPoint(swordSocket.position);
            Assert.That(vpUp.z, Is.GreaterThan(0.1f), "仰視時も剣はカメラの前方に存在する必要があります。");
            Assert.That(vpUp.x, Is.InRange(0.40f, 0.95f), "仰視時も画面上の水平位置を維持する必要があります。");
            Assert.That(vpUp.y, Is.InRange(0.00f, 0.60f), "仰視時も剣が画面下外へ脱落せず維持される必要があります。");

            // 3. 俯視状態（地面を大きく見下ろす）
            camController.ApplyLookInput(new Vector2(0f, -3000f));
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Vector3 vpDown = mainCam.WorldToViewportPoint(swordSocket.position);
            Assert.That(vpDown.z, Is.GreaterThan(0.1f), "俯視時も剣はカメラの前方に存在する必要があります。");
            Assert.That(vpDown.x, Is.InRange(0.40f, 0.95f), "俯視時も画面上の水平位置を維持する必要があります。");
            Assert.That(vpDown.y, Is.InRange(0.00f, 0.60f), "俯視時も剣が画面上部へ逸脱せず維持される必要があります。");
        }

        [UnityTest]
        public IEnumerator ViewmodelSword_TriggersAttackKinetics_OnPlayerAttack()
        {
            var combat = Object.FindAnyObjectByType<PlayerCombatController>();
            Assert.That(combat, Is.Not.Null, "PlayerCombatControllerが見つかりません。");

            var viewmodel = combat.GetComponentInChildren<FirstPersonViewmodelController>(true);
            Assert.That(viewmodel, Is.Not.Null, "FirstPersonViewmodelControllerが見つかりません。");

            // 初段攻撃開始
            Assert.That(combat.TryStartAttack(out string diag), Is.True, diag);
            yield return null;

            Assert.That(viewmodel.IsAttacking, Is.True, "攻撃開始と同時にViewmodelControllerがIsAttacking状態になる必要があります。");
            Assert.That(viewmodel.CurrentAttackComboIndex, Is.EqualTo(0));

            // 出刀完了を待機
            float elapsed = 0f;
            while (viewmodel.IsAttacking && elapsed < 1.0f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.That(viewmodel.IsAttacking, Is.False, "出刀時間経過後は待機姿勢に復帰する必要があります。");
        }

        private static void DisableAllSceneEnemies()
        {
            CombatantMarker[] markers = Object.FindObjectsByType<CombatantMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                CombatantMarker marker = markers[i];
                if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Enemy)
                {
                    marker.gameObject.SetActive(false);
                }
            }
        }
    }
}
