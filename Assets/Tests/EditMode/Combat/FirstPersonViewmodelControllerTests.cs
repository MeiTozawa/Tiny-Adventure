using NUnit.Framework;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 第一人称視口武器コントローラー（FirstPersonViewmodelController）の
    /// カメラ追従、基準オフセット、LookSway慣性、歩行Bobbing挙動を検証します。
    /// </summary>
    public sealed class FirstPersonViewmodelControllerTests
    {
        private GameObject cameraObject;
        private Camera testCamera;
        private GameObject viewmodelObject;
        private FirstPersonViewmodelController controller;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("TestCamera");
            testCamera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 1.4f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(15f, 30f, 0f); // 俯仰15度、旋回30度

            viewmodelObject = new GameObject("TestViewmodel");
            controller = viewmodelObject.AddComponent<FirstPersonViewmodelController>();
            controller.SetTargetCamera(testCamera);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(viewmodelObject);
            Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void RestPosition_FollowsCameraPositionAndRotationWithOffset()
        {
            Vector3 customOffset = new Vector3(0.25f, -0.20f, 0.50f);
            Vector3 customRotOffset = new Vector3(5f, -10f, 0f);
            controller.DefaultPositionOffset = customOffset;
            controller.DefaultRotationOffset = customRotOffset;

            controller.Evaluate(0.1f);

            Vector3 expectedWorldPos = testCamera.transform.TransformPoint(customOffset);
            Quaternion expectedWorldRot = testCamera.transform.rotation * Quaternion.Euler(customRotOffset);

            Assert.That(Vector3.Distance(viewmodelObject.transform.position, expectedWorldPos), Is.LessThan(0.01f),
                "視口武器はカメラの指定ローカルオフセット位置に追従する必要があります。");
            Assert.That(Quaternion.Angle(viewmodelObject.transform.rotation, expectedWorldRot), Is.LessThan(0.1f),
                "視口武器はカメラの回転にローカル回転オフセットを加えた姿勢に追従する必要があります。");
        }

        [Test]
        public void RestPosition_BladeTipExtendsForwardIntoViewport()
        {
            // 剣の階層（SwordSocket -> SwordVisual -> default）を模倣
            var socket = new GameObject("SwordSocket").transform;
            socket.SetParent(viewmodelObject.transform, false);
            var visual = new GameObject("SwordVisual").transform;
            visual.SetParent(socket, false);
            visual.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var tipMarker = new GameObject("TipMarker").transform;
            tipMarker.SetParent(visual, false);
            tipMarker.localPosition = new Vector3(0f, 1.8f, 0f); // 刃先

            controller.Evaluate(0.01f);

            Vector3 tipCamSpace = testCamera.transform.InverseTransformPoint(tipMarker.position);
            Vector3 tipVp = testCamera.WorldToViewportPoint(tipMarker.position);

            // 刃先は必ずカメラの前方（Z > 1.0m）に伸展し、後方へ逸脱してはならない
            Assert.That(tipCamSpace.z, Is.GreaterThan(1.0f), "剣先はカメラの前方（+Z方向）に伸展する必要があります。");
            Assert.That(tipVp.x, Is.InRange(0.40f, 0.80f), "剣先は視口中央から右半面に位置する必要があります。");
            Assert.That(tipVp.y, Is.InRange(0.35f, 0.85f), "剣先は視口内に収まっている必要があります。");
        }

        [Test]
        public void CameraPitchAndYaw_DirectlyTransfersToViewmodel()
        {
            // カメラを大きく見上げる（-60度）
            cameraObject.transform.rotation = Quaternion.Euler(-60f, 90f, 0f);
            controller.Evaluate(0.1f);

            // カメラ空間でのローカル位置が初期設定と一致しているか検証（＝画面上の相対位置が変わらない）
            Vector3 localToCam = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
            Assert.That(Vector3.Distance(localToCam, controller.DefaultPositionOffset), Is.LessThan(0.01f),
                "カメラがどのような仰角・旋回角であっても、画面上の視口位置は維持される必要があります。");
        }

        [Test]
        public void LookSway_DisplacesViewmodelWithinClampedLimits()
        {
            controller.Evaluate(0.1f);
            Vector3 baseLocalPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);

            // 急激なマウス視線移動入力を与える
            controller.ApplyLookInput(new Vector2(100f, 50f));
            controller.Evaluate(0.016f);

            Vector3 swayedLocalPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
            float swayDisplacement = Vector3.Distance(baseLocalPos, swayedLocalPos);

            Assert.That(swayDisplacement, Is.GreaterThan(0.001f), "急激な視線移動でSway慣性変位が発生する必要があります。");
            Assert.That(swayDisplacement, Is.LessThanOrEqualTo(controller.MaxSwayDistance + 0.005f),
                "Sway変位は画面外への逸脱を防ぐためMaxSwayDistance内に制限される必要があります。");
        }

        [Test]
        public void MovementBobbing_ProducesSubtleOscillation_WhenMoving()
        {
            controller.SetMovementState(true, 1f);

            Vector3 prevLocalPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
            bool oscillationDetected = false;

            for (int i = 0; i < 20; i++)
            {
                controller.Evaluate(0.05f);
                Vector3 currentLocalPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
                if (Mathf.Abs(currentLocalPos.y - prevLocalPos.y) > 0.0005f)
                {
                    oscillationDetected = true;
                    break;
                }
                prevLocalPos = currentLocalPos;
            }

            Assert.That(oscillationDetected, Is.True, "移動中に步伐による微小なボビング振動（Bobbing）が発生する必要があります。");
        }

        [Test]
        public void TriggerAttack_SetsIsAttackingTrue_AndAdvancesComboIndex()
        {
            Assert.That(controller.IsAttacking, Is.False);

            controller.TriggerAttack(0, 1f);
            Assert.That(controller.IsAttacking, Is.True);
            Assert.That(controller.CurrentAttackComboIndex, Is.EqualTo(0));

            controller.TriggerAttack(1, 1f);
            Assert.That(controller.CurrentAttackComboIndex, Is.EqualTo(1));

            controller.TriggerAttack(2, 1f);
            Assert.That(controller.CurrentAttackComboIndex, Is.EqualTo(2));
        }

        [Test]
        public void HorizontalSlash_ProducesHorizontalSweepAcrossCenter()
        {
            controller.TriggerAttack(0, 1f);

            float minX = float.MaxValue;
            float maxX = float.MinValue;

            // 出刀の全期間をサンプリング
            for (float t = 0f; t <= 0.55f; t += 0.02f)
            {
                controller.Evaluate(0.02f);
                Vector3 localPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
                if (localPos.x < minX) minX = localPos.x;
                if (localPos.x > maxX) maxX = localPos.x;
            }

            // 右側から左側へ水平に横薙ぎスイープしたことを検証
            Assert.That(maxX - minX, Is.GreaterThan(0.20f), "横薙ぎ攻撃は水平方向に十分な振り抜き軌跡を描く必要があります。");
            Assert.That(minX, Is.LessThan(controller.DefaultPositionOffset.x - 0.15f), "横薙ぎ攻撃は准星中心部を横切って左側へ抜ける必要があります。");
        }

        [Test]
        public void VerticalSlash_ProducesDownwardChop()
        {
            controller.TriggerAttack(1, 1f);

            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (float t = 0f; t <= 0.55f; t += 0.02f)
            {
                controller.Evaluate(0.02f);
                Vector3 localPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
                if (localPos.y < minY) minY = localPos.y;
                if (localPos.y > maxY) maxY = localPos.y;
            }

            // 上方から下方へ振り下ろしたことを検証
            Assert.That(maxY - minY, Is.GreaterThan(0.20f), "縦斬り攻撃は上方から下方へ十分な振り下ろし軌跡を描く必要があります。");
        }

        [Test]
        public void Thrust_ProducesForwardReachAlongZAxis()
        {
            controller.TriggerAttack(2, 1f);

            float maxZ = float.MinValue;

            for (float t = 0f; t <= 0.55f; t += 0.02f)
            {
                controller.Evaluate(0.02f);
                Vector3 localPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
                if (localPos.z > maxZ) maxZ = localPos.z;
            }

            // 前方への突き刺し変位を検証
            float forwardReach = maxZ - controller.DefaultPositionOffset.z;
            Assert.That(forwardReach, Is.GreaterThan(0.25f), "突進刺突は前方（+Z方向）へ大きく突き出る必要があります。");
        }

        [Test]
        public void AttackSpeedMultiplier_ShortensAttackDuration()
        {
            // 2倍速で第1段を実行
            controller.TriggerAttack(0, 2f);
            float normalDuration = controller.BaseAttackDuration;
            float expectedDuration = normalDuration / 2f;

            // 期待時間の80%経過時点ではまだ攻撃中
            controller.Evaluate(expectedDuration * 0.8f);
            Assert.That(controller.IsAttacking, Is.True, "攻撃倍速に応じた完了時間前は攻撃中である必要があります。");

            // 期待時間を超えたら攻撃完了
            controller.Evaluate(expectedDuration * 0.4f);
            Assert.That(controller.IsAttacking, Is.False, "攻撃倍速に応じた完了時間経過後は待機姿勢に復帰する必要があります。");
        }

        [Test]
        public void CancelAttack_ImmediatelyResetsAttackState()
        {
            controller.TriggerAttack(0, 1f);
            controller.Evaluate(0.1f);
            Assert.That(controller.IsAttacking, Is.True);

            controller.CancelAttack();
            Assert.That(controller.IsAttacking, Is.False, "CancelAttack呼び出し後は即座に非攻撃状態になる必要があります。");
        }

        [Test]
        public void AttackLifecycle_ActivatesAndDeactivatesSwordTrail()
        {
            var trailGo = new GameObject("TestTrail");
            trailGo.transform.SetParent(viewmodelObject.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.emitting = false;

            controller.TriggerAttack(0, 1f);
            Assert.That(trail.emitting, Is.True, "出刀開始時にTrailRendererのemittingがtrueになる必要があります。");

            controller.Evaluate(controller.BaseAttackDuration + 0.1f);
            Assert.That(trail.emitting, Is.False, "出刀終了時にTrailRendererのemittingがfalseになる必要があります。");
        }

        [Test]
        public void CancelAttack_DeactivatesSwordTrail()
        {
            var trailGo = new GameObject("TestTrail");
            trailGo.transform.SetParent(viewmodelObject.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();

            controller.TriggerAttack(0, 1f);
            Assert.That(trail.emitting, Is.True);

            controller.CancelAttack();
            Assert.That(trail.emitting, Is.False, "出刀中断時にTrailRendererのemittingがfalseになる必要があります。");
        }

        [Test]
        public void TriggerImpactJolt_CausesViewmodelDisplacement_AndSettlesSmoothly()
        {
            controller.Evaluate(0.016f);
            Vector3 restingPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);

            // 右側からの被弾（局所受力ベクトルは左方向：X < 0）
            controller.TriggerImpactJolt(new Vector3(-1f, 0f, 0f), 1f);

            Assert.That(controller.IsJolting, Is.True, "被弾インパルス後はJolt状態が有効になる必要があります。");
            Assert.That(controller.CurrentJoltPositionOffset.y, Is.LessThan(-0.03f), "被弾により武器が下方に沈降（Y < -0.03m）する必要があります。");
            Assert.That(controller.CurrentJoltPositionOffset.z, Is.LessThan(-0.02f), "被弾により武器が後退（Z < -0.02m）する必要があります。");
            Assert.That(controller.CurrentJoltPositionOffset.x, Is.LessThan(0f), "右側からの打撃により武器が左方へ変位する必要があります。");

            controller.Evaluate(0.016f);
            Vector3 joltedPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
            Assert.That(joltedPos.y, Is.LessThan(restingPos.y - 0.02f), "カメラ空間での武器描画位置に沈降変位が反映される必要があります。");

            // 0.4秒間評価して待機位置へ平滑復帰
            for (int i = 0; i < 25; i++)
            {
                controller.Evaluate(0.016f);
            }

            Assert.That(controller.IsJolting, Is.False, "0.4秒後には受撃反動が完全に収束する必要があります。");
            Vector3 recoveredPos = testCamera.transform.InverseTransformPoint(viewmodelObject.transform.position);
            Assert.That(Vector3.Distance(recoveredPos, restingPos), Is.LessThan(0.005f), "減衰後は待機位置へ完全に復帰する必要があります。");
        }

        [Test]
        public void ResetImpactJolt_ImmediatelyClearsJoltState()
        {
            controller.TriggerImpactJolt(new Vector3(0f, 0f, -1f), 1.5f);
            Assert.That(controller.IsJolting, Is.True);

            controller.ResetImpactJolt();
            Assert.That(controller.IsJolting, Is.False, "ResetImpactJolt呼び出し後は即座に静止状態へリセットされる必要があります。");
            Assert.That(controller.CurrentJoltPositionOffset, Is.EqualTo(Vector3.zero));
            Assert.That(controller.CurrentJoltRotationOffset, Is.EqualTo(Quaternion.identity));
        }
    }
}
