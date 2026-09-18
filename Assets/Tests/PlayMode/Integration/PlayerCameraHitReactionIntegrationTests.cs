using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Unity.Cinemachine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// SampleSceneにおけるプレイヤー被弾時のカメラ動的トラウマスプリング
    /// （後仰Pitch、側傾Dutch Roll、FOV収縮）および視口武器Jolt反動のPlayMode統合テストです。
    /// 現代アクションゲーム水準の生理的被弾反動と、マウス照準完全復帰を検証します。
    /// </summary>
    public sealed class PlayerCameraHitReactionIntegrationTests
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

            // CinemachineBrainの初期ブレンドを即座に完了させてテスト状態を固定化
            CinemachineBrain brain = Object.FindAnyObjectByType<CinemachineBrain>();
            if (brain != null)
            {
                brain.ActiveBlend = null;
            }

            // 物理・Cinemachineの初期安定化
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerHurt_FromRight_RollsCameraLeft_AndRecoversSmoothly()
        {
            var camController = Object.FindAnyObjectByType<FirstPersonCameraController>();
            Assert.That(camController, Is.Not.Null, "FirstPersonCameraControllerが存在する必要があります。");

            var cmCam = GameObject.Find("Camera/CM_FirstPerson")?.GetComponent<CinemachineCamera>();
            Assert.That(cmCam, Is.Not.Null, "CM_FirstPersonのCinemachineCameraが存在する必要があります。");

            var feedback = Object.FindAnyObjectByType<CombatCameraFeedback>();
            Assert.That(feedback, Is.Not.Null, "CombatCameraFeedbackが存在する必要があります。");

            // 初期状態ではDutch傾斜が0
            Assert.That(Mathf.Abs(cmCam.Lens.Dutch), Is.LessThan(0.01f), "初期状態ではDutch側傾斜は0である必要があります。");

            // プレイヤー右側からの攻撃（局所受力ベクトルは左方向：X < 0）
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                null,
                null,
                default,
                Vector3.zero,
                new Vector3(-1f, 0f, 0f),
                false,
                isPlayerTarget: true,
                default);

            feedback.Play(request);

            // 1フレーム前進してピーク変位を捉える
            yield return null;

            // 右側被弾により左傾斜（Dutch < 0）および後仰（Pitch > 0）が発生することを検証
            Assert.That(cmCam.Lens.Dutch, Is.LessThan(-0.5f), "右側からの被弾によりCinemachineCamera.Lens.Dutchが左方向へ側傾斜する必要があります。");
            Assert.That(camController.CurrentTraumaPitch, Is.GreaterThan(0.2f), "被弾により後仰Pitchスプリングが跳ね上がる必要があります。");

            // 約0.40秒経過後、物理スプリングが滑らかに整定・収束することを検証
            yield return new WaitForSeconds(0.40f);

            Assert.That(Mathf.Abs(cmCam.Lens.Dutch), Is.LessThan(0.05f), "0.40秒後にはDutch側傾斜が滑らかに0へ復帰する必要があります。");
            Assert.That(Mathf.Abs(camController.CurrentTraumaPitch), Is.LessThan(0.05f), "0.40秒後には後仰角が滑らかに0へ復帰する必要があります。");
        }

        [UnityTest]
        public IEnumerator PlayerHurt_FromFront_PitchesCameraUpward_AndPreservesBaseAim()
        {
            var camController = Object.FindAnyObjectByType<FirstPersonCameraController>();
            Assert.That(camController, Is.Not.Null);

            var feedback = Object.FindAnyObjectByType<CombatCameraFeedback>();
            Assert.That(feedback, Is.Not.Null);

            // プレイヤーが特定角度（例：少し見下ろす）へマウス照準している状態
            camController.ApplyLookInput(new Vector2(0f, -200f));
            yield return null;

            float initialAimPitch = camController.CurrentPitch;
            Assert.That(Mathf.Abs(initialAimPitch), Is.GreaterThan(5f), "プレイヤーの基本照準角が設定されている必要があります。");

            // 正面からの攻撃（受力ベクトルは後方：Z < 0）
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                null,
                null,
                default,
                Vector3.zero,
                new Vector3(0f, 0f, -1f),
                false,
                isPlayerTarget: true,
                default);

            feedback.Play(request);

            // 被弾直後
            yield return null;

            // 実効俯仰角TotalPitchには後仰スプリングが加算されているが、マウス照準CurrentPitchは一切改変されない
            Assert.That(camController.CurrentPitch, Is.EqualTo(initialAimPitch).Within(0.001f),
                "被弾による後仰は動的オフセットであるため、プレイヤーのマウス照準角（CurrentPitch）は改変されません。");
            Assert.That(camController.TotalPitch, Is.GreaterThan(initialAimPitch + 0.2f),
                "実効俯仰角（TotalPitch）には被弾後仰角が加算されている必要があります。");

            // 減衰整定後
            yield return new WaitForSeconds(0.40f);

            Assert.That(camController.CurrentPitch, Is.EqualTo(initialAimPitch).Within(0.001f),
                "スプリング整定後もプレイヤーのマウス照準角は100%維持されます。");
            Assert.That(camController.TotalPitch, Is.EqualTo(initialAimPitch).Within(0.05f),
                "スプリング整定後は実効俯仰角もプレイヤーのマウス照準角に完全一致します。");
        }

        [UnityTest]
        public IEnumerator PlayerHurt_ViewmodelJoltsSynchronously_AndRecovers()
        {
            var player = GameObject.Find("Player");
            Assert.That(player, Is.Not.Null);

            var viewmodel = player.GetComponentInChildren<FirstPersonViewmodelController>(true);
            Assert.That(viewmodel, Is.Not.Null, "FirstPersonViewmodelControllerが存在する必要があります。");

            var feedback = Object.FindAnyObjectByType<CombatCameraFeedback>();
            Assert.That(feedback, Is.Not.Null);

            Camera mainCam = Camera.main;
            Assert.That(mainCam, Is.Not.Null);

            yield return null;

            // 被弾リクエストを発行
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                null,
                null,
                default,
                Vector3.zero,
                new Vector3(-1f, 0f, 0f),
                false,
                isPlayerTarget: true,
                default);

            feedback.Play(request);
            yield return null;

            // 視口武器のJolt反動（下沈・後退）が直ちに発火することを検証
            Assert.That(viewmodel.IsJolting, Is.True, "被弾により視口武器のJolt反動が活性化される必要があります。");
            Assert.That(viewmodel.CurrentJoltPositionOffset.y, Is.LessThan(-0.02f), "視口武器が下方に沈降する必要があります。");
            Assert.That(viewmodel.CurrentJoltPositionOffset.z, Is.LessThan(-0.01f), "視口武器が後方に後退反動する必要があります。");

            // 0.40秒後には待機位置へ滑らかに復帰
            yield return new WaitForSeconds(0.40f);

            Assert.That(viewmodel.IsJolting, Is.False, "0.40秒後には視口武器のJolt反動が完全に収束する必要があります。");
            Assert.That(viewmodel.CurrentJoltPositionOffset.sqrMagnitude, Is.LessThan(0.0001f),
                "減衰後は視口武器のJolt位置変位が完全に0へ復帰する必要があります。");
            Assert.That(Quaternion.Angle(viewmodel.CurrentJoltRotationOffset, Quaternion.identity), Is.LessThan(0.05f),
                "減衰後は視口武器のJolt回転傾斜が完全に0へ復帰する必要があります。");
        }

        private static void DisableAllSceneEnemies()
        {
            CombatantMarker[] markers = Object.FindObjectsByType<CombatantMarker>();
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
