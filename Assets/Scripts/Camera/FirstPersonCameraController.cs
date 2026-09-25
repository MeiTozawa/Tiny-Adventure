using System;
using UnityEngine;
using Unity.Cinemachine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 一人称Cinemachineカメラコントローラー。
    /// プレイヤー頭部（CameraTarget）の追従、Look入力による回転（水平は身体Yaw、垂直はPanTilt）、
    /// FOV設定、および被弾トラウマスプリング揺れを管理します。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineCamera))]
    [RequireComponent(typeof(CinemachineHardLockToTarget))]
    [RequireComponent(typeof(CinemachinePanTilt))]
    public sealed class FirstPersonCameraController : MonoBehaviour
    {
        private const string PlayerRootName = "Player";
        private const string CameraTargetName = "CameraTarget";

        [Header("追従対象")]
        [SerializeField] private Transform playerCameraTarget;
        [SerializeField] private CameraInputReader cameraInputReader;

        [Header("俯仰・感度")]
        [SerializeField] private Vector2 pitchLimits;
        [SerializeField, Min(0f)] private float pitchSensitivity;
        [SerializeField, Min(0f)] private float yawSensitivity;
        [SerializeField] private bool invertVerticalLook;
        [SerializeField] private FirstPersonViewmodelController viewmodelController;

        [Header("Cinemachine コンポーネント (Awakeキャッシュ)")]
        private CinemachineCamera cinemachineCamera;
        private CinemachineHardLockToTarget hardLock;
        private CinemachinePanTilt panTilt;

        [Header("視野角 (FOV)")]
        [SerializeField, Range(GameSettingsService.MinFov, GameSettingsService.MaxFov)]
        private float baseFov;

        [Header("レンズ視野角クランプ限界")]
        [SerializeField] private float minLensFov;
        [SerializeField] private float maxLensFov;

        private CombatCameraFeedback combatCameraFeedback;
        private Transform playerRootTransform;
        private float currentPitch;
        private bool missingTargetReported;
        private bool missingInputReaderReported;
        private bool isInputSuspended;
        private bool isSettingsSubscribed;

        /// <summary>現在の基準FOV視野角。</summary>
        public float BaseFov => baseFov;

        /// <summary>現在の俯仰角（Pitch）。</summary>
        public float CurrentPitch => currentPitch;

        private float currentTraumaPitch;
        private float currentTraumaRoll;

        /// <summary>被弾による現在の後仰角オフセット（Pitch）。</summary>
        public float CurrentTraumaPitch => currentTraumaPitch;

        /// <summary>被弾による現在の側傾斜角オフセット（Roll / Dutch）。</summary>
        public float CurrentTraumaRoll => currentTraumaRoll;

        /// <summary>被弾による現在の偏航角オフセット（Yaw）。</summary>
        public float CurrentTraumaYaw => 0f;

        /// <summary>被弾による現在の視野角オフセット（FOV）。</summary>
        public float CurrentTraumaFovOffset => 0f;

        /// <summary>マウス照準と被弾揺れを合算した実効俯仰角。</summary>
        public float TotalPitch => Mathf.Clamp(currentPitch + currentTraumaPitch, pitchLimits.x, pitchLimits.y);

        /// <summary>Cinemachine仮想カメラコンポーネント。</summary>
        public Component Rig => cinemachineCamera;

        /// <summary>一人称カメラの追従・注視ターゲット。</summary>
        public Transform PlayerCameraTarget => playerCameraTarget;

        /// <summary>追従対象のカメラターゲットを設定します。</summary>
        public void SetPlayerCameraTarget(Transform target) => playerCameraTarget = target;

        /// <summary>視点入力の中断フラグ（設定ダイアログ表示時など）。</summary>
        public bool IsInputSuspended
        {
            get => isInputSuspended;
            set => isInputSuspended = value;
        }

        private IPlayerViewmodel activeViewmodel;

        /// <summary>一人称ビューモデルコントローラー。</summary>
        public IPlayerViewmodel ViewmodelController => activeViewmodel ?? viewmodelController;

        /// <summary>一人称ビューモデルコントローラーを設定します。</summary>
        public void SetViewmodelController(IPlayerViewmodel controller)
        {
            activeViewmodel = controller;
            if (controller is FirstPersonViewmodelController fpvm) viewmodelController = fpvm;
        }

        private IGameSettingsService settingsService;

        [Inject]
        public void Construct(
            IPlayerViewmodel viewmodel = null,
            IGameSettingsService settings = null)
        {
            if (viewmodel != null)
            {
                activeViewmodel = viewmodel;
                if (viewmodel is FirstPersonViewmodelController fpvm) viewmodelController = fpvm;
            }
            if (settings != null) settingsService = settings;
        }

        public void SetInputReader(CameraInputReader inputReader)
        {
            if (inputReader != null) cameraInputReader = inputReader;
        }

        public void SetCameraFeedback(CombatCameraFeedback cameraFeedback)
        {
            if (cameraFeedback != null) combatCameraFeedback = cameraFeedback;
        }

        private void Awake()
        {
            activeViewmodel = viewmodelController;
            NormalizeConfiguration();
            currentPitch = Mathf.Clamp(0f, pitchLimits.x, pitchLimits.y);
            CacheComponents();
            ResolvePlayerCameraTarget();
            ApplyRigConfiguration();
        }

        private void OnEnable()
        {
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
            var settings = settingsService ?? GameSettingsService.Instance;
            if (settings != null)
            {
                settings.FovChanged += HandleFovChanged;
                isSettingsSubscribed = true;
                SetBaseFov(settings.CurrentFov);
            }
        }

        private void UnsubscribeFromSettings()
        {
            if (!isSettingsSubscribed) return;
            var settings = settingsService ?? GameSettingsService.Instance;
            if (settings != null)
            {
                settings.FovChanged -= HandleFovChanged;
            }
            isSettingsSubscribed = false;
        }

        private void Update()
        {
            if (!isInputSuspended && !PauseService.Instance.IsPaused)
            {
                UpdateOrbitFromLookInput();
            }

            if (!PauseService.Instance.IsPaused)
            {
                if (currentTraumaPitch > 0.001f || Mathf.Abs(currentTraumaRoll) > 0.001f)
                {
                    UpdateTrauma(Time.deltaTime);
                }
                else
                {
                    ApplyDynamicCameraOffsets();
                }
            }
        }

        private void OnValidate()
        {
            NormalizeConfiguration();
            ClampOrbit();
            CacheComponents();
            ApplyRigConfiguration();
        }

        /// <summary>基準FOVを設定し、仮想カメラとフィードバックコントローラーへ反映します。</summary>
        public void SetBaseFov(float fov)
        {
            baseFov = Mathf.Clamp(fov, GameSettingsService.MinFov, GameSettingsService.MaxFov);
            if (cinemachineCamera == null)
            {
                CacheComponents();
            }

            UpdateCameraLens();

            if (combatCameraFeedback != null)
            {
                combatCameraFeedback.SetBaseFov(baseFov);
            }
        }

        /// <summary>Player配下のCameraTarget注視点を解決・設定します。</summary>
        public bool ResolvePlayerCameraTarget()
        {
            playerRootTransform = playerCameraTarget.parent;
            cinemachineCamera.Follow = playerCameraTarget;
            cinemachineCamera.LookAt = playerCameraTarget;
            return true;
        }

        /// <summary>リグ設定をCinemachine各コンポーネントへ適用します。</summary>
        public void ApplyRigConfiguration()
        {
            cinemachineCamera.Follow = playerCameraTarget;
            cinemachineCamera.LookAt = playerCameraTarget;
            UpdateCameraLens();

            panTilt.ReferenceFrame = CinemachinePanTilt.ReferenceFrames.TrackingTarget;
            ConfigureAxis(ref panTilt.TiltAxis, pitchLimits, currentPitch);
            ConfigureAxis(ref panTilt.PanAxis, new Vector2(-180f, 180f), 0f);
        }

        /// <summary>局所受撃方向と強度を受け取り、動的カメラオフセットを更新します。</summary>
        public void ApplyTraumaImpulse(Vector3 localDirection, float intensity = 1f)
        {
            float safeIntensity = Mathf.Max(0.1f, intensity);
            currentTraumaPitch = Mathf.Clamp(currentTraumaPitch + 1.2f * safeIntensity, 0f, 15f);
            if (Mathf.Abs(localDirection.x) > 0.01f)
            {
                currentTraumaRoll = Mathf.Clamp(currentTraumaRoll + localDirection.x * 2.5f * safeIntensity, -20f, 20f);
            }
            ApplyDynamicCameraOffsets();
        }

        /// <summary>動的カメラオフセットを時間更新します。</summary>
        public void UpdateTrauma(float deltaTime)
        {
            float decay = Mathf.Clamp01(deltaTime * 10f);
            currentTraumaPitch = Mathf.Lerp(currentTraumaPitch, 0f, decay);
            currentTraumaRoll = Mathf.Lerp(currentTraumaRoll, 0f, decay);
            if (currentTraumaPitch < 0.01f) currentTraumaPitch = 0f;
            if (Mathf.Abs(currentTraumaRoll) < 0.01f) currentTraumaRoll = 0f;
            ApplyDynamicCameraOffsets();
        }

        /// <summary>動的カメラオフセットを初期状態へリセットします。</summary>
        public void ResetTrauma()
        {
            currentTraumaPitch = 0f;
            currentTraumaRoll = 0f;
            ApplyDynamicCameraOffsets();
        }

        /// <summary>照準角をCinemachine各コンポーネントに反映します。</summary>
        public void ApplyDynamicCameraOffsets()
        {
            panTilt.TiltAxis.Value = TotalPitch;
            panTilt.PanAxis.Value = 0f;
            UpdateCameraLens();
        }

        /// <summary>Look入力ベクトルからカメラPitchと身体Yawを更新します。</summary>
        public void ApplyLookInput(Vector2 lookInput)
        {
            if (lookInput.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            RotatePlayerFromHorizontalLook(lookInput.x);

            float verticalDirection = invertVerticalLook ? 1f : -1f;
            currentPitch = Mathf.Clamp(currentPitch + lookInput.y * pitchSensitivity * verticalDirection, pitchLimits.x, pitchLimits.y);
            ApplyPitchToPanTilt();
            activeViewmodel?.ApplyLookInput(lookInput);
        }

        private void RotatePlayerFromHorizontalLook(float horizontalLook)
        {
            if (Mathf.Abs(horizontalLook) <= Mathf.Epsilon)
            {
                return;
            }

            playerRootTransform.Rotate(Vector3.up, horizontalLook * yawSensitivity, Space.World);
        }

        private void ApplyPitchToPanTilt()
        {
            ApplyDynamicCameraOffsets();
        }

        private void UpdateOrbitFromLookInput()
        {
            ApplyLookInput(cameraInputReader.ReadLook());
        }

        private void HandleFovChanged(float newFov)
        {
            SetBaseFov(newFov);
        }

        private void ClampOrbit()
        {
            currentPitch = Mathf.Clamp(currentPitch, pitchLimits.x, pitchLimits.y);
        }

        private void CacheComponents()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
            hardLock = GetComponent<CinemachineHardLockToTarget>();
            panTilt = GetComponent<CinemachinePanTilt>();
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

        private void UpdateCameraLens()
        {
            LensSettings lens = cinemachineCamera.Lens;
            lens.Dutch = currentTraumaRoll;
            float minFov = minLensFov > 0f ? minLensFov : 15f;
            float maxFov = maxLensFov > minFov ? maxLensFov : 160f;
            lens.FieldOfView = Mathf.Clamp(baseFov, minFov, maxFov);
            cinemachineCamera.Lens = lens;
        }

        private static void ConfigureAxis(ref InputAxis axis, Vector2 limits, float value)
        {
            float clampedValue = Mathf.Clamp(value, limits.x, limits.y);
            axis.Range = limits;
            axis.Wrap = false;
            axis.Center = clampedValue;
            axis.Value = clampedValue;
            axis.Recentering.Enabled = false;
        }
    }
}
