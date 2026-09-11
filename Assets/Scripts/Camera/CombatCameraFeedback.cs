using System;
using UnityEngine;
using Unity.Cinemachine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗相机反馈控制器。
    /// 负责受击与命中时的 Cinemachine Impulse 相机震动与 FOV 瞬态冲击反馈。
    /// 严格遵循架构约束：严禁直接改写相机 Transform，必须通过 Cinemachine Impulse 与 Lens 适配器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCameraFeedback : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("配置引用")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        [Header("Cinemachine 冲量源引用（未设置时自动查找）")]
        [SerializeField]
        private CinemachineImpulseSource impulseSource;

        [SerializeField]
        private Camera targetCamera;

        private ICameraImpulseEmitter impulseEmitter;
        private IFovPunchAdapter fovPunchAdapter;
        private ICombatFeedbackProfileProvider profileProvider;

        /// <summary>诊断通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (fovPunchAdapter is UnityFovPunchAdapter unityFov)
            {
                unityFov.Update(Time.unscaledDeltaTime);
            }
        }

        /// <summary>
        /// 测试用配置与依赖注入。
        /// </summary>
        public void ConfigureForTests(
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

        /// <summary>
        /// 命中反馈入口：触发冲量震动与 FOV 冲击。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("未配置 CombatFeedbackProfile，跳过相机反馈。", false);
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

            // 1. 生成冲量震动
            Vector3 impulseDir = request.Direction.sqrMagnitude > 0.0001f
                ? request.Direction.normalized
                : Vector3.down;

            if (impulseEmitter != null)
            {
                try
                {
                    impulseEmitter.Generate(impulseSettings, impulseDir);
                }
                catch (Exception ex)
                {
                    ReportDiagnostic($"Cinemachine Impulse 发射异常：{ex.Message}", true);
                }
            }

            // 2. FOV 瞬态冲击
            if (fovPunchAdapter != null && Mathf.Abs(fovOffset) > 0.001f)
            {
                try
                {
                    fovPunchAdapter.Punch(fovOffset, camSettings.enterSeconds, camSettings.recoverSeconds);
                }
                catch (Exception ex)
                {
                    ReportDiagnostic($"相机 FOV 冲击异常：{ex.Message}", true);
                }
            }
        }

        /// <summary>
        /// 清理运行时状态，恢复初始相机 FOV。
        /// </summary>
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
                if (impulseSource == null)
                {
                    impulseSource = GetComponentInChildren<CinemachineImpulseSource>(true) ?? FindAnyObjectByType<CinemachineImpulseSource>();
                }
                impulseEmitter = new UnityImpulseEmitter(impulseSource);
            }

            if (fovPunchAdapter == null)
            {
                if (targetCamera == null)
                {
                    targetCamera = Camera.main ?? FindAnyObjectByType<Camera>();
                }
                fovPunchAdapter = new UnityFovPunchAdapter(targetCamera);
            }
        }

        private void ResolveReferences()
        {
            EnsureAdapters();
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[相机反馈诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[相机反馈诊断] {message}", this);
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
        }
    }
}
