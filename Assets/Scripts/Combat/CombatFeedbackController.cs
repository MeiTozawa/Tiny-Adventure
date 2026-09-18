using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗打击感反馈中央分发器。
    /// 订阅 DamageService.HitFeedbackRequested，规范化请求，去重，分类普通/致死，
    /// 并安全隔离子模块异常分发至动画、VFX、音频、Hit Stop、相机反馈。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackController : MonoBehaviour
    {
        [Header("服务引用")]
        [SerializeField]
        private DamageService damageService;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        [Header("子模块引用")]
        [SerializeField]
        private CombatAnimationFeedback animationFeedback;

        [SerializeField]
        private CombatVfxController vfxController;

        [SerializeField]
        private CombatAudioController audioController;

        [SerializeField]
        private HitStopController hitStopController;

        [SerializeField]
        private CombatCameraFeedback cameraFeedback;

        [SerializeField]
        private CombatTimeSlowController timeSlowController;

        private IDamageFeedbackSource damageSource;
        private IGameplayStateProvider stateProvider;
        private ICombatFeedbackProfileProvider profileProvider;
        private ICombatFeedbackModule[] runtimeModules;

        private readonly HashSet<FeedbackDeduplicationKey> handledKeys = new HashSet<FeedbackDeduplicationKey>();
        private bool acceptNewFeedback = true;
        private bool isSubscribed;

        /// <summary>命中反馈分发成功通知。</summary>
        public event Action<CombatFeedbackRequest> FeedbackDispatched;

        /// <summary>反馈诊断信息通知（包含警告与异常隔离日志）。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;
        public bool AcceptNewFeedback => acceptNewFeedback;
        public CombatTimeSlowController TimeSlowController => timeSlowController;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            ClearRuntimeState();
        }

        /// <summary>
        /// 配置测试用可替换依赖项。
        /// </summary>
        public void ConfigureForTests(
            IDamageFeedbackSource source,
            IGameplayStateProvider state,
            ICombatFeedbackProfileProvider profile,
            ICombatFeedbackModule[] modules)
        {
            UnsubscribeEvents();

            damageSource = source;
            stateProvider = state;
            profileProvider = profile;
            runtimeModules = modules;

            ClearRuntimeState();
            SubscribeEvents();
        }

        /// <summary>
        /// 清理运行时状态与已处理去重键。
        /// </summary>
        public void ClearRuntimeState()
        {
            handledKeys.Clear();
            acceptNewFeedback = true;

            var modules = GetActiveModules();
            if (modules != null)
            {
                for (int i = 0; i < modules.Length; i++)
                {
                    try
                    {
                        modules[i]?.ClearRuntimeState();
                    }
                    catch (Exception ex)
                    {
                        ReportDiagnostic($"子模块「{modules[i]?.GetType().Name}」清理异常：{ex.Message}", true);
                    }
                }
            }
        }

        /// <summary>
        /// 验证并尝试构造不可变命中反馈请求。
        /// </summary>
        public bool TryBuildRequest(
            CombatantMarker target,
            DamageRequest damage,
            out CombatFeedbackRequest feedback,
            out string diagnostic)
        {
            if (target == null || !target.IsIdentityValid)
            {
                diagnostic = "受击目标为 null 或标识无效，无法构建命中反馈请求。";
                ReportDiagnostic(diagnostic, false);
                feedback = default;
                return false;
            }

            if (!damage.IsStructurallyValid)
            {
                diagnostic = $"伤害请求无效，无法构建目标「{target.CombatantId}」的命中反馈请求。";
                ReportDiagnostic(diagnostic, false);
                feedback = default;
                return false;
            }

            CombatantMarker source = damage.Source;
            if (source == null || !source.IsIdentityValid)
            {
                diagnostic = $"伤害来源为 null 或标识无效，无法构建目标「{target.CombatantId}」的命中反馈请求。";
                ReportDiagnostic(diagnostic, false);
                feedback = default;
                return false;
            }

            // 计算朝向：Source -> Target，若重叠或极近则尝试回退到 Target.forward，最后回退 Vector3.forward
            Vector3 diff = target.transform.position - source.transform.position;
            Vector3 direction;
            if (diff.sqrMagnitude > 0.0001f)
            {
                direction = diff.normalized;
            }
            else if (target.transform.forward.sqrMagnitude > 0.0001f)
            {
                direction = target.transform.forward.normalized;
            }
            else
            {
                direction = Vector3.forward;
            }

            // 命中点
            Vector3 hitPoint = damage.HitPoint != Vector3.zero ? damage.HitPoint : target.transform.position;

            // 分类普通 / 致死：检查目标 HealthComponent
            HealthComponent targetHealth = target.GetComponent<HealthComponent>() ?? target.GetComponentInParent<HealthComponent>();
            CombatHitType hitType;
            if (targetHealth == null)
            {
                diagnostic = $"目标「{target.CombatantId}」未挂载 HealthComponent，按普通受击处理。";
                ReportDiagnostic(diagnostic, false);
                hitType = CombatHitType.Normal;
            }
            else if (!targetHealth.IsAlive || targetHealth.IsInDeathTransition || targetHealth.CurrentHealth <= 0f)
            {
                hitType = CombatHitType.Lethal;
            }
            else
            {
                hitType = CombatHitType.Normal;
            }

            bool isPlayerAttack = source.Faction == CombatantMarker.CombatantFaction.Player;
            bool isPlayerTarget = target.Faction == CombatantMarker.CombatantFaction.Player;
            var deduplicationKey = new FeedbackDeduplicationKey(source, target, damage.AttackSequenceId);

            feedback = new CombatFeedbackRequest(
                hitType,
                source,
                target,
                damage,
                hitPoint,
                direction,
                isPlayerAttack,
                isPlayerTarget,
                deduplicationKey);

            diagnostic = string.Empty;
            LastDiagnostic = string.Empty;
            return true;
        }

        private void OnHitFeedbackRequested(CombatantMarker target, DamageRequest damage)
        {
            var profile = ProfileProvider;
            bool allowTerminal = profile != null && profile.AllowTerminalHitFeedback;

            if (!acceptNewFeedback)
            {
                ReportDiagnostic("已进入终局状态，忽略后续新反馈请求。", false);
                return;
            }

            bool isTerminal = stateProvider != null && stateProvider.CurrentState != GameplayState.Running;
            if (isTerminal)
            {
                if (!allowTerminal)
                {
                    acceptNewFeedback = false;
                    ReportDiagnostic("游戏已进入终局状态且未允许终局反馈，忽略新反馈请求。", false);
                    return;
                }

                // 允许终局致死一击反馈，但后续新请求立即阻断
                acceptNewFeedback = false;
            }

            if (!TryBuildRequest(target, damage, out CombatFeedbackRequest request, out string diagnostic))
            {
                return;
            }

            // 去重检查
            if (handledKeys.Contains(request.DeduplicationKey))
            {
                ReportDiagnostic($"同一攻击系列「{request.Damage.AttackSequenceId}」对目标「{target.CombatantId}」已触发过反馈，忽略重复命中。", false);
                return;
            }

            handledKeys.Add(request.DeduplicationKey);

            if (isTerminal || (request.HitType == CombatHitType.Lethal && request.IsPlayerTarget))
            {
                acceptNewFeedback = false;
            }

            // 分发事件通知
            FeedbackDispatched?.Invoke(request);

            // 依次安全调用子模块
            var modules = GetActiveModules();
            if (modules != null)
            {
                for (int i = 0; i < modules.Length; i++)
                {
                    var module = modules[i];
                    if (module == null)
                    {
                        continue;
                    }

                    try
                    {
                        module.Play(request);
                    }
                    catch (Exception ex)
                    {
                        ReportDiagnostic($"子模块「{module.GetType().Name}」执行命中反馈异常：{ex.Message}", true);
                    }
                }
            }
        }

        private ICombatFeedbackModule[] GetActiveModules()
        {
            if (runtimeModules != null)
            {
                return runtimeModules;
            }

            var list = new List<ICombatFeedbackModule>(6);
            if (animationFeedback != null) list.Add(animationFeedback);
            if (vfxController != null) list.Add(vfxController);
            if (audioController != null) list.Add(audioController);
            if (hitStopController != null) list.Add(hitStopController);
            if (cameraFeedback != null) list.Add(cameraFeedback);
            if (timeSlowController != null) list.Add(timeSlowController);
            return list.ToArray();
        }

        private void ResolveReferences()
        {
            var registry = SceneReferenceRegistry.ActiveInstance;

            if (damageService == null)
            {
                damageService = registry != null && registry.DamageService != null
                    ? registry.DamageService
                    : FindAnyObjectByType<DamageService>();
            }

            if (gameFlowController == null)
            {
                gameFlowController = registry != null && registry.GameFlowController != null
                    ? registry.GameFlowController
                    : FindAnyObjectByType<GameFlowController>();
            }

            if (stateProvider == null)
            {
                stateProvider = gameFlowController;
            }

            if (animationFeedback == null) animationFeedback = GetComponentInChildren<CombatAnimationFeedback>(true);
            if (vfxController == null) vfxController = GetComponentInChildren<CombatVfxController>(true);
            if (audioController == null) audioController = GetComponentInChildren<CombatAudioController>(true);
            if (hitStopController == null) hitStopController = GetComponentInChildren<HitStopController>(true);
            if (cameraFeedback == null) cameraFeedback = GetComponentInChildren<CombatCameraFeedback>(true);
            if (timeSlowController == null) timeSlowController = GetComponentInChildren<CombatTimeSlowController>(true);
        }

        private void SubscribeEvents()
        {
            if (isSubscribed) return;

            IDamageFeedbackSource source = damageSource ?? (IDamageFeedbackSource)damageService;
            if (source != null)
            {
                source.HitFeedbackRequested += OnHitFeedbackRequested;
                isSubscribed = true;
            }
        }

        private void UnsubscribeEvents()
        {
            if (!isSubscribed) return;

            IDamageFeedbackSource source = damageSource ?? (IDamageFeedbackSource)damageService;
            if (source != null)
            {
                source.HitFeedbackRequested -= OnHitFeedbackRequested;
            }
            isSubscribed = false;
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[反馈诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[反馈诊断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
