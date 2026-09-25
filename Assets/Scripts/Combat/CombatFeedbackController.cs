using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットフィードバック中央ディスパッチャー。
    /// DamageService.HitFeedbackRequested を購読し、リクエストの正規化・重複排除・通常/致命ヒットの分類を行い、
    /// パイプライン（アニメーション、VFX、SE、HitStop、カメラシェイク）へ順次配信します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackController : MonoBehaviour, IHitFeedbackReceiver
    {
        [Header("サービス参照")]
        [SerializeField] private DamageService damageService;
        [SerializeField] private GameFlowController gameFlowController;

        [Header("パイプライン設定・参照")]
        [SerializeField] private CombatFeedbackProfile feedbackProfile;
        [SerializeField] private HitStopController hitStopController;
        [SerializeField] private CombatCameraFeedback cameraFeedback;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private CinemachineImpulseSource impulseSource;

        private IGameplayStateProvider stateProvider;
        private AudioFeedbackHandler audioHandler;

        private readonly HashSet<FeedbackDeduplicationKey> handledKeys = new();
        private readonly List<ICombatFeedbackModule> pipelineModules = new();
        private readonly List<ICombatFeedbackModule> customHandlers = new();
        private bool acceptNewFeedback = true;


        public ICombatFeedbackProfileProvider ProfileProvider => feedbackProfile;
        public bool AcceptNewFeedback => acceptNewFeedback;

        [Inject]
        public void Construct(IGameplayStateProvider flow = null)
        {
            if (flow is GameFlowController gfc) gameFlowController = gfc;
            stateProvider = flow;
        }

        private void Awake()
        {
            stateProvider ??= gameFlowController;
            InitializePipeline();
        }

        private void InitializePipeline()
        {
            pipelineModules.Clear();

            // 1. 被弾アニメーション
            pipelineModules.Add(new AnimationFeedbackHandler());

            // 2. 被弾発光（Hit Flash）
            pipelineModules.Add(new HitFlashFeedbackHandler());

            // 3. オーディオSE
            audioHandler = new AudioFeedbackHandler(audioSource, feedbackProfile);
            pipelineModules.Add(audioHandler);

            // 4. VFX（火花・衝撃波）
            pipelineModules.Add(new VfxFeedbackHandler(feedbackProfile));

            // 5. カメラシェイク（Cinemachine Impulse）
            pipelineModules.Add(new CameraShakeFeedbackHandler(impulseSource, feedbackProfile));

            // 6. ヒットストップ
            pipelineModules.Add(new HitStopFeedbackHandler(hitStopController));
        }

        /// <summary>
        /// 剣撃風切り音（Whoosh）を再生します。
        /// </summary>
        public void PlayAttackWhoosh()
        {
            audioHandler?.PlayWhoosh();
        }

        public void RegisterHandler(ICombatFeedbackModule handler)
        {
            if (handler != null && !customHandlers.Contains(handler))
            {
                customHandlers.Add(handler);
            }
        }

        public void UnregisterHandler(ICombatFeedbackModule handler)
        {
            if (handler != null)
            {
                customHandlers.Remove(handler);
            }
        }

        private void OnDisable()
        {
            ClearRuntimeState();
        }

        public void ClearRuntimeState()
        {
            handledKeys.Clear();
            acceptNewFeedback = true;

            foreach (var t in pipelineModules)
            {
                t?.ClearRuntimeState();
            }

            foreach (var t in customHandlers)
            {
                t?.ClearRuntimeState();
            }
        }

        public Result<CombatFeedbackRequest> BuildRequest(CombatantMarker target, DamageRequest damage)
        {
            if (target == null || !target.IsIdentityValid) return GameError.InvalidParameter;
            if (!damage.IsStructurallyValid) return GameError.InvalidParameter;

            CombatantMarker source = damage.Source;
            if (source == null || !source.IsIdentityValid) return GameError.InvalidParameter;

            Vector3 diff = target.transform.position - source.transform.position;
            Vector3 direction = diff.sqrMagnitude > 0.0001f ? diff.normalized : (target.transform.forward.sqrMagnitude > 0.0001f ? target.transform.forward.normalized : Vector3.forward);
            Vector3 hitPoint = damage.HitPoint != Vector3.zero ? damage.HitPoint : target.transform.position;

            HealthComponent targetHealth = target.Health;
            CombatHitType hitType = (targetHealth == null || !targetHealth.IsAlive || targetHealth.IsInDeathTransition || targetHealth.CurrentHealth <= 0f)
                ? CombatHitType.Lethal
                : CombatHitType.Normal;

            bool isPlayerAttack = source.Faction == CombatantMarker.CombatantFaction.Player;
            bool isPlayerTarget = target.Faction == CombatantMarker.CombatantFaction.Player;
            var deduplicationKey = new FeedbackDeduplicationKey(source, target, damage.AttackSequenceId);

            return new CombatFeedbackRequest(
                hitType,
                source,
                target,
                damage,
                hitPoint,
                direction,
                isPlayerAttack,
                isPlayerTarget,
                deduplicationKey);
        }

        public void OnHitFeedbackRequested(CombatantMarker target, DamageRequest damage)
        {
            if (!acceptNewFeedback) return;

            bool isTerminal = stateProvider.CurrentState != GameplayState.Running;
            if (isTerminal)
            {
                if (!feedbackProfile.AllowTerminalHitFeedback)
                {
                    acceptNewFeedback = false;
                    return;
                }
                acceptNewFeedback = false;
            }

            Result<CombatFeedbackRequest> requestResult = BuildRequest(target, damage);
            if (requestResult.IsErr) return;

            CombatFeedbackRequest request = requestResult.Value;

            if (!handledKeys.Add(request.DeduplicationKey)) return;

            if (isTerminal || request is { HitType: CombatHitType.Lethal, IsPlayerTarget: true })
            {
                acceptNewFeedback = false;
            }

            // プレイヤー攻撃命中時の敵微小ノックバック（通常 0.15m、致命 0.35m）
            if (request is { IsPlayerAttack: true, IsPlayerTarget: false })
            {
                var knockbackReceiver = target.KnockbackReceiver;
                if (knockbackReceiver != null)
                {
                    float knockbackDistance = request.HitType == CombatHitType.Lethal ? 0.35f : 0.15f;
                    knockbackReceiver.ApplyKnockback(request.Direction, knockbackDistance);
                }
            }

            // ターゲット自身へ通知
            target.DispatchHitFeedback(request);


            // パイプラインモジュール順次実行（ゼロGC）
            foreach (var t in pipelineModules)
            {
                try
                {
                    t.Play(request);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CombatFeedbackController] パイプライン実行例外 ({t.GetType().Name}): {ex.Message}", this);
                }
            }

            foreach (var t in customHandlers)
            {
                try
                {
                    t.Play(request);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CombatFeedbackController] カスタムハンドラー例外: {ex.Message}", this);
                }
            }
        }
    }
}
