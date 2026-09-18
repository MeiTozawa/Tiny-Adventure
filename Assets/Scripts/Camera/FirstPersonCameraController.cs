using System;
using UnityEngine;
using Unity.Cinemachine;

namespace TinyAdventure
{
    /// <summary>
    /// 一人称Cinemachineカメラコントローラー。
    /// プレイヤー頭部（CameraTarget）の追従、Look入力による回転（水平は身体Yaw、垂直はPanTilt）、
    /// FOV設定、および被弾トラウマスプリング揺れを管理します。
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

        [Header("俯仰・感度")]
        [SerializeField] private Vector2 pitchLimits = new(-80f, 80f);
        [SerializeField, Min(0f)] private float pitchSensitivity = 0.1f;
        [SerializeField, Min(0f)] private float yawSensitivity = 0.1f;
        [SerializeField] private bool invertVerticalLook;
        [SerializeField] private FirstPersonViewmodelController viewmodelController;

        [Header("視野角 (FOV)")]
        [SerializeField, Range(GameSettingsService.MinFov, GameSettingsService.MaxFov)]
        private float baseFov = GameSettingsService.DefaultFov;

        [Header("被弾カメラ揺れ・物理スプリング")]
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

        /// <summary>現在の基準FOV視野角。</summary>
        public float BaseFov => baseFov;

        /// <summary>現在の俯仰角（Pitch）。</summary>
        public float CurrentPitch => currentPitch;

        /// <summary>被弾カメラ物理スプリング。</summary>
        public CameraHitTraumaSpring HitTraumaSpring => hitTraumaSpring;

        /// <summary>被弾による現在の後仰角オフセット（Pitch）。</summary>
        public float CurrentTraumaPitch => hitTraumaSpring != null ? hitTraumaSpring.CurrentPitch : 0f;

        /// <summary>被弾による現在の側傾斜角オフセット（Roll / Dutch）。</summary>
        public float CurrentTraumaRoll => hitTraumaSpring != null ? hitTraumaSpring.CurrentRoll : 0f;

        /// <summary>被弾による現在の偏航角オフセット（Yaw）。</summary>
        public float CurrentTraumaYaw => hitTraumaSpring != null ? hitTraumaSpring.CurrentYaw : 0f;

        /// <summary>被弾による現在の視野角オフセット（FOV）。</summary>
        public float CurrentTraumaFovOffset => hitTraumaSpring != null ? hitTraumaSpring.CurrentFovOffset : 0f;

        /// <summary>マウス照準と被弾揺れを合算した実効俯仰角。</summary>
        public float TotalPitch => Mathf.Clamp(currentPitch + CurrentTraumaPitch, pitchLimits.x, pitchLimits.y);

        /// <summary>Cinemachine仮想カメラコンポーネント。</summary>
        public Component Rig => cinemachineCamera;

        /// <summary>一人称カメラの追従・注視ターゲット。</summary>
        public Transform PlayerCameraTarget => playerCameraTarget;

        /// <summary>視点入力の中断フラグ（設定ダイアログ表示時など）。</summary>
        public bool IsInputSuspended
        {
            get => isInputSuspended;
            set => isInputSuspended = value;
        }

        /// <summary>一人称ビューモデルコントローラー。</summary>
        public FirstPersonViewmodelController ViewmodelController => viewmodelController;

        /// <summary>一人称ビューモデルコントローラーを設定します。</summary>
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

        /// <summary>基準FOVを設定し、仮想カメラとフィードバックコントローラーへ反映します。</summary>
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

        /// <summary>Player配下のCameraTarget注視点を解決・設定します。</summary>
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
                    Debug.LogError($"[一人称カメラ診断] Playerのカメラターゲット '{PlayerRootName}/{CameraTargetName}' が見つかりません。", this);
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

        /// <summary>リグ設定をCinemachine各コンポーネントへ適用します。</summary>
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

        /// <summary>局所受撃方向と強度を受け取り、カメラ受撃物理スプリングへインパルスを注入します。</summary>
        public void ApplyTraumaImpulse(Vector3 localDirection, float intensity = 1f)
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.ApplyImpact(localDirection, intensity);
            ApplyDynamicCameraOffsets();
        }

        /// <summary>物理スプリングを時間更新し、動的オフセットを適用します。</summary>
        public void UpdateTrauma(float deltaTime)
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.Update(deltaTime);
            ApplyDynamicCameraOffsets();
        }

        /// <summary>受撃スプリングを初期状態へリセットします。</summary>
        public void ResetTrauma()
        {
            hitTraumaSpring ??= new CameraHitTraumaSpring();
            hitTraumaSpring.Reset();
            ApplyDynamicCameraOffsets();
        }

        /// <summary>照準角と受撃スプリング変位をCinemachine各コンポーネントに反映します。</summary>
        public void ApplyDynamicCameraOffsets()
        {
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

        /// <summary>Look入力ベクトルからカメラPitchと身体Yawを更新します。</summary>
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
                        Debug.LogError("[一人称カメラ診断] CameraInputReaderが未設定です。", this);
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
