using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// Playerの移動とyawの責務分離を編集モードで検証します。
    /// </summary>
    public sealed class PlayerControlContractTests
    {
        private GameObject playableFloor;
        private GameObject player;
        private GameObject movementCamera;
        private GameObject cameraRig;

        [SetUp]
        public void セットアップ()
        {
            playableFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playableFloor.name = "PlayableFloor";
            playableFloor.transform.position = new Vector3(0f, -0.5f, 0f);
            playableFloor.transform.localScale = new Vector3(20f, 1f, 20f);

            player = new GameObject("Player");
            player.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerController>();

            GameObject cameraTarget = new GameObject("CameraTarget");
            cameraTarget.transform.SetParent(player.transform, false);
            cameraTarget.transform.localPosition = Vector3.up * 1.5f;

            movementCamera = new GameObject("MovementCamera");
            movementCamera.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            cameraRig = new GameObject("CM_ThirdPerson");

            SetPrivateField(player.GetComponent<PlayerController>(), "movementCamera", movementCamera.AddComponent<Camera>());
            Physics.SyncTransforms();
        }

        [TearDown]
        public void 後始末()
        {
            Object.DestroyImmediate(cameraRig);
            Object.DestroyImmediate(movementCamera);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(playableFloor);
        }

        [TestCase(0f, 1f, "W")]
        [TestCase(0f, -1f, "S")]
        [TestCase(-1f, 0f, "A")]
        [TestCase(1f, 0f, "D")]
        public void WASD移動は平行移動するがPlayerのyawを変更しない(float horizontal, float vertical, string inputName)
        {
            PlayerController controller = player.GetComponent<PlayerController>();
            float initialYaw = player.transform.eulerAngles.y;

            controller.ProcessMovement(new Vector2(horizontal, vertical), 0.1f);

            Vector3 requestedHorizontalTranslation = controller.WorldMoveDirection * controller.MoveSpeed * 0.1f;
            requestedHorizontalTranslation.y = 0f;
            Assert.That(requestedHorizontalTranslation.sqrMagnitude, Is.GreaterThan(0.0001f), $"{inputName}入力はカメラ基準の水平方向への移動を生成します。");
            Assert.That(Mathf.DeltaAngle(initialYaw, player.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.0001f), $"{inputName}入力はPlayerのyawを変更しません。");
        }

        [Test]
        public void 水平Look入力は移動入力なしでもPlayerのyawを更新する()
        {
            LogAssert.Expect(LogType.Error, "[カメラ診断] CM_ThirdPersonに必要なCinemachine第三人称リグがありません。");
            ThirdPersonCameraController controller = cameraRig.AddComponent<ThirdPersonCameraController>();
            Assert.That(controller.ResolvePlayerCameraTarget(), Is.True);
            float initialYaw = player.transform.eulerAngles.y;

            controller.ApplyLookInput(new Vector2(50f, 0f));

            Assert.That(Mathf.DeltaAngle(initialYaw, player.transform.eulerAngles.y), Is.EqualTo(5f).Within(0.0001f));
            Assert.That(controller.CurrentYaw, Is.EqualTo(5f).Within(0.0001f));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName}フィールドが見つかりません。");
            field.SetValue(target, value);
        }
    }
}
