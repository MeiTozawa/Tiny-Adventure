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
        private readonly HashSet<DamageKey> acceptedRequests = new HashSet<DamageKey>();

        /// <summary>HealthComponent.Receive が成功した正式なダメージ通知です。</summary>
        public event Action<DamageRequest> DamageAccepted;

        /// <summary>受撃側の視覚・アニメーション反応を開始する通知です。</summary>
        public event Action<CombatantMarker, DamageRequest> HitFeedbackRequested;

        public ICombatantRegistry CombatantRegistry => combatantRegistry;
        public IGameplayStateProvider GameplayStateProvider => gameplayStateProvider;
        public IGameplayClock Clock => clock;

        private void Awake()
        {
            if (combatantRegistry == null && registryComponent is ICombatantRegistry configuredRegistry)
            {
                combatantRegistry = configuredRegistry;
            }

            if (combatantRegistry == null)
            {
                combatantRegistry = GetComponent<ICombatantRegistry>();
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
        /// </summary>
        public void ConfigureRegistry(ICombatantRegistry registry)
        {
            combatantRegistry = registry;
            acceptedRequests.Clear();
        }

        /// <summary>戦闘対象を DamageService の登録簿へ登録します。</summary>
        public void RegisterCombatant(CombatantMarker combatant)
        {
            UnityEngine.Assertions.Assert.IsNotNull(combatant, "DamageService: 登録する戦闘対象が未設定です。");
            if (combatantRegistry != null)
            {
                combatantRegistry.Register(combatant);
            }
        }

        /// <summary>戦闘対象を DamageService の登録簿から解除します。</summary>
        public void UnregisterCombatant(CombatantMarker combatant)
        {
            UnityEngine.Assertions.Assert.IsNotNull(combatant, "DamageService: 解除する戦闘対象が未設定です。");

            acceptedRequests.RemoveWhere(key => ReferenceEquals(key.Source, combatant) || ReferenceEquals(key.Target, combatant));
            if (combatantRegistry != null)
            {
                combatantRegistry.Unregister(combatant);
            }
        }

        /// <summary>
        /// 攻撃ウィンドウ、登録、陣営、範囲、系列重複を含むダメージ要求を検証します。
        /// このメソッドは状態を変更せず、Submit と同じ検証順を使います。
        /// </summary>
        /// <summary>
        /// 攻撃ウィンドウ、登録、陣営、範囲、系列重複を含むダメージ要求を検証します。
        /// このメソッドは状態を変更せず、Submit と同じ検証順を使います。
        /// </summary>
        public Result Validate(DamageRequest request, AttackWindowTracker attackWindow)
        {
            UnityEngine.Assertions.Assert.IsNotNull(gameplayStateProvider, "DamageService: GameplayStateProvider参照が未設定です。");

            if (gameplayStateProvider.CurrentState != GameplayState.Running)
            {
                return GameError.InvalidState;
            }

            if (request.Source == null || !request.Source.IsIdentityValid || !request.Source.IsAvailableForCombat)
            {
                return GameError.InvalidParameter;
            }

            if (request.Target == null || !request.Target.IsIdentityValid || request.Source == request.Target)
            {
                return GameError.InvalidParameter;
            }

            if (request.Amount <= 0f || float.IsNaN(request.Amount) || float.IsInfinity(request.Amount))
            {
                return GameError.InvalidParameter;
            }

            if (request.AttackSequenceId <= 0)
            {
                return GameError.InvalidParameter;
            }

            if (!request.IsStructurallyValid)
            {
                return GameError.InvalidParameter;
            }

            if (!request.HasRegisteredParticipants(CombatantRegistry))
            {
                return GameError.CombatantNotRegistered;
            }

            if (!IsAllowedFactionPair(request.Source, request.Target, request.AttackKind))
            {
                return GameError.InvalidFactionPair;
            }

            if (!request.Target.IsAvailableForCombat)
            {
                return GameError.TargetUnavailable;
            }

            HealthComponent targetHealth = FindHealth(request.Target);
            UnityEngine.Assertions.Assert.IsNotNull(targetHealth, "DamageService: 対象にHealthComponentが存在しません。");

            if (!targetHealth.IsAlive)
            {
                return GameError.TargetDead;
            }

            if (attackWindow == null || !attackWindow.IsWindowOpen ||
                attackWindow.Attacker != request.Source ||
                attackWindow.OpenSequenceId != request.AttackSequenceId)
            {
                return GameError.AttackWindowClosed;
            }

            if (!IsWithinRange(request.Source, request.Target, attackWindow.AttackRange))
            {
                return GameError.OutOfRange;
            }

            DamageKey key = new DamageKey(request.Source, request.Target, request.AttackSequenceId);
            if (acceptedRequests.Contains(key))
            {
                return GameError.DuplicateHitInSequence;
            }

            return Result.Ok();
        }

        /// <summary>
        /// 検証済みの DamageRequest を HealthComponent へ一度だけ渡します。
        /// DamageService 以外から HealthComponent.Receive を直接呼び出してはいけません。
        /// </summary>
        public Result Submit(DamageRequest request, AttackWindowTracker attackWindow)
        {
            Result validateResult = Validate(request, attackWindow);
            if (validateResult.IsErr)
            {
                return validateResult;
            }

            HealthComponent targetHealth = FindHealth(request.Target);
            Result receiveResult = targetHealth.Receive(request);
            if (receiveResult.IsErr)
            {
                return receiveResult;
            }

            acceptedRequests.Add(new DamageKey(
                request.Source,
                request.Target,
                request.AttackSequenceId));
            DamageAccepted?.Invoke(request);
            HitFeedbackRequested?.Invoke(request.Target, request);
            return Result.Ok();
        }

        /// <summary>
        /// 攻撃候補から現在のゲーム時刻で DamageRequest を作り、正式に送信します。
        /// </summary>
        public Result Submit(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            AttackWindowTracker attackWindow,
            Vector3 hitPoint)
        {
            double timestamp = clock != null ? clock.Now : Time.timeAsDouble;
            Result<DamageRequest> requestResult = DamageRequest.Create(
                CombatantRegistry,
                source,
                target,
                amount,
                attackSequenceId,
                attackKind,
                hitPoint,
                timestamp);

            if (requestResult.IsErr)
            {
                return requestResult.Error;
            }

            return Submit(requestResult.Value, attackWindow);
        }

        /// <summary>テストや敵攻撃から利用する、命中位置省略版の送信入口です。</summary>
        public Result Submit(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            AttackWindowTracker attackWindow)
        {
            Vector3 hitPoint = target != null ? target.transform.position : Vector3.zero;
            return Submit(source, target, amount, attackSequenceId, attackKind, attackWindow, hitPoint);
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
