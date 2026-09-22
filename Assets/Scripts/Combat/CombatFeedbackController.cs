using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットフィードバック中央ディスパッチャー。
    /// DamageService.HitFeedbackRequested を購読し、リクエストの正規化・重複排除・通常/致命ヒットの分類を行い、
    /// 各サブモジュール（アニメーション、VFX、SE、HitStop、カメラ）へ安全に配信します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackController : MonoBehaviour
    {
        [Header("サービス参照")]
        [SerializeField]
        private DamageService damageService;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        [Header("サブモジュール参照")]
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

        private readonly HashSet<FeedbackDeduplicationKey> handledKeys = new();
        private bool acceptNewFeedback = true;
        private bool isSubscribed;

        /// <summary>ヒットフィードバック配信完了イベント。</summary>
        public event Action<CombatFeedbackRequest> FeedbackDispatched;

        /// <summary>フィードバック診断メッセージ通知（警告および例外隔離ログを含む）。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;
        public bool AcceptNewFeedback => acceptNewFeedback;
        public CombatTimeSlowController TimeSlowController => timeSlowController;

        [Inject]
        public void Construct(DamageService damage = null, GameFlowController flow = null)
        {
            if (damage != null) damageService = damage;
            if (flow != null)
            {
                gameFlowController = flow;
                stateProvider = flow;
            }
        }

        private void Awake()
        {
            stateProvider ??= gameFlowController;
            if (animationFeedback == null) animationFeedback = GetComponentInChildren<CombatAnimationFeedback>(true);
            if (vfxController == null) vfxController = GetComponentInChildren<CombatVfxController>(true);
            if (audioController == null) audioController = GetComponentInChildren<CombatAudioController>(true);
            if (hitStopController == null) hitStopController = GetComponentInChildren<HitStopController>(true);
            if (cameraFeedback == null) cameraFeedback = GetComponentInChildren<CombatCameraFeedback>(true);
            if (timeSlowController == null) timeSlowController = GetComponentInChildren<CombatTimeSlowController>(true);

            if (feedbackProfile == null)
            {
                if (vfxController != null && vfxController.ProfileProvider is CombatFeedbackProfile vfxProfile)
                    feedbackProfile = vfxProfile;
                else if (audioController != null && audioController.ProfileProvider is CombatFeedbackProfile audioProfile)
                    feedbackProfile = audioProfile;
                else if (hitStopController != null && hitStopController.ProfileProvider is CombatFeedbackProfile hitStopProfile)
                    feedbackProfile = hitStopProfile;
                else if (cameraFeedback != null && cameraFeedback.ProfileProvider is CombatFeedbackProfile cameraProfile)
                    feedbackProfile = cameraProfile;
            }
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            ClearRuntimeState();
        }

        /// <summary>
        /// 依存関係を設定します。
        /// </summary>
        internal void SetDependencies(
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
        /// ランタイム状態と重複排除キーをクリアします。
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
                        ReportDiagnostic($"サブモジュール「{modules[i]?.GetType().Name}」のクリーンアップ例外: {ex.Message}", true);
                    }
                }
            }
        }

        /// <summary>
        /// 検証を行い、不変のヒットフィードバックリクエストを構築します。
        /// </summary>
        public bool TryBuildRequest(
            CombatantMarker target,
            DamageRequest damage,
            out CombatFeedbackRequest feedback,
            out string diagnostic)
        {
            if (target == null || !target.IsIdentityValid)
            {
                diagnostic = "対象が null または無効なため、ヒットフィードバックリクエストを構築できません。";
                ReportDiagnostic(diagnostic, false);
                feedback = default;
                return false;
            }

            if (!damage.IsStructurallyValid)
            {
                diagnostic = $"ダメージリクエストが無効なため、対象「{target.CombatantId}」のヒットフィードバックリクエストを構築できません。";
                ReportDiagnostic(diagnostic, false);
                feedback = default;
                return false;
            }

            CombatantMarker source = damage.Source;
            if (source == null || !source.IsIdentityValid)
            {
                diagnostic = $"攻撃元が null または無効なため、対象「{target.CombatantId}」のヒットフィードバックリクエストを構築できません。";
                ReportDiagnostic(diagnostic, false);
                feedback = default;
                return false;
            }

            // 向きを計算: Source -> Target。重なりまたは至近距離の場合は Target.forward、最後に Vector3.forward にフォールバック
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

            // ヒット位置
            Vector3 hitPoint = damage.HitPoint != Vector3.zero ? damage.HitPoint : target.transform.position;

            // 通常 / 致命の分類: 対象の HealthComponent を確認
            HealthComponent targetHealth = target != null ? target.Health : null;
            CombatHitType hitType;
            if (targetHealth == null)
            {
                diagnostic = $"対象「{target.CombatantId}」に HealthComponent がアタッチされていないため、通常ヒットとして扱います。";
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
                ReportDiagnostic("ゲーム終了状態のため、以降の新規フィードバックリクエストを無視しました。", false);
                return;
            }

            bool isTerminal = stateProvider != null && stateProvider.CurrentState != GameplayState.Running;
            if (isTerminal)
            {
                if (!allowTerminal)
                {
                    acceptNewFeedback = false;
                    ReportDiagnostic("ゲーム終了状態で終了時フィードバックが無効なため、新規リクエストを無視しました。", false);
                    return;
                }

                // 終了時の致命ヒットフィードバックを許可しますが、以降のリクエストは即座に遮断します
                acceptNewFeedback = false;
            }

            if (!TryBuildRequest(target, damage, out CombatFeedbackRequest request, out string diagnostic))
            {
                return;
            }

            // 重複排除チェック
            if (handledKeys.Contains(request.DeduplicationKey))
            {
                ReportDiagnostic($"同一攻撃シーケンス「{request.Damage.AttackSequenceId}」による対象「{target.CombatantId}」へのフィードバックは既に処理済みなため、重複ヒットを無視しました。", false);
                return;
            }

            handledKeys.Add(request.DeduplicationKey);

            if (isTerminal || (request.HitType == CombatHitType.Lethal && request.IsPlayerTarget))
            {
                acceptNewFeedback = false;
            }

            // プレイヤー攻撃命中時の敵微小ノックバック（通常 0.15m、致命 0.35m）の適用
            // 刃の物理的重量感を演出し、被弾前傾モーションによるプレイヤーカメラとの穿模（めり込み）を防止
            if (request.IsPlayerAttack && !request.IsPlayerTarget && target != null)
            {
                var knockbackReceiver = target.KnockbackReceiver;
                if (knockbackReceiver != null)
                {
                    float knockbackDistance = request.HitType == CombatHitType.Lethal ? 0.35f : 0.15f;
                    knockbackReceiver.ApplyKnockback(request.Direction, knockbackDistance);
                }
            }

            // イベント通知の配信
            FeedbackDispatched?.Invoke(request);

            // 各サブモジュールを安全に順次呼び出し
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
                        ReportDiagnostic($"サブモジュール「{module.GetType().Name}」のヒットフィードバック実行例外: {ex.Message}", true);
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
                Debug.LogError($"[戦闘フィードバック診断] {message}", this);
            }
            else
            {
                Debug.Log($"[戦闘フィードバック診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
