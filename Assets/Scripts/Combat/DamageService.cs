using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// すべての攻撃から体力へ到達する唯一の正式な入口です。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageService : MonoBehaviour, IDamageFeedbackSource
    {
        [Header("参照")]
        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        // Unity の Inspector はインターフェースを直接シリアライズできないため、
        // SceneReferenceRegistry など ICombatantRegistry を実装するコンポーネントを指定します。
        [SerializeField]
        private MonoBehaviour registryComponent;

        private ICombatantRegistry combatantRegistry;
        private IGameplayStateProvider gameplayStateProvider;
        private IGameplayClock clock;
        private readonly LocalCombatantRegistry localRegistry = new LocalCombatantRegistry();
        private readonly HashSet<DamageKey> acceptedRequests = new HashSet<DamageKey>();

        /// <summary>HealthComponent.Receive が成功した正式なダメージ通知です。</summary>
        public event Action<DamageRequest> DamageAccepted;

        /// <summary>受撃側の視覚・アニメーション反応を開始する通知です。</summary>
        public event Action<CombatantMarker, DamageRequest> HitFeedbackRequested;

        /// <summary>拒否理由または構成異常を日本語で通知します。</summary>
        public event Action<string> DiagnosticReported;

        public ICombatantRegistry CombatantRegistry => combatantRegistry ?? localRegistry;
        public IGameplayStateProvider GameplayStateProvider => gameplayStateProvider;
        public IGameplayClock Clock => clock;
        public string LastDiagnostic { get; private set; } = string.Empty;

        private void Awake()
        {
            if (combatantRegistry == null && registryComponent is ICombatantRegistry configuredRegistry)
            {
                combatantRegistry = configuredRegistry;
            }

            if (gameplayStateProvider == null && gameFlowController != null)
            {
                gameplayStateProvider = gameFlowController;
            }

            if (clock == null && gameplayClock != null)
            {
                clock = gameplayClock;
            }
        }

        private void OnEnable()
        {
        }

        private void OnDisable()
        {
            acceptedRequests.Clear();
        }

        /// <summary>
        /// 外部の SceneReferenceRegistry を正式な登録元として接続します。
        /// null を渡した場合は明示登録用のローカル登録簿へ戻ります。
        /// </summary>
        public void ConfigureRegistry(ICombatantRegistry registry)
        {
            combatantRegistry = registry;
            acceptedRequests.Clear();
        }

        /// <summary>戦闘対象を DamageService の登録簿へ登録します。</summary>
        public bool RegisterCombatant(CombatantMarker combatant)
        {
            if (combatant == null || !combatant.IsIdentityValid)
            {
                ReportDiagnostic(FormatDiagnostic(
                    "無効な戦闘対象を登録できませんでした。",
                    null,
                    combatant,
                    0), true);
                return false;
            }

            return CombatantRegistry.Register(combatant);
        }

        /// <summary>戦闘対象を DamageService の登録簿から解除します。</summary>
        public bool UnregisterCombatant(CombatantMarker combatant)
        {
            if (combatant == null)
            {
                return false;
            }

            acceptedRequests.RemoveWhere(key => ReferenceEquals(key.Source, combatant) || ReferenceEquals(key.Target, combatant));
            return CombatantRegistry.Unregister(combatant);
        }

        /// <summary>
        /// 攻撃ウィンドウ、登録、陣営、範囲、系列重複を含むダメージ要求を検証します。
        /// このメソッドは状態を変更せず、Submit と同じ検証順を使います。
        /// </summary>
        public bool Validate(DamageRequest request, AttackWindowTracker attackWindow, out string diagnostic)
        {

            if (gameplayStateProvider == null)
            {
                diagnostic = FormatDiagnostic("GameFlowController参照がないため、ダメージを受理できません。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, true, out diagnostic);
            }

            if (gameplayStateProvider.CurrentState != GameplayState.Running)
            {
                diagnostic = FormatDiagnostic("終局または初期化中のため、ダメージを無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (request.Source == null || !request.Source.IsIdentityValid || !request.Source.IsAvailableForCombat)
            {
                diagnostic = FormatDiagnostic("無効なダメージ発生元を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (request.Target == null || !request.Target.IsIdentityValid || request.Source == request.Target)
            {
                diagnostic = FormatDiagnostic("無効なダメージ対象を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!DamageRequest.IsFinitePositiveAmount(request.Amount))
            {
                diagnostic = FormatDiagnostic("無効なダメージ量を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!DamageRequest.IsValidAttackSequenceId(request.AttackSequenceId))
            {
                diagnostic = FormatDiagnostic("攻撃系列IDが無効なため、ダメージを無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!request.IsStructurallyValid)
            {
                diagnostic = FormatDiagnostic("不正なダメージ要求を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!request.HasRegisteredParticipants(CombatantRegistry))
            {
                diagnostic = FormatDiagnostic("未登録の戦闘対象を含むダメージ要求を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!IsAllowedFactionPair(request.Source, request.Target, request.AttackKind))
            {
                diagnostic = FormatDiagnostic("攻撃陣営または攻撃種別が不正なため、ダメージを無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!request.Target.IsAvailableForCombat)
            {
                diagnostic = FormatDiagnostic("死亡状態または無効状態の対象へのダメージを無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            HealthComponent targetHealth = FindHealth(request.Target);
            if (targetHealth == null)
            {
                diagnostic = FormatDiagnostic("対象にHealthComponentがないため、ダメージを無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, true, out diagnostic);
            }

            if (!targetHealth.IsAlive)
            {
                diagnostic = FormatDiagnostic("死亡状態の対象へのダメージを無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (attackWindow == null || !attackWindow.IsWindowOpen ||
                attackWindow.Attacker != request.Source ||
                attackWindow.OpenSequenceId != request.AttackSequenceId)
            {
                diagnostic = FormatDiagnostic("攻撃有効時間外の接触を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            if (!IsWithinRange(request.Source, request.Target, attackWindow.AttackRange))
            {
                diagnostic = FormatDiagnostic("攻撃範囲外の対象への接触を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            DamageKey key = new DamageKey(request.Source, request.Target, request.AttackSequenceId);
            if (acceptedRequests.Contains(key))
            {
                diagnostic = FormatDiagnostic("同一攻撃系列で既に命中済みの対象を無視しました。", request.Source, request.Target, request.AttackSequenceId);
                return ReportDiagnostic(diagnostic, false, out diagnostic);
            }

            diagnostic = string.Empty;
            LastDiagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// 検証済みの DamageRequest を HealthComponent へ一度だけ渡します。
        /// DamageService 以外から HealthComponent.Receive を直接呼び出してはいけません。
        /// </summary>
        public bool Submit(DamageRequest request, AttackWindowTracker attackWindow, out string diagnostic)
        {
            if (!Validate(request, attackWindow, out diagnostic))
            {
                return false;
            }

            HealthComponent targetHealth = FindHealth(request.Target);
            if (!targetHealth.Receive(request, out diagnostic))
            {
                diagnostic = FormatDiagnostic(
                    string.IsNullOrEmpty(diagnostic) ? "HealthComponentがダメージを拒否しました。" : diagnostic,
                    request.Source,
                    request.Target,
                    request.AttackSequenceId);
                ReportDiagnostic(diagnostic, false, out diagnostic);
                return false;
            }

            acceptedRequests.Add(new DamageKey(
                request.Source,
                request.Target,
                request.AttackSequenceId));
            LastDiagnostic = string.Empty;
            DamageAccepted?.Invoke(request);
            HitFeedbackRequested?.Invoke(request.Target, request);
            diagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// 攻撃候補から現在のゲーム時刻で DamageRequest を作り、正式に送信します。
        /// </summary>
        public bool Submit(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            AttackWindowTracker attackWindow,
            Vector3 hitPoint,
            out string diagnostic)
        {
            double timestamp = clock != null ? clock.Now : Time.timeAsDouble;
            if (!DamageRequest.TryCreate(
                    CombatantRegistry,
                    source,
                    target,
                    amount,
                    attackSequenceId,
                    attackKind,
                    hitPoint,
                    timestamp,
                    out DamageRequest request,
                    out diagnostic))
            {
                diagnostic = FormatDiagnostic(diagnostic, source, target, attackSequenceId);
                ReportDiagnostic(diagnostic, false, out diagnostic);
                return false;
            }

            return Submit(request, attackWindow, out diagnostic);
        }

        /// <summary>テストや敵攻撃から利用する、命中位置省略版の送信入口です。</summary>
        public bool Submit(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            AttackWindowTracker attackWindow,
            out string diagnostic)
        {
            Vector3 hitPoint = target != null ? target.transform.position : Vector3.zero;
            return Submit(source, target, amount, attackSequenceId, attackKind, attackWindow, hitPoint, out diagnostic);
        }

        public void Construct(GameFlowController gameFlowController, IGameplayClock clock, ICombatantRegistry registry = null)
        {
            this.gameFlowController = gameFlowController;
            this.gameplayStateProvider = gameFlowController;
            this.clock = clock;
            this.gameplayClock = clock as GameplayClock;
            if (registry != null)
            {
                this.combatantRegistry = registry;
            }
            acceptedRequests.Clear();
        }

        public void Construct(IGameplayStateProvider stateProvider, IGameplayClock gameplayTime, ICombatantRegistry registry = null)
        {
            this.gameplayStateProvider = stateProvider;
            this.gameFlowController = stateProvider as GameFlowController;
            this.clock = gameplayTime;
            this.gameplayClock = gameplayTime as GameplayClock;
            if (registry != null)
            {
                this.combatantRegistry = registry;
            }
            acceptedRequests.Clear();
        }

        /// <summary>
        /// 実行シーンのGameFlow、Clock、Registryを接続します。
        /// </summary>
        public void ConfigureForRuntime(
            GameFlowController flow,
            GameplayClock gameplayTime,
            ICombatantRegistry registry)
        {
            Construct(flow, gameplayTime, registry);
        }

        /// <summary>
        /// 差し替え可能な依存関係を設定します。EditMode テストと将来の Registry 接続で使用します。
        /// </summary>
        internal void SetDependencies(
            ICombatantRegistry registry,
            IGameplayStateProvider stateProvider,
            IGameplayClock gameplayTime)
        {
            combatantRegistry = registry;
            gameplayStateProvider = stateProvider;
            clock = gameplayTime;
            acceptedRequests.Clear();
        }

        private static HealthComponent FindHealth(CombatantMarker target)
        {
            return target != null ? target.Health : null;
        }

        private static bool IsAllowedFactionPair(CombatantMarker source, CombatantMarker target, string attackKind)
        {
            if (attackKind == AttackKinds.KnightSword)
            {
                return source.Faction == CombatantMarker.CombatantFaction.Player &&
                    target.Faction == CombatantMarker.CombatantFaction.Enemy;
            }

            if (attackKind == AttackKinds.EnemyMelee)
            {
                return source.Faction == CombatantMarker.CombatantFaction.Enemy &&
                    target.Faction == CombatantMarker.CombatantFaction.Player;
            }

            return false;
        }

        private static bool IsWithinRange(CombatantMarker source, CombatantMarker target, float attackRange)
        {
            if (source == null || target == null || float.IsNaN(attackRange) || float.IsInfinity(attackRange) || attackRange < 0f)
            {
                return false;
            }

            return (target.transform.position - source.transform.position).sqrMagnitude <= attackRange * attackRange;
        }

        private bool ReportDiagnostic(string message, bool asError, out string diagnostic)
        {
            diagnostic = message;
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[ダメージ診断] {message}", this);
            }
            else
            {
                Debug.Log($"[ダメージ診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
            return false;
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            ReportDiagnostic(message, asError, out _);
        }

        private static string FormatDiagnostic(string reason, CombatantMarker source, CombatantMarker target, int sequenceId)
        {
            string sourceName = source != null && !string.IsNullOrWhiteSpace(source.CombatantId) ? source.CombatantId : "不明";
            string targetName = target != null && !string.IsNullOrWhiteSpace(target.CombatantId) ? target.CombatantId : "不明";
            return $"{reason} 発生元「{sourceName}」、対象「{targetName}」、攻撃系列{sequenceId}。";
        }

        private readonly struct DamageKey : IEquatable<DamageKey>
        {
            public DamageKey(CombatantMarker source, CombatantMarker target, int sequenceId)
            {
                Source = source;
                Target = target;
                SequenceId = sequenceId;
            }

            public CombatantMarker Source { get; }
            public CombatantMarker Target { get; }
            public int SequenceId { get; }

            public bool Equals(DamageKey other)
            {
                return ReferenceEquals(Source, other.Source) &&
                    ReferenceEquals(Target, other.Target) &&
                    SequenceId == other.SequenceId;
            }

            public override bool Equals(object obj)
            {
                return obj is DamageKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Source);
                    hash = (hash * 397) ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Target);
                    return (hash * 397) ^ SequenceId;
                }
            }
        }
    }
}
