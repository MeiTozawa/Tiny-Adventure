using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 第一人称（FPS）カメラリグの追従、Look入力、視点切替、および頭部メッシュのShadowsOnly処理を編集モードで検証します。
    /// </summary>
    public sealed class FirstPersonCameraContractTests
    {
        private const string CinemachineAssemblyName = "Unity.Cinemachine";
        private GameObject player;
        private GameObject cameraTarget;
        private GameObject fpRig;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Player");
            player.transform.position = new Vector3(1f, 0f, 2f);
            player.transform.rotation = Quaternion.Euler(0f, 40f, 0f);

            cameraTarget = new GameObject("CameraTarget");
            cameraTarget.transform.SetParent(player.transform, false);
            cameraTarget.transform.localPosition = new Vector3(0f, 1.4f, 0f);

            fpRig = new GameObject("CM_FirstPerson");
            AddCinemachineComponent(fpRig, "Unity.Cinemachine.CinemachineCamera");
            AddCinemachineComponent(fpRig, "Unity.Cinemachine.CinemachineHardLockToTarget");
            AddCinemachineComponent(fpRig, "Unity.Cinemachine.CinemachinePanTilt");
            AddCinemachineComponent(fpRig, "Unity.Cinemachine.CinemachineImpulseListener");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(fpRig);
            UnityEngine.Object.DestroyImmediate(player);
        }

        [Test]
        public void FirstPerson_HardLocksPositionToCameraTargetWithoutLag()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.ResolvePlayerCameraTarget();
            controller.ApplyRigConfiguration();

            Component fpCam = controller.Rig;
            Assert.That(fpCam, Is.Not.Null, "第一人称リグが解決されている必要があります。");

            Vector3 evaluatedPos = EvaluateCameraState(fpCam, out _);
            Assert.That((evaluatedPos - cameraTarget.transform.position).sqrMagnitude, Is.LessThan(0.001f),
                "第一人称カメラの位置はCameraTarget（プレイヤー眼部）に遅延なく一致する必要があります。");

            // プレイヤーを移動させて追従を再検証
            player.transform.position = new Vector3(10f, 5f, -8f);
            Physics.SyncTransforms();

            Vector3 movedPos = EvaluateCameraState(fpCam, out _);
            Assert.That((movedPos - cameraTarget.transform.position).sqrMagnitude, Is.LessThan(0.001f),
                "プレイヤー移動後も第一人称カメラは即座に位置を一致させます。");
        }

        [Test]
        public void HorizontalLook_RotatesPlayerYawDirectly()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.ResolvePlayerCameraTarget();
            controller.ApplyRigConfiguration();

            float initialYaw = player.transform.eulerAngles.y;
            controller.ApplyLookInput(new Vector2(60f, 0f));

            float deltaYaw = Mathf.DeltaAngle(initialYaw, player.transform.eulerAngles.y);
            Assert.That(deltaYaw, Is.EqualTo(6f).Within(0.001f), "水平Look入力はプレイヤーの身体（Yaw）を直接回転させます。");
        }

        [Test]
        public void VerticalLook_AdjustsPanTiltWithinClampedRange()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.ResolvePlayerCameraTarget();
            controller.ApplyRigConfiguration();

            // 大幅に上を見上げる入力
            controller.ApplyLookInput(new Vector2(0f, 2000f));
            Assert.That(controller.CurrentPitch, Is.EqualTo(-80f).Within(0.01f), "仰角の上限は-80度にクランプされます。");

            // 大幅に見下ろす入力
            controller.ApplyLookInput(new Vector2(0f, -4000f));
            Assert.That(controller.CurrentPitch, Is.EqualTo(80f).Within(0.01f), "俯角の下限は80度にクランプされます。");
        }

        [Test]
        public void FirstPerson_AppliesBaseFovToCinemachineCamera()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.SetBaseFov(90f);

            var cmCam = fpRig.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            Assert.That(cmCam.Lens.FieldOfView, Is.EqualTo(90f).Within(0.001f), "SetBaseFov は CinemachineCamera.Lens.FieldOfView に直接反映される必要があります。");
            Assert.That(controller.BaseFov, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void FirstPerson_UpdatesBaseFovOnSettingsChanged()
        {
            GameSettingsService.Instance.ResetToDefault();
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();

            GameSettingsService.Instance.SetFov(100f);

            var cmCam = fpRig.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            Assert.That(cmCam.Lens.FieldOfView, Is.EqualTo(100f).Within(0.001f), "GameSettingsService.FovChanged イベントによりカメラの FieldOfView が即時同期される必要があります。");
            Assert.That(controller.BaseFov, Is.EqualTo(100f).Within(0.001f));

            // デフォルトへリセット
            GameSettingsService.Instance.ResetToDefault();
        }

        [Test]
        public void MeshHandler_SetsHeadToShadowsOnly_InFirstPerson()
        {
            GameObject headObj = new GameObject("Knight_Head");
            headObj.transform.SetParent(player.transform, false);
            MeshRenderer headRenderer = headObj.AddComponent<MeshRenderer>();
            headRenderer.shadowCastingMode = ShadowCastingMode.On;

            PlayerFirstPersonMeshHandler handler = player.AddComponent<PlayerFirstPersonMeshHandler>();
            handler.ConfigureRenderers(new[] { headRenderer });

            // 第一人称
            handler.SetFirstPersonMode(true);
            Assert.That(headRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "第一人称では頭部RendererがShadowsOnlyになり、視線遮断を防止しつつ影を維持します。");

            // 第三人称
            handler.SetFirstPersonMode(false);
            Assert.That(headRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "第三人称では頭部RendererがOnに戻り、通常通り描画されます。");
        }

        [Test]
        public void MeshHandler_SetsBodyCapeArmsAndLegsToShadowsOnly_InFirstPerson()
        {
            GameObject headObj = new GameObject("Knight_Head");
            headObj.transform.SetParent(player.transform, false);
            MeshRenderer headRenderer = headObj.AddComponent<MeshRenderer>();
            headRenderer.shadowCastingMode = ShadowCastingMode.On;

            GameObject bodyObj = new GameObject("Knight_Body");
            bodyObj.transform.SetParent(player.transform, false);
            MeshRenderer bodyRenderer = bodyObj.AddComponent<MeshRenderer>();
            bodyRenderer.shadowCastingMode = ShadowCastingMode.On;

            GameObject capeObj = new GameObject("Knight_Cape");
            capeObj.transform.SetParent(player.transform, false);
            MeshRenderer capeRenderer = capeObj.AddComponent<MeshRenderer>();
            capeRenderer.shadowCastingMode = ShadowCastingMode.On;

            GameObject armObj = new GameObject("Knight_ArmRight");
            armObj.transform.SetParent(player.transform, false);
            MeshRenderer armRenderer = armObj.AddComponent<MeshRenderer>();
            armRenderer.shadowCastingMode = ShadowCastingMode.On;

            GameObject legObj = new GameObject("Knight_LegRight");
            legObj.transform.SetParent(player.transform, false);
            MeshRenderer legRenderer = legObj.AddComponent<MeshRenderer>();
            legRenderer.shadowCastingMode = ShadowCastingMode.On;

            PlayerFirstPersonMeshHandler handler = player.AddComponent<PlayerFirstPersonMeshHandler>();

            // 第一人称モード
            handler.SetFirstPersonMode(true);
            Assert.That(bodyRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "第一人称では胴体（Knight_Body）がShadowsOnlyになり、近クリップ面によるカメラ貫通や画面のチラつき・揺れを防ぎます。");
            Assert.That(capeRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "第一人称ではマント（Knight_Cape）がShadowsOnlyになります。");
            Assert.That(armRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "第一人称では視口武器（Viewmodel）と重複する空手腕の露出を防ぐため、腕（Knight_ArmRight）がShadowsOnlyになります。");
            Assert.That(legRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "第一人称では低頭時の浮遊足・透明胴体の違和感を防ぐため、脚部（Knight_LegRight）もShadowsOnlyになります。");

            // 第三人称モードへ切替
            handler.SetFirstPersonMode(false);
            Assert.That(bodyRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "第三人称では胸甲がOnに戻り、通常の外見を描画します。");
            Assert.That(capeRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "第三人称ではマントがOnに戻ります。");
            Assert.That(armRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "腕は第三人称でOnに戻ります。");
            Assert.That(legRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "脚部も第三人称でOnに戻り、全身が正常に描画されます。");
        }

        [Test]
        public void FirstPersonCamera_SupportsDampingForPhysicsCollisionSmoothing()
        {
            Component hardLock = fpRig.GetComponent("CinemachineHardLockToTarget");
            Assert.That(hardLock, Is.Not.Null, "CM_FirstPersonにCinemachineHardLockToTargetが必要です。");

            FieldInfo dampingField = hardLock.GetType().GetField("Damping", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(dampingField, Is.Not.Null, "CinemachineHardLockToTargetにDampingフィールドが存在する必要があります。");

            dampingField.SetValue(hardLock, 0.04f);
            float currentDamping = (float)dampingField.GetValue(hardLock);
            Assert.That(currentDamping, Is.EqualTo(0.04f).Within(0.001f),
                "Dampingに微小減衰（0.04）を設定して、CharacterControllerの物理衝突微振動（Depenetration）を吸収可能である必要があります。");
        }

        [Test]
        public void Enemy_ConfiguredStoppingDistance_MaintainsMeleeSafetyDistance()
        {
            GameObject enemyObj = new GameObject("TestEnemy");
            try
            {
                EnemyMotor motor = enemyObj.AddComponent<EnemyMotor>();
                EnemyBrain brain = enemyObj.AddComponent<EnemyBrain>();

                Assert.That(motor.ConfiguredStoppingDistance, Is.GreaterThanOrEqualTo(2.0f),
                    "EnemyMotorの停止距離はプレイヤーのCharacterControllerへの物理衝突突入およびモデル重なりを防ぐため2.0m以上である必要があります。");
                Assert.That(brain.ConfiguredStoppingDistance, Is.GreaterThanOrEqualTo(2.0f),
                    "EnemyBrainの停止距離は2.0m以上である必要があります。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemyObj);
            }
        }

        [Test]
        public void FirstPersonCamera_ApplyTraumaImpulse_AppliesDutchRollAndRecoversToZero()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.ResolvePlayerCameraTarget();
            controller.ApplyRigConfiguration();

            var cmCam = fpRig.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            Assert.That(cmCam, Is.Not.Null);

            // 右側からの攻撃（受力ベクトルは左方向：X < 0）
            controller.ApplyTraumaImpulse(new Vector3(-1f, 0f, 0f), 1f);
            controller.UpdateTrauma(0.033f);

            Assert.That(controller.CurrentTraumaRoll, Is.LessThan(0f), "右側からの打撃により左側へのロール（Roll < 0）が発生する必要があります。");
            Assert.That(cmCam.Lens.Dutch, Is.LessThan(0f), "CinemachineCamera.Lens.Dutchに左傾斜が即時反映される必要があります。");
            Assert.That(controller.CurrentTraumaPitch, Is.GreaterThan(0f), "打撃により頭部の後仰（Pitch > 0）が発生する必要があります。");

            // 0.5秒間更新して静止位置へ滑らかに整定
            for (int i = 0; i < 35; i++)
            {
                controller.UpdateTrauma(0.016f);
            }

            Assert.That(Mathf.Abs(cmCam.Lens.Dutch), Is.LessThan(0.01f), "0.5秒後にはDutchロールが0に平滑収束する必要があります。");
            Assert.That(Mathf.Abs(controller.CurrentTraumaPitch), Is.LessThan(0.01f), "0.5秒後には受撃後仰角が0に収束する必要があります。");
            Assert.That(Mathf.Abs(controller.TotalPitch - controller.CurrentPitch), Is.LessThan(0.01f));
        }

        [Test]
        public void FirstPersonCamera_TraumaPitch_PreservesMouseAimIntegrity()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.ResolvePlayerCameraTarget();
            controller.ApplyRigConfiguration();

            // プレイヤーが照準を上方向に動かしている状態
            controller.ApplyLookInput(new Vector2(0f, 200f));
            float aimPitch = controller.CurrentPitch;

            // 受撃インパルスを印加
            controller.ApplyTraumaImpulse(new Vector3(0f, 0f, -1f), 1.5f);
            controller.UpdateTrauma(0.033f);

            // プレイヤー自身のマウス照準CurrentPitchは一切改変されない
            Assert.That(controller.CurrentPitch, Is.EqualTo(aimPitch).Within(0.001f),
                "受撃による後仰角は動的オフセットとして加算されるため、プレイヤー自身の照準角CurrentPitchは改変されません。");
            Assert.That(controller.TotalPitch, Is.Not.EqualTo(aimPitch), "実効俯仰角TotalPitchには受撃インパルスが加算されている必要があります。");

            // 減衰整定後
            for (int i = 0; i < 35; i++)
            {
                controller.UpdateTrauma(0.016f);
            }

            Assert.That(controller.CurrentPitch, Is.EqualTo(aimPitch).Within(0.001f), "減衰後もプレイヤーの照準角は100%完全維持されます。");
            Assert.That(controller.TotalPitch, Is.EqualTo(aimPitch).Within(0.01f), "減衰後は実効俯仰角も完全にプレイヤーの照準角に一致します。");
        }

        [Test]
        public void FirstPersonCamera_ApplyTraumaImpulse_FromLeft_RollsRight()
        {
            FirstPersonCameraController controller = fpRig.AddComponent<FirstPersonCameraController>();
            controller.ResolvePlayerCameraTarget();
            controller.ApplyRigConfiguration();

            var cmCam = fpRig.GetComponent<Unity.Cinemachine.CinemachineCamera>();

            // 左側からの攻撃（受力ベクトルは右方向：X > 0）
            controller.ApplyTraumaImpulse(new Vector3(1f, 0f, 0f), 1f);
            controller.UpdateTrauma(0.033f);

            Assert.That(controller.CurrentTraumaRoll, Is.GreaterThan(0f), "左側からの打撃により右側へのロール（Roll > 0）が発生する必要があります。");
            Assert.That(cmCam.Lens.Dutch, Is.GreaterThan(0f), "CinemachineCamera.Lens.Dutchに右傾斜が即時反映される必要があります。");
        }

        private static Component AddCinemachineComponent(GameObject target, string fullTypeName)
        {
            Type componentType = Type.GetType($"{fullTypeName}, {CinemachineAssemblyName}");
            Assert.That(componentType, Is.Not.Null, $"{fullTypeName}が解決できません。Cinemachineパッケージが必要です。");
            return target.AddComponent(componentType);
        }

        private static int GetCameraPriority(Component cinemachineCamera)
        {
            Assert.That(cinemachineCamera, Is.Not.Null, "cinemachineCamera must not be null");
            object priorityObj = GetMember(cinemachineCamera, "Priority");
            Assert.That(priorityObj, Is.Not.Null, "priorityObj must not be null");
            object valObj = GetMember(priorityObj, "Value");
            Assert.That(valObj, Is.Not.Null, "Value must not be null");
            return Convert.ToInt32(valObj);
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.CanRead ? property.GetValue(target) : null;
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

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName}フィールドが見つかりません。");
            field.SetValue(target, value);
        }
    }
}
