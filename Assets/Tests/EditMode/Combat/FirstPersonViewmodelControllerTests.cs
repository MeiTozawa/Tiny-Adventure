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
    }
}
