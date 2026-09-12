using System;
using System.Reflection;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// カメラの視点モードです。
    /// </summary>
    public enum CameraPerspectiveMode
    {
        FirstPerson,
        ThirdPerson
    }

    /// <summary>
    /// Cinemachine第一人称および第三人称リグの追従対象と構成値を一元管理します。
    /// パッケージ固有のAPIはこのアダプター内の反射処理に限定し、Cinemachine 3の正式なリグだけを構成します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        private const float MinimumDistance = 0.1f;
        private const float MinimumCameraRadius = 0.01f;
        private const string CinemachineAssemblyName = "Unity.Cinemachine";
        private const string CameraTypeName = "Unity.Cinemachine.CinemachineCamera";
        private const string OrbitalFollowTypeName = "Unity.Cinemachine.CinemachineOrbitalFollow";
        private const string RotationComposerTypeName = "Unity.Cinemachine.CinemachineRotationComposer";
        private const string DeoccluderTypeName = "Unity.Cinemachine.CinemachineDeoccluder";
        private const string HardLockTypeName = "Unity.Cinemachine.CinemachineHardLockToTarget";
        private const string PanTiltTypeName = "Unity.Cinemachine.CinemachinePanTilt";

        [Header("視点モード")]
        [SerializeField] private CameraPerspectiveMode perspectiveMode = CameraPerspectiveMode.FirstPerson;
        [SerializeField] private GameObject firstPersonRigObject;
        [SerializeField] private Vector2 firstPersonPitchLimits = new(-80f, 80f);
        [SerializeField] private readonly string firstPersonRigPath = "Camera/CM_FirstPerson";

        [Header("追従対象")]
        [SerializeField] private Transform playerCameraTarget;
        [SerializeField] private readonly string playerRootName = "Player";
        [SerializeField] private readonly string cameraTargetName = "CameraTarget";
        [SerializeField] private CameraInputReader cameraInputReader;

        [Header("軌道")]
        [Tooltip("Cinemachine OrbitalFollowのHorizontalAxisの初期および固定の中心オフセット角です。TrackerSettings.BindingModeが" +
            "LockToTargetWithWorldUpのため、この値はPlayerの現在の向きからの相対オフセットとして扱われ、マウスの水平Look入力では変化しません。")]
        [SerializeField] private readonly float initialYaw;
        [SerializeField] private readonly float initialPitch = 12f;
        [Tooltip("HorizontalAxisに許可する角度範囲です。HorizontalAxisは水平Look入力では動かないため、主に手動調整時の安全範囲として使われます。")]
        [SerializeField] private Vector2 yawLimits = new(-160f, 160f);
        [SerializeField] private Vector2 pitchLimits = new(-30f, 65f);
        [SerializeField, Min(0f)] private float yawSensitivity = 0.1f;
        [SerializeField, Min(0f)] private float pitchSensitivity = 0.1f;
        [SerializeField] private readonly bool invertVerticalLook;

        [Header("軌道フォロー")]
        [Tooltip("CinemachineOrbitalFollowのRadiusへ渡すカメラとLookAtの基準距離です。")]
        [SerializeField, Min(MinimumDistance)] private float cameraDistance = 6f;
        [Tooltip("OrbitalFollowのTargetOffset（水平・垂直方向）へ渡すオフセットです。x/yがShoulderOffsetの水平・垂直成分に相当します。")]
        [SerializeField] private Vector3 shoulderOffset = new(0.65f, 0.35f, 0f);
        [Tooltip("TargetOffsetのy成分に加算する追加の垂直アーム長です。")]
        [SerializeField, Min(0f)] private float verticalArmLength = 0.5f;
        [SerializeField] private Vector3 positionDamping = new(0.15f, 0.25f, 0.2f);
        [SerializeField] private readonly int priority = 20;

        [Header("遮蔽処理")]
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(MinimumDistance)] private float minimumDistanceFromTarget = 0.6f;
        [SerializeField, Min(MinimumCameraRadius)] private float obstacleCameraRadius = 0.2f;
        [SerializeField, Min(1)] private int maximumObstacleResolutionAttempts = 4;
        [SerializeField, Min(0f)] private float returnDamping = 0.45f;
        [SerializeField, Min(0f)] private float occlusionDamping = 0.12f;

        private Component cinemachineCamera;
        private Component orbitalFollow;
        private Component rotationComposer;
        private Component deoccluder;
        private Component firstPersonCamera;
        private Component firstPersonHardLock;
        private Component firstPersonPanTilt;
        private Transform playerRootTransform;
        private float currentYaw;
        private float currentPitch;
        private bool orbitInitialized;
        private bool isTerminalState;
        private bool missingInputReaderReported;
        private bool missingTargetReported;
        private bool missingRigReported;

        /// <summary>視点モード変更通知イベントです。</summary>
        public event Action<CameraPerspectiveMode> PerspectiveChanged;

        /// <summary>現在の視点モードです。</summary>
        public CameraPerspectiveMode PerspectiveMode => perspectiveMode;

        /// <summary>現在の正式なCinemachineカメラコンポーネントです。</summary>
        public Component Rig => (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig)
            ? firstPersonCamera
            : cinemachineCamera;

        /// <summary>第三人称の正式なCinemachineカメラコンポーネントです。</summary>
        public Component ThirdPersonRig => cinemachineCamera;

        /// <summary>第一人称の正式なCinemachineカメラコンポーネントです。</summary>
        public Component FirstPersonRig => firstPersonCamera;

        /// <summary>第一人称リグが設定または解決されているかを示します。</summary>
        public bool HasFirstPersonRig => firstPersonCamera != null;

        /// <summary>Playerのカメラ追従・注視点です。</summary>
        public Transform PlayerCameraTarget => playerCameraTarget;

        /// <summary>
        /// Cinemachine OrbitalFollowのHorizontalAxisの現在値です。TrackerSettings.BindingModeが
        /// LockToTargetWithWorldUpのため、この値はPlayerの向きからの固定オフセットであり、水平Look入力では変化しません
        /// （水平Look入力はPlayerのyawのみを変更します）。
        /// </summary>
        public float CurrentYaw => currentYaw;

        /// <summary>現在の制限済み垂直軌道角です。</summary>
        public float CurrentPitch => currentPitch;

        /// <summary>勝利または敗北の表示中に可視性を維持する状態かを示します。</summary>
        public bool IsTerminalState => isTerminalState;

        private void Awake()
        {
            NormalizeConfiguration();
            InitializeOrbitIfNeeded();
            ResolveReferences();
            CacheComponents();
            ResolvePlayerCameraTarget();
            ApplyRigConfiguration();
        }

        private void OnEnable()
        {
            InitializeOrbitIfNeeded();
            ResolveReferences();
            CacheComponents();
            ResolvePlayerCameraTarget();
            ApplyRigConfiguration();
        }

        private void Update()
        {
            CheckPerspectiveToggleInput();
            UpdateOrbitFromLookInput();
        }

        private void CheckPerspectiveToggleInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.vKey.wasPressedThisFrame)
            {
                TogglePerspective();
            }
