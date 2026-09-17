using System;
using UnityEngine;
using Unity.Cinemachine;

namespace TinyAdventure
{
    /// <summary>
    /// 纯第一人称 Cinemachine 相机控制器。
    /// 负责硬锁定到玩家头部（CameraTarget）、鼠标 Look 输入（水平驱动身体 Yaw，垂直驱动 PanTilt 俯仰）、
    /// 以及动态 FOV 视野角配置与战斗震动反馈同步。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FirstPersonCameraController : MonoBehaviour
    {
        private const string PlayerRootName = "Player";
        private const string CameraTargetName = "CameraTarget";

        [Header("追従対象")]
        [SerializeField] private Transform playerCameraTarget;
        [SerializeField] private CameraInputReader cameraInputReader;

        [Header("俯仰与灵敏度")]
        [SerializeField] private Vector2 pitchLimits = new(-80f, 80f);
        [SerializeField, Min(0f)] private float pitchSensitivity = 0.1f;
        [SerializeField, Min(0f)] private float yawSensitivity = 0.1f;
        [SerializeField] private bool invertVerticalLook;
        [SerializeField] private FirstPersonViewmodelController viewmodelController;

        [Header("视野 (FOV)")]
        [SerializeField, Range(GameSettingsService.MinFov, GameSettingsService.MaxFov)]
        private float baseFov = GameSettingsService.DefaultFov;

        [Header("受撃カメラ揺れ・物理スプリング")]
        [SerializeField] private CameraHitTraumaSpring hitTraumaSpring = new();

        private CinemachineCamera cinemachineCamera;
        private CinemachineHardLockToTarget hardLock;
        private CinemachinePanTilt panTilt;
        private CombatCameraFeedback combatCameraFeedback;
        private Transform playerRootTransform;
        private float currentPitch;
        private bool orbitInitialized;
        private bool missingTargetReported;
        private bool missingInputReaderReported;
        private bool isInputSuspended;
        private bool isSettingsSubscribed;

        /// <summary>当前基准 FOV 视野角。</summary>
        public float BaseFov => baseFov;

        /// <summary>当前俯仰角（Pitch）。</summary>
        public float CurrentPitch => currentPitch;

        /// <summary>受撃カメラ物理スプリングです。</summary>
        public CameraHitTraumaSpring HitTraumaSpring => hitTraumaSpring;

        /// <summary>受撃による現在の後仰角オフセット（Pitch）です。</summary>
        public float CurrentTraumaPitch => hitTraumaSpring != null ? hitTraumaSpring.CurrentPitch : 0f;

        /// <summary>受撃による現在の側傾斜角オフセット（Roll / Dutch）です。</summary>
        public float CurrentTraumaRoll => hitTraumaSpring != null ? hitTraumaSpring.CurrentRoll : 0f;

        /// <summary>受撃による現在の偏航角オフセット（Yaw）です。</summary>
        public float CurrentTraumaYaw => hitTraumaSpring != null ? hitTraumaSpring.CurrentYaw : 0f;

        /// <summary>受撃による現在の視野角オフセット（FOV）です。</summary>
        public float CurrentTraumaFovOffset => hitTraumaSpring != null ? hitTraumaSpring.CurrentFovOffset : 0f;

        /// <summary>マウス照準と受撃揺れを合算した実効俯仰角です。</summary>
        public float TotalPitch => Mathf.Clamp(currentPitch + CurrentTraumaPitch, pitchLimits.x, pitchLimits.y);

        /// <summary>正式 Cinemachine 虚拟相机组件。</summary>
        public Component Rig => cinemachineCamera;

        /// <summary>第一人称相机追随与注视点。</summary>
        public Transform PlayerCameraTarget => playerCameraTarget;

        /// <summary>是否挂起视角输入（例如打开设置面板时）。</summary>
        public bool IsInputSuspended
        {
            get => isInputSuspended;
            set => isInputSuspended = value;
        }

        /// <summary>第一人称视口武器控制器。</summary>
        public FirstPersonViewmodelController ViewmodelController => viewmodelController;

        /// <summary>第一人称视口武器控制器を設定します。</summary>
        public void SetViewmodelController(FirstPersonViewmodelController controller) => viewmodelController = controller;

        private void Awake()
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            NormalizeConfiguration();
            InitializeOrbitIfNeeded();
            ResolveReferences();
            CacheComponents();
            ResolvePlayerCameraTarget();
            ApplyRigConfiguration();
            SubscribeToSettings();
        }

