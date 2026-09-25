using System;
using UnityEngine;
using Unity.Cinemachine;
using VContainer;

namespace TinyAdventure {
    /// <summary>
    /// 被弾・命中時のCinemachineインパルス振動およびFOV瞬態衝撃フィードバックを管理します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCameraFeedback : MonoBehaviour, ICombatFeedbackModule {
        [Header("設定")] [SerializeField] private CombatFeedbackProfile feedbackProfile;

        [Header("Cinemachine インパルス参照")] [SerializeField]
        private CinemachineImpulseSource impulseSource;

        [SerializeField] private Camera targetCamera;

        [Header("一人称受撃コントローラー参照")] [SerializeField]
        private FirstPersonCameraController fpCameraController;

        [SerializeField] private FirstPersonViewmodelController viewmodelController;

        [SerializeField] private Transform playerTransform;

        [Header("プレイヤー被弾演出チューニング")] [Tooltip("プレイヤー被弾時のカメラインパルス上向きバイアス成分")] [SerializeField]
        private float playerHurtImpulseUpwardBias = 0.35f;

        [Tooltip("通常被弾時のカメラ・腕部Jolt衝撃強度")] [SerializeField]
        private float normalHitTraumaIntensity = 1.0f;

        [Tooltip("致命被弾時のカメラ・腕部Jolt衝撃強度")] [SerializeField]
        private float lethalHitTraumaIntensity = 1.4f;

        private ICameraImpulseEmitter impulseEmitter;
        private IFovPunchAdapter fovPunchAdapter;
        private UnityFovPunchAdapter activeUnityFov;
        private ICombatFeedbackProfileProvider profileProvider;

        private IPlayerViewmodel activeViewmodel;

        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;
        public FirstPersonCameraController FpCameraController => fpCameraController;
        public IPlayerViewmodel ViewmodelController => activeViewmodel ?? viewmodelController;

        [Inject]
        public void Construct(
            FirstPersonCameraController camController = null,
            IPlayerViewmodel viewmodel = null)
        {
            if (camController != null) fpCameraController = camController;
            if (viewmodel != null)
            {
                activeViewmodel = viewmodel;
                if (viewmodel is FirstPersonViewmodelController fpvm) viewmodelController = fpvm;
            }
        }

        private void Awake()
        {
            impulseEmitter = new UnityImpulseEmitter(impulseSource);
            activeUnityFov = new UnityFovPunchAdapter(targetCamera);
            fovPunchAdapter = activeUnityFov;
            activeViewmodel = viewmodelController;
        }

        private void Update()
        {
            activeUnityFov.Update(Time.unscaledDeltaTime);
        }

        /// <summary>テスト用または外部から依存関係を注入します。</summary>
        public void ConstructForTesting(
            ICameraImpulseEmitter impulse,
            IFovPunchAdapter fov,
            ICombatFeedbackProfileProvider profile = null)
        {
            impulseEmitter = impulse;
            fovPunchAdapter = fov;
            activeUnityFov = fov as UnityFovPunchAdapter;
            if (profile != null)
            {
                profileProvider = profile;
            }

            ClearRuntimeState();
        }

        /// <summary>一人称受撃コントローラーを設定します。</summary>
        public void ConfigurePlayerHitControllers(
            FirstPersonCameraController fpCam,
            IPlayerViewmodel viewmodel,
            Transform player = null)
        {
            fpCameraController = fpCam;
            activeViewmodel = viewmodel;
            if (viewmodel is FirstPersonViewmodelController fpvm) viewmodelController = fpvm;
            playerTransform = player;
        }

        /// <summary>命中・被弾フィードバックを実行します。</summary>
        public void Play(CombatFeedbackRequest request)
        {
            var camSettings = ProfileProvider.Camera;
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
                impulseDir = (impulseDir + Vector3.up * playerHurtImpulseUpwardBias).normalized;
            }

            impulseEmitter.Generate(impulseSettings, impulseDir);

            if (Mathf.Abs(fovOffset) > 0.001f)
            {
                fovPunchAdapter.Punch(fovOffset, camSettings.enterSeconds, camSettings.recoverSeconds);
            }
        }

        /// <summary>基準FOVを設定します。</summary>
        public void SetBaseFov(float baseFov)
        {
            fovPunchAdapter.SetBaseFov(baseFov);
        }

        /// <summary>実行時状態をクリアし、初期FOVに復帰させます。</summary>
        public void ClearRuntimeState()
        {
            fovPunchAdapter.ClearRuntimeState();
        }


        private void ApplyPlayerHitDynamics(CombatFeedbackRequest request, float amplitude)
        {
            Vector3 worldDir = request.Direction.sqrMagnitude > 0.0001f ? request.Direction.normalized : Vector3.back;
            Vector3 localDir = playerTransform.InverseTransformDirection(worldDir);
            // 物理受撃スプリング及びビューモデルJolt反動の強度（通常・致命打撃をInspector設定値から取得）
            float intensity = request.HitType == CombatHitType.Lethal
                ? lethalHitTraumaIntensity
                : normalHitTraumaIntensity;

            fpCameraController.ApplyTraumaImpulse(localDir, intensity);


            if (activeViewmodel != null)
            {
                activeViewmodel.TriggerImpactJolt(localDir, intensity);
            }
        }

        private sealed class UnityImpulseEmitter : ICameraImpulseEmitter {
            private readonly CinemachineImpulseSource source;

            public UnityImpulseEmitter(CinemachineImpulseSource source)
            {
                this.source = source;
            }

            public void Generate(ImpulseFeedbackSettings settings, Vector3 direction)
            {
                Vector3 vel = (direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.down) *
                    settings.amplitude;
                source.GenerateImpulseWithVelocity(vel);
            }
        }

        private sealed class UnityFovPunchAdapter : IFovPunchAdapter {
            private readonly Camera camera;
            private float baseFov;
            private float targetOffset;
            private float currentOffset;
            private float recoverSpeed;

            public UnityFovPunchAdapter(Camera camera)
            {
                this.camera = camera;
                baseFov = camera.fieldOfView;
            }

            public void Punch(float offset, float enterSeconds, float recoverSeconds)
            {
                currentOffset = offset;
                targetOffset = 0f;
                recoverSpeed = Mathf.Abs(offset) / Mathf.Max(0.01f, recoverSeconds);
                camera.fieldOfView = Mathf.Clamp(baseFov + currentOffset, 15f, 160f);
            }

            public void Update(float unscaledDeltaTime)
            {
                if (Mathf.Approximately(currentOffset, targetOffset)) return;

                currentOffset = Mathf.MoveTowards(currentOffset, targetOffset, recoverSpeed * unscaledDeltaTime);
                camera.fieldOfView = Mathf.Clamp(baseFov + currentOffset, 15f, 160f);
            }

            public void ClearRuntimeState()
            {
                camera.fieldOfView = baseFov;
                currentOffset = 0f;
                targetOffset = 0f;
            }

            public void SetBaseFov(float newBaseFov)
            {
                baseFov = newBaseFov;
                camera.fieldOfView = Mathf.Clamp(baseFov + currentOffset, 15f, 160f);
            }
        }
    }
}