#endif
        }

        private void OnValidate()
        {
            NormalizeConfiguration();
            if (Application.isPlaying)
            {
                ClampOrbit();
            }
            else
            {
                orbitInitialized = false;
                InitializeOrbitIfNeeded();
            }

            ResolveReferences();
            CacheComponents();
            ApplyRigConfiguration();
        }

        /// <summary>
        /// Player配下のCameraTargetを解決し、正式CinemachineリグのFollowとLookAtへ設定します。
        /// </summary>
        public bool ResolvePlayerCameraTarget()
        {
            GameObject playerRoot = playerRootTransform != null ? playerRootTransform.gameObject : GameObject.Find(playerRootName);
            if (playerRoot != null)
            {
                playerRootTransform = playerRoot.transform;
                if (playerCameraTarget == null)
                {
                    playerCameraTarget = playerRootTransform.Find(cameraTargetName);
                }
            }

            if (playerCameraTarget == null)
            {
                if (!missingTargetReported)
                {
                    missingTargetReported = true;
                    Debug.LogError($"[カメラ診断] Playerのカメラターゲット '{playerRootName}/{cameraTargetName}' が見つかりません。", this);
                }

                return false;
            }

            missingTargetReported = false;
            SetMember(cinemachineCamera, "Follow", playerCameraTarget);
            SetMember(cinemachineCamera, "LookAt", playerCameraTarget);
            if (firstPersonCamera != null)
            {
                SetMember(firstPersonCamera, "Follow", playerCameraTarget);
            }
            return true;
        }

        /// <summary>
        /// 終局中も同じ正式CinemachineリグでPlayerを可視に保ちます。
        /// 通常のUnity Cameraへの切り替えや代替は行いません。
        /// </summary>
        public void SetTerminalState(bool terminalState)
        {
            if (isTerminalState == terminalState)
            {
                return;
            }

            isTerminalState = terminalState;
            if (!isTerminalState)
            {
                return;
            }

            CacheComponents();
            ResolvePlayerCameraTarget();
            ApplyRigConfiguration();
        }

        /// <summary>
        /// 視点モードを切り替え、優先度と俯仰制限を更新します。
        /// </summary>
        public void SetPerspective(CameraPerspectiveMode mode)
        {
            if (perspectiveMode == mode)
            {
                return;
            }

            perspectiveMode = mode;
            Vector2 activePitchLimits = (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig)
                ? firstPersonPitchLimits
                : pitchLimits;
            currentPitch = Mathf.Clamp(currentPitch, activePitchLimits.x, activePitchLimits.y);

            ConfigurePriority();
            ApplyOrbitToRig();
            PerspectiveChanged?.Invoke(perspectiveMode);
        }

        /// <summary>
        /// 第一人称と第三人称の視点をトグルします。
        /// </summary>
        public void TogglePerspective()
        {
            CameraPerspectiveMode nextMode = perspectiveMode == CameraPerspectiveMode.FirstPerson
                ? CameraPerspectiveMode.ThirdPerson
                : CameraPerspectiveMode.FirstPerson;
            SetPerspective(nextMode);
        }

        /// <summary>
        /// テストや動的初期化向けに第一人称リグGameObjectを設定しキャッシュを再構築します。
        /// </summary>
        public void ConfigureFirstPersonRig(GameObject fpRig)
        {
            firstPersonRigObject = fpRig;
            CacheComponents();
            ApplyRigConfiguration();
        }

        /// <summary>
        /// Inspector値をCinemachine 3のリグ（第一人称・第三人称）へ反映します。
        /// </summary>
        public void ApplyRigConfiguration()
        {
            InitializeOrbitIfNeeded();
            ConfigurePriority();

            // 第一人称リグの設定
            if (firstPersonCamera != null && playerCameraTarget != null)
            {
                SetMember(firstPersonCamera, "Follow", playerCameraTarget);
                if (firstPersonPanTilt != null)
                {
                    SetEnumMember(firstPersonPanTilt, "ReferenceFrame", "TrackingTarget");
                    ConfigureAxisOnComponent(firstPersonPanTilt, "TiltAxis", firstPersonPitchLimits, currentPitch);
                    ConfigureAxisOnComponent(firstPersonPanTilt, "PanAxis", new Vector2(-180f, 180f), 0f);
                }
            }

            // 第三人称リグの設定
            if (!HasRequiredRig())
            {
                return;
            }

            SetMember(cinemachineCamera, "Follow", playerCameraTarget);
            SetMember(cinemachineCamera, "LookAt", playerCameraTarget);

            SetMember(orbitalFollow, "Radius", cameraDistance);
            SetMember(orbitalFollow, "TargetOffset", new Vector3(shoulderOffset.x, shoulderOffset.y + verticalArmLength, shoulderOffset.z));
            SetEnumMember(orbitalFollow, "OrbitStyle", "Sphere");
            SetEnumMember(orbitalFollow, "RecenteringTarget", "LookAtTarget");
            ConfigureTrackerSettings();
            ConfigureAxis("HorizontalAxis", yawLimits, currentYaw);
            ConfigureAxis("VerticalAxis", pitchLimits, currentPitch);

            ConfigureRotationComposer();
            ConfigureDeoccluder();
        }

        private void ResolveReferences()
        {
            if (cameraInputReader == null)
            {
                cameraInputReader = GetComponent<CameraInputReader>();
            }

            if (cameraInputReader == null)
            {
                cameraInputReader = FindAnyObjectByType<CameraInputReader>();
            }

            if (cameraInputReader != null)
            {
                missingInputReaderReported = false;
            }
        }

        private void UpdateOrbitFromLookInput()
        {
            if (cameraInputReader == null)
            {
                ResolveReferences();
                if (cameraInputReader == null)
                {
                    if (!missingInputReaderReported)
                    {
                        missingInputReaderReported = true;
                        Debug.LogError("[カメラ診断] Look入力を読むCameraInputReaderが設定されていません。", this);
                    }

                    return;
                }
            }

            ApplyLookInput(cameraInputReader.ReadLook());
        }

        /// <summary>
        /// CameraInputReaderが所有するLook入力をPlayerのyawとカメラのpitchへ適用します。
        /// 水平Look入力はPlayerのyawを回転させ、垂直Look入力はアクティブなリグのpitch/TiltAxisを更新します。
        /// </summary>
        public void ApplyLookInput(Vector2 lookInput)
        {
            if (lookInput.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            InitializeOrbitIfNeeded();
            RotatePlayerFromHorizontalLook(lookInput.x);
            float verticalDirection = invertVerticalLook ? 1f : -1f;
            Vector2 activePitchLimits = (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig)
                ? firstPersonPitchLimits
                : pitchLimits;
            currentPitch = Mathf.Clamp(currentPitch + lookInput.y * pitchSensitivity * verticalDirection, activePitchLimits.x, activePitchLimits.y);
            ApplyOrbitToRig();
        }

        private void RotatePlayerFromHorizontalLook(float horizontalLook)
        {
            if (Mathf.Abs(horizontalLook) <= Mathf.Epsilon)
            {
                return;
            }

            if (playerRootTransform == null)
            {
                ResolvePlayerCameraTarget();
            }

            if (playerRootTransform != null)
            {
                playerRootTransform.Rotate(Vector3.up, horizontalLook * yawSensitivity, Space.World);
            }
        }

        private void InitializeOrbitIfNeeded()
        {
            if (orbitInitialized)
            {
                return;
            }

            Vector2 activePitchLimits = (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig)
                ? firstPersonPitchLimits
                : pitchLimits;
            currentYaw = Mathf.Clamp(initialYaw, yawLimits.x, yawLimits.y);
            currentPitch = Mathf.Clamp(initialPitch, activePitchLimits.x, activePitchLimits.y);
            orbitInitialized = true;
        }

        private void ClampOrbit()
        {
            InitializeOrbitIfNeeded();
            Vector2 activePitchLimits = (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig)
                ? firstPersonPitchLimits
                : pitchLimits;
            currentYaw = Mathf.Clamp(currentYaw, yawLimits.x, yawLimits.y);
            currentPitch = Mathf.Clamp(currentPitch, activePitchLimits.x, activePitchLimits.y);
        }

        private void ApplyOrbitToRig()
        {
            if (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig)
            {
                if (firstPersonPanTilt != null)
                {
                    SetAxisValueOnComponent(firstPersonPanTilt, "TiltAxis", currentPitch, firstPersonPitchLimits);
                    SetAxisValueOnComponent(firstPersonPanTilt, "PanAxis", 0f, new Vector2(-180f, 180f));
                }
                return;
            }

            if (orbitalFollow == null)
            {
                CacheComponents();
            }

            if (orbitalFollow == null)
            {
                return;
            }

            SetAxisValue("HorizontalAxis", currentYaw);
            SetAxisValue("VerticalAxis", currentPitch);
        }

        private void CacheComponents()
        {
            cinemachineCamera = GetCinemachineComponent(CameraTypeName);
            orbitalFollow = GetCinemachineComponent(OrbitalFollowTypeName);
            rotationComposer = GetCinemachineComponent(RotationComposerTypeName);
            deoccluder = GetCinemachineComponent(DeoccluderTypeName);

            ResolveFirstPersonRig();
        }

        private void ResolveFirstPersonRig()
        {
            if (firstPersonRigObject == null)
            {
                firstPersonRigObject = GameObject.Find(firstPersonRigPath);
                if (firstPersonRigObject == null && transform.parent != null)
                {
                    Transform sibling = transform.parent.Find("CM_FirstPerson");
                    if (sibling != null)
                    {
                        firstPersonRigObject = sibling.gameObject;
                    }
                }
            }

            if (firstPersonRigObject != null)
            {
                firstPersonCamera = GetCinemachineComponentOnObject(firstPersonRigObject, CameraTypeName);
                firstPersonHardLock = GetCinemachineComponentOnObject(firstPersonRigObject, HardLockTypeName);
                firstPersonPanTilt = GetCinemachineComponentOnObject(firstPersonRigObject, PanTiltTypeName);
            }
            else
            {
                firstPersonCamera = null;
                firstPersonHardLock = null;
                firstPersonPanTilt = null;
            }
        }

        private static Component GetCinemachineComponentOnObject(GameObject targetObject, string fullTypeName)
        {
            if (targetObject == null)
            {
                return null;
            }

            Type componentType = FindCinemachineType(fullTypeName);
            return componentType != null ? targetObject.GetComponent(componentType) : null;
        }

        private Component GetCinemachineComponent(string fullTypeName)
        {
            Type componentType = FindCinemachineType(fullTypeName);
            return componentType != null ? GetComponent(componentType) : null;
        }

        private bool HasRequiredRig()
        {
            bool isValid = cinemachineCamera != null
                && orbitalFollow != null
                && rotationComposer != null
                && deoccluder != null;
            if (!isValid && !missingRigReported && (!HasFirstPersonRig || perspectiveMode == CameraPerspectiveMode.ThirdPerson))
            {
                missingRigReported = true;
                Debug.LogError("[カメラ診断] CM_ThirdPersonに必要なCinemachine第三人称リグ（OrbitalFollow/RotationComposer/Deoccluder）がありません。", this);
            }

            if (isValid)
            {
                missingRigReported = false;
            }

            return isValid;
        }

        private void ConfigurePriority()
        {
            int tpPriority = (perspectiveMode == CameraPerspectiveMode.FirstPerson && HasFirstPersonRig) ? 10 : priority;
            SetPriorityOnCamera(cinemachineCamera, tpPriority);

            if (firstPersonCamera != null)
            {
                int fpPriority = (perspectiveMode == CameraPerspectiveMode.FirstPerson) ? priority : 10;
                SetPriorityOnCamera(firstPersonCamera, fpPriority);
            }
        }

        private static void SetPriorityOnCamera(Component targetCamera, int value)
        {
            if (targetCamera == null)
            {
                return;
            }

            object prioritySettings = GetMember(targetCamera, "Priority");
            if (prioritySettings == null)
            {
                return;
            }

            SetMember(prioritySettings, "Value", value);
            SetMember(targetCamera, "Priority", prioritySettings);
        }

        /// <summary>
        /// OrbitalFollowのTrackerSettingsをLockToTargetWithWorldUpに設定します。
        /// </summary>
        private void ConfigureTrackerSettings()
        {
            object tracker = GetMember(orbitalFollow, "TrackerSettings");
            if (tracker == null)
            {
                return;
            }

            SetEnumMember(tracker, "BindingMode", "LockToTargetWithWorldUp");
            SetMember(tracker, "PositionDamping", positionDamping);
            SetMember(tracker, "RotationDamping", positionDamping);
            SetMember(orbitalFollow, "TrackerSettings", tracker);
        }

        /// <summary>
        /// RotationComposer（Aimステージ）をLookAtターゲットが常に画面中央に収まるよう構成します。
        /// </summary>
        private void ConfigureRotationComposer()
        {
            object composition = GetMember(rotationComposer, "Composition");
            if (composition != null)
            {
                SetMember(composition, "ScreenPosition", Vector2.zero);
                SetMember(rotationComposer, "Composition", composition);
            }

            SetMember(rotationComposer, "Damping", new Vector2(positionDamping.x, positionDamping.y));
            SetMember(rotationComposer, "CenterOnActivate", true);
        }

        private void ConfigureAxis(string axisName, Vector2 limits, float value)
        {
            ConfigureAxisOnComponent(orbitalFollow, axisName, limits, value);
        }

        private void SetAxisValue(string axisName, float value)
        {
            Vector2 limits = axisName == "HorizontalAxis" ? yawLimits : pitchLimits;
            SetAxisValueOnComponent(orbitalFollow, axisName, value, limits);
        }

        private void ConfigureAxisOnComponent(Component component, string axisName, Vector2 limits, float value)
        {
            if (component == null)
            {
                return;
            }

            object axis = GetMember(component, axisName);
            if (axis == null)
            {
                return;
            }

            float clampedValue = Mathf.Clamp(value, limits.x, limits.y);
            SetMember(axis, "Range", limits);
            SetMember(axis, "Wrap", false);
            SetMember(axis, "Center", clampedValue);
            SetMember(axis, "Value", clampedValue);
            DisableAxisRecentering(axis);
            SetMember(component, axisName, axis);
        }

        private void SetAxisValueOnComponent(Component component, string axisName, float value, Vector2 limits)
        {
            if (component == null)
            {
                return;
            }

            object axis = GetMember(component, axisName);
            if (axis == null)
            {
                return;
            }

            SetMember(axis, "Value", Mathf.Clamp(value, limits.x, limits.y));
            SetMember(component, axisName, axis);
        }

        private static void DisableAxisRecentering(object axis)
        {
            object recentering = GetMember(axis, "Recentering");
            if (recentering == null)
            {
                return;
            }

            SetMember(recentering, "Enabled", false);
            SetMember(axis, "Recentering", recentering);
        }

        private void ConfigureDeoccluder()
        {
            SetMember(deoccluder, "CollideAgainst", obstacleLayers);
            SetMember(deoccluder, "IgnoreTag", "Player");
            SetMember(deoccluder, "MinimumDistanceFromTarget", minimumDistanceFromTarget);

            object avoidance = GetMember(deoccluder, "AvoidObstacles");
            if (avoidance == null)
            {
                return;
            }

            SetMember(avoidance, "Enabled", true);
            SetMember(avoidance, "CameraRadius", obstacleCameraRadius);
            SetEnumMember(avoidance, "Strategy", "PullCameraForward");
            SetMember(avoidance, "MaximumEffort", maximumObstacleResolutionAttempts);
            SetMember(avoidance, "Damping", returnDamping);
            SetMember(avoidance, "DampingWhenOccluded", occlusionDamping);

            object followTargetSettings = GetMember(avoidance, "UseFollowTarget");
            if (followTargetSettings != null)
            {
                SetMember(followTargetSettings, "Enabled", true);
                SetMember(followTargetSettings, "YOffset", 0f);
                SetMember(avoidance, "UseFollowTarget", followTargetSettings);
            }

            SetMember(deoccluder, "AvoidObstacles", avoidance);
        }

        private static Type FindCinemachineType(string fullTypeName)
        {
            return Type.GetType($"{fullTypeName}, {CinemachineAssemblyName}");
        }

        private static object GetMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                return field.GetValue(target);
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.CanRead ? property.GetValue(target) : null;
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null)
            {
                return;
            }

            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
            }
        }

        private static void SetEnumMember(object target, string name, string enumValue)
        {
            if (target == null)
            {
                return;
            }

            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null && field.FieldType.IsEnum && Enum.IsDefined(field.FieldType, enumValue))
            {
                field.SetValue(target, Enum.Parse(field.FieldType, enumValue));
            }
        }

        private void NormalizeConfiguration()
        {
            cameraDistance = Mathf.Max(MinimumDistance, cameraDistance);
            verticalArmLength = Mathf.Max(0f, verticalArmLength);
            positionDamping = new Vector3(
                Mathf.Max(0f, positionDamping.x),
                Mathf.Max(0f, positionDamping.y),
                Mathf.Max(0f, positionDamping.z));
            minimumDistanceFromTarget = Mathf.Clamp(minimumDistanceFromTarget, MinimumDistance, cameraDistance);
            obstacleCameraRadius = Mathf.Max(MinimumCameraRadius, obstacleCameraRadius);
            maximumObstacleResolutionAttempts = Mathf.Max(1, maximumObstacleResolutionAttempts);
            returnDamping = Mathf.Max(0f, returnDamping);
            occlusionDamping = Mathf.Max(0f, occlusionDamping);
            yawSensitivity = Mathf.Max(0f, yawSensitivity);
            pitchSensitivity = Mathf.Max(0f, pitchSensitivity);
            yawLimits = NormalizeLimits(yawLimits, -180f, 180f);
            pitchLimits = NormalizeLimits(pitchLimits, -89f, 89f);
            firstPersonPitchLimits = NormalizeLimits(firstPersonPitchLimits, -89f, 89f);
        }

        private static Vector2 NormalizeLimits(Vector2 limits, float minimum, float maximum)
        {
            limits.x = Mathf.Clamp(limits.x, minimum, maximum);
            limits.y = Mathf.Clamp(limits.y, minimum, maximum);
            if (limits.y < limits.x)
            {
                (limits.x, limits.y) = (limits.y, limits.x);
            }

            return limits;
        }
    }
}