        private void OnEnable()
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            InitializeOrbitIfNeeded();
            ResolveReferences();
            CacheComponents();
            ResolvePlayerCameraTarget();
            ApplyRigConfiguration();
            SubscribeToSettings();
        }

        private void OnDisable()
        {
            UnsubscribeFromSettings();
        }

        private void OnDestroy()
        {
            UnsubscribeFromSettings();
        }

        private void SubscribeToSettings()
        {
            if (isSettingsSubscribed) return;
            if (GameSettingsService.Instance != null)
            {
                GameSettingsService.Instance.FovChanged += HandleFovChanged;
                isSettingsSubscribed = true;
                SetBaseFov(GameSettingsService.Instance.CurrentFov);
            }
        }

        private void UnsubscribeFromSettings()
        {
            if (!isSettingsSubscribed) return;
            if (GameSettingsService.Instance != null)
            {
                GameSettingsService.Instance.FovChanged -= HandleFovChanged;
            }
            isSettingsSubscribed = false;
        }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.Update(dt);

            if (!isInputSuspended)
            {
                UpdateOrbitFromLookInput();
            }

            ApplyDynamicCameraOffsets();
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
        /// 设定基准 FOV，并实时反映到 Cinemachine 虚拟相机 Lens 与战斗反馈控制器。
        /// </summary>
        public void SetBaseFov(float fov)
        {
            baseFov = Mathf.Clamp(fov, GameSettingsService.MinFov, GameSettingsService.MaxFov);
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            if (cinemachineCamera == null)
            {
                CacheComponents();
            }

            if (cinemachineCamera != null)
            {
                LensSettings lens = cinemachineCamera.Lens;
                lens.Dutch = hitTraumaSpring.CurrentRoll;
                lens.FieldOfView = Mathf.Clamp(baseFov + hitTraumaSpring.CurrentFovOffset, 15f, 160f);
                cinemachineCamera.Lens = lens;
            }

            if (combatCameraFeedback == null)
            {
                combatCameraFeedback = FindAnyObjectByType<CombatCameraFeedback>();
            }

            if (combatCameraFeedback != null)
            {
                combatCameraFeedback.SetBaseFov(baseFov);
            }
        }

        /// <summary>
        /// 寻找并设置 Player 骨骼下的 CameraTarget 注视点。
        /// </summary>
        public bool ResolvePlayerCameraTarget()
        {
            GameObject playerRoot = playerRootTransform != null ? playerRootTransform.gameObject : GameObject.Find(PlayerRootName);
            if (playerRoot != null)
            {
                playerRootTransform = playerRoot.transform;
                if (playerCameraTarget == null)
                {
                    playerCameraTarget = playerRootTransform.Find(CameraTargetName);
                }
            }

            if (playerCameraTarget == null)
            {
                if (!missingTargetReported)
                {
                    missingTargetReported = true;
                    Debug.LogError($"[第一人称相机诊断] Player的相机目标 '{PlayerRootName}/{CameraTargetName}' 未找到。", this);
                }

                return false;
            }

            missingTargetReported = false;
            if (cinemachineCamera != null)
            {
                cinemachineCamera.Follow = playerCameraTarget;
                cinemachineCamera.LookAt = playerCameraTarget;
            }

            return true;
        }

        /// <summary>
        /// 将 Inspector 配置同步给 CinemachineCamera、HardLockToTarget 与 PanTilt。
        /// </summary>
        public void ApplyRigConfiguration()
        {
            InitializeOrbitIfNeeded();
            hitTraumaSpring ??= new CameraHitTraumaSpring();

            if (cinemachineCamera == null)
            {
                CacheComponents();
            }

            if (cinemachineCamera != null && playerCameraTarget != null)
            {
                cinemachineCamera.Follow = playerCameraTarget;
                cinemachineCamera.LookAt = playerCameraTarget;

                LensSettings lens = cinemachineCamera.Lens;
                lens.Dutch = hitTraumaSpring.CurrentRoll;
                lens.FieldOfView = Mathf.Clamp(baseFov + hitTraumaSpring.CurrentFovOffset, 15f, 160f);
                cinemachineCamera.Lens = lens;
            }

            if (panTilt != null)
            {
                panTilt.ReferenceFrame = CinemachinePanTilt.ReferenceFrames.TrackingTarget;
                float totalPitch = Mathf.Clamp(currentPitch + hitTraumaSpring.CurrentPitch, pitchLimits.x, pitchLimits.y);
                ConfigureAxisOnComponent(panTilt, "TiltAxis", pitchLimits, totalPitch);
                ConfigureAxisOnComponent(panTilt, "PanAxis", new Vector2(-180f, 180f), hitTraumaSpring.CurrentYaw);
            }
        }

        /// <summary>
        /// 局所受撃方向と強度を受け取り、カメラ受撃物理スプリングへインパルスを注入します。
        /// </summary>
        public void ApplyTraumaImpulse(Vector3 localDirection, float intensity = 1f)
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.ApplyImpact(localDirection, intensity);
            ApplyDynamicCameraOffsets();
        }

        /// <summary>
        /// 物理スプリングの状態を時間更新し、Cinemachineの各軸およびLensへ動的オフセットを適用します。
        /// </summary>
        public void UpdateTrauma(float deltaTime)
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.Update(deltaTime);
            ApplyDynamicCameraOffsets();
        }

        /// <summary>
        /// 受撃スプリングを即座に初期状態へリセットします。
        /// </summary>
        public void ResetTrauma()
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.Reset();
            ApplyDynamicCameraOffsets();
        }

        /// <summary>
        /// プレイヤーの照準角（Pitch）と受撃物理スプリングの動的変位（Pitch後仰、Roll側傾、Yaw偏航、FOV収縮）を
        /// CinemachinePanTiltおよびCinemachineCamera.Lensに反映します。
        /// </summary>
        public void ApplyDynamicCameraOffsets()
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();

            if (panTilt == null || cinemachineCamera == null)
            {
                CacheComponents();
            }

            if (panTilt != null)
            {
                float totalPitch = Mathf.Clamp(currentPitch + hitTraumaSpring.CurrentPitch, pitchLimits.x, pitchLimits.y);
                SetAxisValueOnComponent(panTilt, "TiltAxis", totalPitch, pitchLimits);
                float totalPan = hitTraumaSpring.CurrentYaw;
                SetAxisValueOnComponent(panTilt, "PanAxis", totalPan, new Vector2(-180f, 180f));
            }

            if (cinemachineCamera != null)
            {
                LensSettings lens = cinemachineCamera.Lens;
                lens.Dutch = hitTraumaSpring.CurrentRoll;
                lens.FieldOfView = Mathf.Clamp(baseFov + hitTraumaSpring.CurrentFovOffset, 15f, 160f);
                cinemachineCamera.Lens = lens;
            }
        }

        /// <summary>
        /// 输入 Look 向量更新相机 Pitch 与玩家身体 Yaw。
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
            currentPitch = Mathf.Clamp(currentPitch + lookInput.y * pitchSensitivity * verticalDirection, pitchLimits.x, pitchLimits.y);
            ApplyPitchToPanTilt();

            if (viewmodelController == null)
            {
                viewmodelController = FindAnyObjectByType<FirstPersonViewmodelController>();
            }

            viewmodelController?.ApplyLookInput(lookInput);
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

        private void ApplyPitchToPanTilt()
        {
            ApplyDynamicCameraOffsets();
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
                        Debug.LogError("[第一人称相机诊断] CameraInputReader 未配置。", this);
                    }

                    return;
                }
            }

            ApplyLookInput(cameraInputReader.ReadLook());
        }

        private void HandleFovChanged(float newFov)
        {
            SetBaseFov(newFov);
        }

        private void InitializeOrbitIfNeeded()
        {
            if (orbitInitialized)
            {
                return;
            }

            currentPitch = Mathf.Clamp(0f, pitchLimits.x, pitchLimits.y);
            orbitInitialized = true;
        }

        private void ClampOrbit()
        {
            InitializeOrbitIfNeeded();
            currentPitch = Mathf.Clamp(currentPitch, pitchLimits.x, pitchLimits.y);
        }

        private void CacheComponents()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
            hardLock = GetComponent<CinemachineHardLockToTarget>();
            panTilt = GetComponent<CinemachinePanTilt>();
            combatCameraFeedback = FindAnyObjectByType<CombatCameraFeedback>();
        }

        private void ResolveReferences()
        {
            if (cameraInputReader == null)
            {
                cameraInputReader = GetComponent<CameraInputReader>() ?? FindAnyObjectByType<CameraInputReader>();
            }

            if (cameraInputReader != null)
            {
                missingInputReaderReported = false;
            }

            if (viewmodelController == null)
            {
                viewmodelController = FindAnyObjectByType<FirstPersonViewmodelController>();
            }
        }

        private void NormalizeConfiguration()
        {
            pitchSensitivity = Mathf.Max(0f, pitchSensitivity);
            yawSensitivity = Mathf.Max(0f, yawSensitivity);
            pitchLimits = NormalizeLimits(pitchLimits, -89f, 89f);
            baseFov = Mathf.Clamp(baseFov, GameSettingsService.MinFov, GameSettingsService.MaxFov);
        }

        private static Vector2 NormalizeLimits(Vector2 limits, float min, float max)
        {
            limits.x = Mathf.Clamp(limits.x, min, max);
            limits.y = Mathf.Clamp(limits.y, min, max);
            if (limits.y < limits.x)
            {
                (limits.x, limits.y) = (limits.y, limits.x);
            }

            return limits;
        }

        private static void ConfigureAxisOnComponent(Component component, string axisName, Vector2 limits, float value)
        {
            if (component == null) return;
            object axis = GetMember(component, axisName);
            if (axis == null) return;

            float clampedValue = Mathf.Clamp(value, limits.x, limits.y);
            SetMember(axis, "Range", limits);
            SetMember(axis, "Wrap", false);
            SetMember(axis, "Center", clampedValue);
            SetMember(axis, "Value", clampedValue);
            object recentering = GetMember(axis, "Recentering");
            if (recentering != null)
            {
                SetMember(recentering, "Enabled", false);
                SetMember(axis, "Recentering", recentering);
            }
            SetMember(component, axisName, axis);
        }

        private static void SetAxisValueOnComponent(Component component, string axisName, float value, Vector2 limits)
        {
            if (component == null) return;
            object axis = GetMember(component, axisName);
            if (axis == null) return;

            SetMember(axis, "Value", Mathf.Clamp(value, limits.x, limits.y));
            SetMember(component, axisName, axis);
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            System.Reflection.FieldInfo field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field != null) return field.GetValue(target);
            System.Reflection.PropertyInfo property = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            return property != null && property.CanRead ? property.GetValue(target) : null;
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null) return;
            Type type = target.GetType();
            System.Reflection.FieldInfo field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            System.Reflection.PropertyInfo property = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
            }
        }
    }
}
