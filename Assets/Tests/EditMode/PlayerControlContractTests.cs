using System;
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
        private const string CinemachineAssemblyName = "Unity.Cinemachine";
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
            UnityEngine.Object.DestroyImmediate(cameraRig);
            UnityEngine.Object.DestroyImmediate(movementCamera);
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(playableFloor);
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
            LogAssert.Expect(LogType.Error, "[カメラ診断] CM_ThirdPersonに必要なCinemachine第三人称リグ（OrbitalFollow/RotationComposer/Deoccluder）がありません。");
            ThirdPersonCameraController controller = cameraRig.AddComponent<ThirdPersonCameraController>();
            Assert.That(controller.ResolvePlayerCameraTarget(), Is.True);
            float initialYaw = player.transform.eulerAngles.y;
            float initialHorizontalAxis = controller.CurrentYaw;

            controller.ApplyLookInput(new Vector2(50f, 0f));

            Assert.That(Mathf.DeltaAngle(initialYaw, player.transform.eulerAngles.y), Is.EqualTo(5f).Within(0.0001f), "水平Look入力はPlayerのyawを回転させます。");
            Assert.That(controller.CurrentYaw, Is.EqualTo(initialHorizontalAxis).Within(0.0001f), "HorizontalAxisはLockToTargetWithWorldUp基準の固定オフセットのままで、水平Look入力では二重に加算されません。");
        }

        [Test]
        public void 水平Look入力は正式リグでもOrbitalFollowのHorizontalAxisを二重回転しない()
        {
            ThirdPersonCameraController controller = AddFullCinemachineRig();
            Assert.That(controller.ResolvePlayerCameraTarget(), Is.True);
            float initialYaw = player.transform.eulerAngles.y;
            float initialHorizontalAxis = controller.CurrentYaw;

            controller.ApplyLookInput(new Vector2(50f, 0f));

            Assert.That(Mathf.DeltaAngle(initialYaw, player.transform.eulerAngles.y), Is.EqualTo(5f).Within(0.0001f), "水平Look入力はPlayerのyawのみを回転させます。");
            Assert.That(controller.CurrentYaw, Is.EqualTo(initialHorizontalAxis).Within(0.0001f), "OrbitalFollowのHorizontalAxisは水平Look入力で加算されず、二重回転しません。");
        }

        [Test]
        public void 垂直Look入力はカメラをPlayerカメラターゲット中心に軌道させ注視を維持する()
        {
            ThirdPersonCameraController controller = AddFullCinemachineRig();
            Assert.That(controller.ResolvePlayerCameraTarget(), Is.True);
            Component cinemachineCamera = controller.Rig;
            Transform cameraTarget = controller.PlayerCameraTarget;

            Vector3 previousPosition = EvaluateCameraState(cinemachineCamera, out Quaternion previousOrientation);
            float previousDot = ForwardDotToTarget(previousPosition, previousOrientation, cameraTarget.position);
            Assert.That(previousDot, Is.GreaterThan(0.98f), "初期状態でカメラはPlayerのカメラターゲットを正面に捉えます。");

            // pitchLimitsのデフォルト下限(-30)まで下げてから上限(65)まで段階的に上げ、
            // 各段階でカメラ位置が変化しながらも注視方向がPlayerカメラターゲットへ向いたままであることを確認する。
            for (int i = 0; i < 50; i++)
            {
                controller.ApplyLookInput(new Vector2(0f, 400f));
            }

            bool positionChanged = false;
            float minimumDot = 1f;
            Vector3 lastPosition = EvaluateCameraState(cinemachineCamera, out Quaternion lastOrientation);

            for (int step = 0; step < 20; step++)
            {
                controller.ApplyLookInput(new Vector2(0f, -30f));
                Vector3 currentPosition = EvaluateCameraState(cinemachineCamera, out Quaternion currentOrientation);
                float dot = ForwardDotToTarget(currentPosition, currentOrientation, cameraTarget.position);
                minimumDot = Mathf.Min(minimumDot, dot);

                if ((currentPosition - lastPosition).sqrMagnitude > 0.0001f)
                {
                    positionChanged = true;
                }

                lastPosition = currentPosition;
            }

            Assert.That(controller.CurrentPitch, Is.GreaterThan(0f), "垂直Look入力の累積によりpitchが変化しています。");
            Assert.That(positionChanged, Is.True, "OrbitalFollowはpitch変化に応じてカメラ位置（軌道）を移動させます。");
            Assert.That(minimumDot, Is.GreaterThan(0.95f), "RotationComposerによりpitchが変化してもカメラは常にPlayerのカメラターゲットを注視します。");
        }

        private ThirdPersonCameraController AddFullCinemachineRig()
        {
            AddCinemachineComponent(cameraRig, "Unity.Cinemachine.CinemachineCamera");
            AddCinemachineComponent(cameraRig, "Unity.Cinemachine.CinemachineOrbitalFollow");
            AddCinemachineComponent(cameraRig, "Unity.Cinemachine.CinemachineRotationComposer");
            AddCinemachineComponent(cameraRig, "Unity.Cinemachine.CinemachineDeoccluder");
            return cameraRig.AddComponent<ThirdPersonCameraController>();
        }

        private static Component AddCinemachineComponent(GameObject target, string fullTypeName)
        {
            Type componentType = Type.GetType($"{fullTypeName}, {CinemachineAssemblyName}");
            Assert.That(componentType, Is.Not.Null, $"{fullTypeName}が解決できません。Cinemachineパッケージが必要です。");
            return target.AddComponent(componentType);
        }

        private static Vector3 EvaluateCameraState(Component cinemachineCamera, out Quaternion orientation)
        {
            Type cameraType = cinemachineCamera.GetType();
            MethodInfo updateMethod = cameraType.GetMethod("InternalUpdateCameraState", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(updateMethod, Is.Not.Null, "InternalUpdateCameraStateが見つかりません。");
            updateMethod.Invoke(cinemachineCamera, new object[] { Vector3.up, 0.016f });

            PropertyInfo stateProperty = cameraType.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
            object state = stateProperty.GetValue(cinemachineCamera);
            Type stateType = state.GetType();
            Vector3 position = (Vector3)stateType.GetField("RawPosition").GetValue(state);
            orientation = (Quaternion)stateType.GetField("RawOrientation").GetValue(state);
            return position;
        }

        private static float ForwardDotToTarget(Vector3 cameraPosition, Quaternion cameraOrientation, Vector3 targetPosition)
        {
            Vector3 forward = cameraOrientation * Vector3.forward;
            Vector3 toTarget = (targetPosition - cameraPosition).normalized;
            return Vector3.Dot(forward, toTarget);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName}フィールドが見つかりません。");
            field.SetValue(target, value);
        }
    }
}
