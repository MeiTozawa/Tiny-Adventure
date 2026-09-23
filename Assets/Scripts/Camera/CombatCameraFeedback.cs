using System;
using UnityEngine;
using Unity.Cinemachine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾・命中時のCinemachineインパルス振動およびFOV瞬態衝撃フィードバックを管理します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCameraFeedback : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("設定")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        [Header("Cinemachine インパルス参照")]
        [SerializeField]
        private CinemachineImpulseSource impulseSource;

        [SerializeField]
        private Camera targetCamera;

        [Header("一人称受撃コントローラー参照")]
        [SerializeField]
        private FirstPersonCameraController fpCameraController;

        [SerializeField]
        private FirstPersonViewmodelController viewmodelController;

        [SerializeField]
        private Transform playerTransform;

        private ICameraImpulseEmitter impulseEmitter;
        private IFovPunchAdapter fovPunchAdapter;
        private ICombatFeedbackProfileProvider profileProvider;

        /// <summary>診断通知イベント。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;
        public FirstPersonCameraController FpCameraController => fpCameraController;
        public FirstPersonViewmodelController ViewmodelController => viewmodelController;

        [Inject]
        public void Construct(
            FirstPersonCameraController camController = null,
            FirstPersonViewmodelController viewmodel = null)
        {
            if (camController != null) fpCameraController = camController;
            if (viewmodel != null) viewmodelController = viewmodel;
        }

        public void SetDependencies(
            FirstPersonCameraController camController = null,
            FirstPersonViewmodelController viewmodel = null,
            Camera camera = null,
            CinemachineImpulseSource impulse = null)
        {
            if (camController != null) fpCameraController = camController;
            if (viewmodel != null) viewmodelController = viewmodel;
            if (camera != null) targetCamera = camera;
            if (impulse != null) impulseSource = impulse;
        }

        private void Awake()
        {
            EnsureAdapters();
        }

        private void Update()
        {
            if (fovPunchAdapter is UnityFovPunchAdapter unityFov)
            {
                unityFov.Update(Time.unscaledDeltaTime);
            }
        }

        /// <summary>依存関係を注入します。</summary>
        internal void SetDependencies(
            ICameraImpulseEmitter impulse,
            IFovPunchAdapter fov,
            ICombatFeedbackProfileProvider profile = null)
        {
            impulseEmitter = impulse;
            fovPunchAdapter = fov;
            if (profile != null)
            {
                profileProvider = profile;
            }
            ClearRuntimeState();
        }

        /// <summary>一人称受撃コントローラーを設定します。</summary>
        public void ConfigurePlayerHitControllers(
            FirstPersonCameraController fpCam,
            FirstPersonViewmodelController viewmodel,
            Transform player = null)
        {
            fpCameraController = fpCam;
            viewmodelController = viewmodel;
            playerTransform = player;
        }

        /// <summary>命中・被弾フィードバックを実行します。</summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("CombatFeedbackProfileが未設定のため、カメラフィードバックをスキップしました。", false);
                return;
            }

            EnsureAdapters();

            var camSettings = profile.Camera;
            ImpulseFeedbackSettings impulseSettings;
            float fovOffset;

            if (request.IsPlayerTarget)
            {
                impulseSettings = camSettings.playerHurtImpulse;
                fovOffset = camSettings.playerHurtFovOffset;
                ApplyPlayerHitDynamics(request, impulseSettings.amplitude);
            }
            else if (request.HitType == CombatHitType.Lethal)
            {
                impulseSettings = camSettings.lethalHitImpulse;
                fovOffset = camSettings.lethalHitFovOffset;
            }
            else
            {
                impulseSettings = camSettings.normalHitImpulse;
                fovOffset = camSettings.normalHitFovOffset;
            }

            Vector3 impulseDir = request.Direction.sqrMagnitude > 0.0001f
                ? request.Direction.normalized
                : Vector3.down;

            if (request.IsPlayerTarget && impulseDir != Vector3.down)
            {
                impulseDir = (impulseDir + Vector3.up * 0.35f).normalized;
            }

            if (impulseEmitter != null)
            {
                try
                {
                    impulseEmitter.Generate(impulseSettings, impulseDir);
                }
                catch (Exception ex)
                {
                    ReportDiagnostic($"Cinemachine Impulse 発射例外: {ex.Message}", true);
                }
            }

            if (fovPunchAdapter != null && Mathf.Abs(fovOffset) > 0.001f)
            {
                try
                {
                    fovPunchAdapter.Punch(fovOffset, camSettings.enterSeconds, camSettings.recoverSeconds);
                }
                catch (Exception ex)
                {
                    ReportDiagnostic($"カメラ FOV 衝撃例外: {ex.Message}", true);
                }
            }
        }

        /// <summary>基準FOVを設定します。</summary>
        public void SetBaseFov(float baseFov)
        {
            EnsureAdapters();
            if (fovPunchAdapter != null)
            {
                fovPunchAdapter.SetBaseFov(baseFov);
            }
        }

        /// <summary>実行時状態をクリアし、初期FOVに復帰させます。</summary>
        public void ClearRuntimeState()
        {
            if (fovPunchAdapter != null)
            {
                fovPunchAdapter.ClearRuntimeState();
            }

            LastDiagnostic = string.Empty;
        }

        private void EnsureAdapters()
        {
            if (impulseEmitter == null)
            {
                impulseEmitter = new UnityImpulseEmitter(impulseSource);
            }

            if (fovPunchAdapter == null)
            {
                if (targetCamera == null)
                {
                    targetCamera = Camera.main;
                }
                fovPunchAdapter = new UnityFovPunchAdapter(targetCamera);
            }
        }

        private void ApplyPlayerHitDynamics(CombatFeedbackRequest request, float amplitude)
        {

            Vector3 worldDir = request.Direction.sqrMagnitude > 0.0001f ? request.Direction.normalized : Vector3.back;
            Vector3 localDir = playerTransform != null
                ? playerTransform.InverseTransformDirection(worldDir)
                : worldDir;
            // 物理受撃スプリング及びビューモデルJolt反動の強度（通常1.0、致命打撃1.4）
            float intensity = request.HitType == CombatHitType.Lethal ? 1.4f : 1.0f;

            if (fpCameraController != null)
            {
                try
                {
                    fpCameraController.ApplyTraumaImpulse(localDir, intensity);
                }
                catch (Exception ex)
                {
                    ReportDiagnostic($"一人称カメラ受撃スプリング印加例外: {ex.Message}", true);
                }
            }

            if (viewmodelController != null)
            {
                try
                {
                    viewmodelController.TriggerImpactJolt(localDir, intensity);
                }
                catch (Exception ex)
                {
                    ReportDiagnostic($"ビューモデル受撃Jolt印加例外: {ex.Message}", true);
                }
            }
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[カメラフィードバック診断] {message}", this);
            }
            else
            {
                Debug.Log($"[カメラフィードバック診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }

        private sealed class UnityImpulseEmitter : ICameraImpulseEmitter
        {
            private readonly CinemachineImpulseSource source;

            public UnityImpulseEmitter(CinemachineImpulseSource source)
            {
                this.source = source;
            }

            public void Generate(ImpulseFeedbackSettings settings, Vector3 direction)
            {
                if (source == null) return;
                Vector3 vel = (direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.down) * settings.amplitude;
                source.GenerateImpulseWithVelocity(vel);
            }
        }

        private sealed class UnityFovPunchAdapter : IFovPunchAdapter
        {
            private readonly Camera camera;
            private float baseFov = 60f;
            private float targetOffset;
            private float currentOffset;
            private float recoverSpeed;
            private bool hasBaseFov;

            public UnityFovPunchAdapter(Camera camera)
            {
                this.camera = camera;
                if (camera != null)
                {
                    baseFov = camera.fieldOfView;
                    hasBaseFov = true;
                }
            }

            public void Punch(float offset, float enterSeconds, float recoverSeconds)
            {
                if (camera == null) return;

                if (!hasBaseFov)
                {
                    baseFov = camera.fieldOfView;
                    hasBaseFov = true;
                }

                currentOffset = offset;
                targetOffset = 0f;
                recoverSpeed = Mathf.Abs(offset) / Mathf.Max(0.01f, recoverSeconds);
                camera.fieldOfView = Mathf.Clamp(baseFov + currentOffset, 15f, 160f);
            }

            public void Update(float unscaledDeltaTime)
            {
                if (camera == null || !hasBaseFov || Mathf.Approximately(currentOffset, targetOffset)) return;

                currentOffset = Mathf.MoveTowards(currentOffset, targetOffset, recoverSpeed * unscaledDeltaTime);
                camera.fieldOfView = Mathf.Clamp(baseFov + currentOffset, 15f, 160f);
            }

            public void ClearRuntimeState()
            {
                if (camera != null && hasBaseFov)
                {
                    camera.fieldOfView = baseFov;
                }
                currentOffset = 0f;
                targetOffset = 0f;
            }

            public void SetBaseFov(float newBaseFov)
            {
                baseFov = newBaseFov;
                hasBaseFov = true;
                if (camera != null)
                {
                    camera.fieldOfView = Mathf.Clamp(baseFov + currentOffset, 15f, 160f);
                }
            }
        }
    }
}
