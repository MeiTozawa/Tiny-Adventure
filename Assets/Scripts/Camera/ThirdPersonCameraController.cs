using System;
using System.Reflection;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// Cinemachine第三人称リグの追従対象と構成値を一元管理します。
    /// パッケージ固有のAPIはこのアダプター内の反射処理に限定し、Cinemachine 3の正式なリグだけを構成します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        private const float MinimumDistance = 0.1f;
        private const float MinimumCameraRadius = 0.01f;
        private const string CinemachineAssemblyName = "Unity.Cinemachine";
        private const string CameraTypeName = "Unity.Cinemachine.CinemachineCamera";
        private const string ThirdPersonFollowTypeName = "Unity.Cinemachine.CinemachineThirdPersonFollow";
        private const string PanTiltTypeName = "Unity.Cinemachine.CinemachinePanTilt";
        private const string DeoccluderTypeName = "Unity.Cinemachine.CinemachineDeoccluder";

        [Header("追従対象")]
        [SerializeField] private Transform playerCameraTarget;
        [SerializeField] private string playerRootName = "Player";
        [SerializeField] private string cameraTargetName = "CameraTarget";
        [SerializeField] private CameraInputReader cameraInputReader;

        [Header("軌道")]
        [SerializeField] private float initialYaw;
        [SerializeField] private float initialPitch = 12f;
        [SerializeField] private Vector2 yawLimits = new Vector2(-160f, 160f);
        [SerializeField] private Vector2 pitchLimits = new Vector2(-30f, 65f);
        [SerializeField, Min(0f)] private float yawSensitivity = 0.1f;
        [SerializeField, Min(0f)] private float pitchSensitivity = 0.1f;
        [SerializeField] private bool invertVerticalLook;

        [Header("第三人称フォロー")]
        [SerializeField, Min(MinimumDistance)] private float cameraDistance = 6f;
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.65f, 0.35f, 0f);
        [SerializeField, Min(0f)] private float verticalArmLength = 0.5f;
        [SerializeField] private Vector3 positionDamping = new Vector3(0.15f, 0.25f, 0.2f);
        [SerializeField] private int priority = 20;

        [Header("遮蔽処理")]
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(MinimumDistance)] private float minimumDistanceFromTarget = 0.6f;
        [SerializeField, Min(MinimumCameraRadius)] private float obstacleCameraRadius = 0.2f;
        [SerializeField, Min(1)] private int maximumObstacleResolutionAttempts = 4;
        [SerializeField, Min(0f)] private float returnDamping = 0.45f;
        [SerializeField, Min(0f)] private float occlusionDamping = 0.12f;

        private Component cinemachineCamera;
        private Component thirdPersonFollow;
        private Component panTilt;
        private Component deoccluder;
        private Transform playerRootTransform;
        private float currentYaw;
        private float currentPitch;
        private bool orbitInitialized;
        private bool isTerminalState;
        private bool missingInputReaderReported;
        private bool missingTargetReported;
        private bool missingRigReported;

        /// <summary>現在の正式なCinemachineカメラコンポーネントです。</summary>
        public Component Rig => cinemachineCamera;

        /// <summary>Playerのカメラ追従・注視点です。</summary>
        public Transform PlayerCameraTarget => playerCameraTarget;

        /// <summary>現在の制限済み水平軌道角です。</summary>
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
            UpdateOrbitFromLookInput();
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
        /// Inspector値をCinemachine 3の第三人称Follow、PanTilt、Deoccluderへ反映します。
        /// </summary>
        public void ApplyRigConfiguration()
        {
            if (!HasRequiredRig())
            {
                return;
            }

            InitializeOrbitIfNeeded();
            ConfigurePriority();
            SetMember(cinemachineCamera, "Follow", playerCameraTarget);
            SetMember(cinemachineCamera, "LookAt", playerCameraTarget);

            SetMember(thirdPersonFollow, "CameraDistance", cameraDistance);
            SetMember(thirdPersonFollow, "ShoulderOffset", shoulderOffset);
            SetMember(thirdPersonFollow, "VerticalArmLength", verticalArmLength);
            SetMember(thirdPersonFollow, "CameraSide", 1f);
            SetMember(thirdPersonFollow, "Damping", positionDamping);

            SetEnumMember(panTilt, "ReferenceFrame", "TrackingTarget");
            SetEnumMember(panTilt, "RecenterTarget", "TrackingTargetForward");
            ConfigureAxis("PanAxis", yawLimits, currentYaw);
            ConfigureAxis("TiltAxis", pitchLimits, currentPitch);
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
        /// CameraInputReaderが所有するLook入力を第三人称視点とPlayerのyawへ適用します。
        /// </summary>
        public void ApplyLookInput(Vector2 lookInput)
        {
            if (lookInput.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            InitializeOrbitIfNeeded();
            RotatePlayerFromHorizontalLook(lookInput.x);
            currentYaw = Mathf.Clamp(currentYaw + lookInput.x * yawSensitivity, yawLimits.x, yawLimits.y);
            float verticalDirection = invertVerticalLook ? 1f : -1f;
            currentPitch = Mathf.Clamp(currentPitch + lookInput.y * pitchSensitivity * verticalDirection, pitchLimits.x, pitchLimits.y);
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

            currentYaw = Mathf.Clamp(initialYaw, yawLimits.x, yawLimits.y);
            currentPitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
            orbitInitialized = true;
        }

        private void ClampOrbit()
        {
            InitializeOrbitIfNeeded();
            currentYaw = Mathf.Clamp(currentYaw, yawLimits.x, yawLimits.y);
            currentPitch = Mathf.Clamp(currentPitch, pitchLimits.x, pitchLimits.y);
        }

        private void ApplyOrbitToRig()
        {
            if (panTilt == null)
            {
                CacheComponents();
            }

            if (panTilt == null)
            {
                return;
            }

            SetAxisValue("PanAxis", currentYaw);
            SetAxisValue("TiltAxis", currentPitch);
        }

        private void CacheComponents()
        {
            cinemachineCamera = GetCinemachineComponent(CameraTypeName);
            thirdPersonFollow = GetCinemachineComponent(ThirdPersonFollowTypeName);
            panTilt = GetCinemachineComponent(PanTiltTypeName);
            deoccluder = GetCinemachineComponent(DeoccluderTypeName);
        }

        private Component GetCinemachineComponent(string fullTypeName)
        {
            Type componentType = FindCinemachineType(fullTypeName);
            return componentType != null ? GetComponent(componentType) : null;
        }

        private bool HasRequiredRig()
        {
            bool isValid = cinemachineCamera != null
                && thirdPersonFollow != null
                && panTilt != null
                && deoccluder != null;
            if (!isValid && !missingRigReported)
            {
                missingRigReported = true;
                Debug.LogError("[カメラ診断] CM_ThirdPersonに必要なCinemachine第三人称リグがありません。", this);
            }

            if (isValid)
            {
                missingRigReported = false;
            }

            return isValid;
        }

        private void ConfigurePriority()
        {
            object prioritySettings = GetMember(cinemachineCamera, "Priority");
            if (prioritySettings == null)
            {
                return;
            }

            SetMember(prioritySettings, "Value", priority);
            SetMember(cinemachineCamera, "Priority", prioritySettings);
        }

        private void ConfigureAxis(string axisName, Vector2 limits, float value)
        {
            object axis = GetMember(panTilt, axisName);
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
            SetMember(panTilt, axisName, axis);
        }

        private void SetAxisValue(string axisName, float value)
        {
            object axis = GetMember(panTilt, axisName);
            if (axis == null)
            {
                return;
            }

            Vector2 limits = axisName == "PanAxis" ? yawLimits : pitchLimits;
            SetMember(axis, "Value", Mathf.Clamp(value, limits.x, limits.y));
            SetMember(panTilt, axisName, axis);
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
