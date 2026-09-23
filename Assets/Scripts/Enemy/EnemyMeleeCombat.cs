using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の近接攻撃を管理します。攻撃系列、攻撃有効ウィンドウ、冷却、
    /// Player限定の命中判定をDamageServiceへ接続します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatantMarker))]
    public sealed class EnemyMeleeCombat : MonoBehaviour
    {
        public const float MinimumAttackRange = 0.01f;
        public const float MinimumDamage = 0.01f;
        public const float MinimumCooldown = 0f;
        public const float MaximumCompletionNormalizedTime = 0.99f;
        public const float DefaultAttackRange = 2.60f;
        public const float DefaultAttackDamage = 15f;
        public const float DefaultAttackCooldown = 1.25f;
        public const float DefaultWindowOpenNormalizedTime = 0.55f;
        public const float DefaultWindowCloseNormalizedTime = 0.75f;
        public const float DefaultCompletionNormalizedTime = 0.95f;

        [Header("参照")]
        [SerializeField]
        private EnemyBrain enemyBrain;

        [SerializeField]
        private CombatantMarker combatantMarker;

        [SerializeField]
        private CombatHitbox weaponHitbox;

        [SerializeField]
        private EnemyAnimationDriver animationDriver;

        [SerializeField]
        private Animator targetAnimator;

        [SerializeField]
        private HealthComponent healthComponent;

        [SerializeField]
        private DamageService damageService;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        [Header("近接攻撃設定")]
        [Tooltip("近接攻撃の数値設定アセットです。未設定時はデフォルト値を使用します。")]
        [SerializeField]
        private AttackConfig attackConfig;

        [SerializeField]
        private readonly GameplayState fallbackGameplayState = GameplayState.Running;

        private AttackWindowTracker attackWindowTracker;
        private AttackSequence attackSequence;
        private double nextAttackAllowedTime;
        private int currentAttackSequenceId;
        private bool subscribed;
        private bool completionInProgress;
        private bool attackAnimationObserved;
        private bool missingReferenceDiagnosticReported;

        public AttackConfig AttackConfig
        {
            get => attackConfig;
            set => attackConfig = value;
        }

        public float AttackRange => attackConfig != null ? attackConfig.AttackRange : DefaultAttackRange;
        public float AttackDamage => attackConfig != null ? attackConfig.AttackDamage : DefaultAttackDamage;
        public float AttackCooldown => attackConfig != null ? attackConfig.AttackCooldown : DefaultAttackCooldown;
        public float AttackWindowOpenNormalizedTime => attackConfig != null ? attackConfig.AttackWindowOpenNormalizedTime : DefaultWindowOpenNormalizedTime;
        public float AttackWindowCloseNormalizedTime => attackConfig != null ? attackConfig.AttackWindowCloseNormalizedTime : DefaultWindowCloseNormalizedTime;
        public float AttackCompletionNormalizedTime => attackConfig != null ? attackConfig.AttackCompletionNormalizedTime : DefaultCompletionNormalizedTime;

        /// <summary>現在の攻撃系列です。</summary>
        public AttackSequence CurrentAttackSequence => attackSequence;

        /// <summary>現在の攻撃ウィンドウ追跡器です。</summary>
        public AttackWindowTracker AttackWindowTracker => attackWindowTracker;

        /// <summary>現在攻撃中かを返します。</summary>
        public bool IsAttacking => attackSequence != null && attackSequence.IsActive;

        /// <summary>現在の攻撃系列IDです。攻撃中でない場合は0です。</summary>
        public int CurrentAttackSequenceId => currentAttackSequenceId;

        /// <summary>この戦闘コンポーネントが属する参戦者マーカーです。</summary>
        public CombatantMarker CombatantMarker => combatantMarker;

        public void SetCombatantMarker(CombatantMarker marker) => combatantMarker = marker;

        /// <summary>攻撃開始、完了、取消の通知です。</summary>
        public event Action<int> AttackStarted;
        public event Action<int> AttackCompleted;
        public event Action<int> AttackCancelled;

        /// <summary>DamageServiceへ送信された有効なPlayer命中の通知です。</summary>
        public event Action<CombatantMarker, int> PlayerHitSubmitted;

        [Inject]
        public void Construct(
            DamageService damage = null,
            GameFlowController flow = null,
            IGameplayClock clock = null)
        {
            if (damage != null) damageService = damage;
            if (flow != null) gameFlowController = flow;
            if (clock != null) gameplayClock = clock as GameplayClock;
        }

        private void Awake()
        {
            combatantMarker = GetComponent<CombatantMarker>();
            UnityEngine.Assertions.Assert.IsNotNull(combatantMarker, "EnemyMeleeCombat: CombatantMarkerが必要です。");
            UnityEngine.Assertions.Assert.IsTrue(combatantMarker.Faction == CombatantMarker.CombatantFaction.Enemy, "EnemyMeleeCombat: CombatantMarkerはEnemy陣営である必要があります。");
            ClampConfiguration();
            InitializeAttackSequence();
        }

        private void OnEnable()
        {
            InitializeAttackSequence();
            SubscribeToDependencies();
        }

        private void OnDisable()
        {
            CancelAttack();
            UnsubscribeFromDependencies();
        }

        private void Update()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (CurrentGameplayState != GameplayState.Running ||
                (healthComponent != null && !healthComponent.IsAlive))
            {
                CancelAttack();
                return;
            }

            TickAttackAnimation();
        }

        private void OnValidate()
        {
            ClampConfiguration();
        }

        /// <summary>
        /// EnemyBrainのAttackRequestedから呼び出される攻撃開始処理です。
        /// </summary>
        public Result BeginAttack(int sequenceId)
        {
            if (!EnsureReferencesReady())
            {
                return GameError.InvalidState;
            }

            if (CurrentGameplayState != GameplayState.Running)
            {
                return GameError.StateAlreadyTerminal;
            }

            if (healthComponent != null && !healthComponent.IsAlive)
            {
                return GameError.TargetDead;
            }

            if (IsAttacking)
            {
                return GameError.ActionInProgress;
            }

            if (CurrentGameTime < nextAttackAllowedTime)
            {
                return GameError.ActionCooldownActive;
            }

            if (!DamageRequest.IsValidAttackSequenceId(sequenceId))
            {
                return GameError.InvalidParameter;
            }

            Result startResult = attackSequence.StartSequence(sequenceId);
            if (startResult.IsErr)
            {
                return startResult;
            }

            currentAttackSequenceId = sequenceId;
            attackAnimationObserved = false;
            nextAttackAllowedTime = CurrentGameTime + AttackCooldown;
            float effectiveClose = Mathf.Clamp(AttackWindowCloseNormalizedTime, 0.1f, MaximumCompletionNormalizedTime);
            float effectiveOpen = Mathf.Clamp(AttackWindowOpenNormalizedTime, 0.01f, effectiveClose - 0.05f);
            attackSequence.ConfigureTiming(effectiveClose, effectiveOpen);
            attackWindowTracker.AttackRange = AttackRange;
            weaponHitbox?.SetWindowTracker(attackWindowTracker);
            weaponHitbox?.ResetForNewSequence();
            AttackStarted?.Invoke(sequenceId);
                return Result.Ok();
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃ウィンドウを開きます。</summary>
        public Result AnimationEventBeginAttackWindow()
        {
            if (!IsAttacking || CurrentGameplayState != GameplayState.Running)
            {
                return GameError.InvalidState;
            }

            return attackSequence.OnAttackWindowOpenEvent();
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃ウィンドウを閉じます。</summary>
        public Result AnimationEventEndAttackWindow()
        {
            if (attackSequence == null)
            {
                return GameError.InvalidState;
            }

            return attackSequence.OnAttackWindowCloseEvent();
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃系列を完了します。</summary>
        public Result AnimationEventCompleteAttack()
        {
            return CompleteAttack(currentAttackSequenceId, true);
        }

        /// <summary>終局、死亡、無効化時に攻撃と攻撃ウィンドウを閉じます。</summary>
        public void CancelAttack()
        {
            if (attackSequence == null || !attackSequence.IsActive)
            {
                currentAttackSequenceId = 0;
                return;
            }

            int sequenceId = currentAttackSequenceId;
            attackSequence.Cancel();
            attackAnimationObserved = false;
            currentAttackSequenceId = 0;
            AttackCancelled?.Invoke(sequenceId);
            enemyBrain?.NotifyAttackCancelled(sequenceId);
        }

        /// <summary>依存関係を明示的に差し替えます。</summary>
        internal void SetDependencies(
            EnemyBrain brain,
            CombatantMarker enemy,
            CombatHitbox hitbox,
            DamageService damage,
            HealthComponent health,
            GameFlowController flow,
            EnemyAnimationDriver driver = null,
            Animator animator = null,
            GameplayClock clock = null)
        {
            UnsubscribeFromDependencies();
            enemyBrain = brain;
            combatantMarker = enemy;
            weaponHitbox = hitbox;
            damageService = damage;
            healthComponent = health;
            gameFlowController = flow;
            animationDriver = driver;
            targetAnimator = animator;
            gameplayClock = clock;
            InitializeAttackSequence();
            SubscribeToDependencies();
        }

        private void HandleBrainAttackRequested(int sequenceId)
        {
            Result result = BeginAttack(sequenceId);
            if (result.IsErr && enemyBrain != null)
            {
                enemyBrain.NotifyAttackCancelled(sequenceId);
            }
        }

        private void HandleBrainAttackCompleted(int sequenceId)
        {
            CompleteAttack(sequenceId, false);
        }

        private void HandleBrainAttackCancelled(int sequenceId)
        {
            if (currentAttackSequenceId == sequenceId)
            {
                CancelAttackWithoutNotifyingBrain();
            }
        }

        private Result CompleteAttack(int sequenceId, bool notifyBrain)
        {
            if (completionInProgress || attackSequence == null || !attackSequence.IsActive || sequenceId == 0 || sequenceId != currentAttackSequenceId)
            {
                return GameError.InvalidState;
            }

            completionInProgress = true;
            try
            {
                Result compResult = attackSequence.Complete();
                if (compResult.IsErr)
                {
                    return compResult;
                }

                attackAnimationObserved = false;
                currentAttackSequenceId = 0;
                AttackCompleted?.Invoke(sequenceId);
                if (notifyBrain && enemyBrain != null)
                {
                    enemyBrain.NotifyAttackCompleted(sequenceId);
                }

                    return Result.Ok();
            }
            finally
            {
                completionInProgress = false;
            }
        }

        private void HandleTargetRegistered(CombatantMarker target, int sequenceId)
        {
            if (target == null || target.Faction != CombatantMarker.CombatantFaction.Player)
            {
                return;
            }

            if (CurrentGameplayState != GameplayState.Running || combatantMarker == null || damageService == null)
            {
                return;
            }

            Vector3 hitPoint = target.transform.position;
            Result submitResult = damageService.Submit(
                combatantMarker,
                target,
                AttackDamage,
                sequenceId,
                AttackKinds.EnemyMelee,
                attackWindowTracker,
                hitPoint);

            if (submitResult.IsOk)
            {
                PlayerHitSubmitted?.Invoke(target, sequenceId);
            }
        }

        private void TickAttackAnimation()
        {
            if (!IsAttacking)
            {
                return;
            }

            if (animationDriver != null)
            {
                Result<float> animResult = animationDriver.GetAttackNormalizedTime();
                if (animResult.IsOk)
                {
                    float normalizedTime = animResult.Value;
                    attackAnimationObserved = true;
                    attackSequence.Tick(normalizedTime);
                    if (normalizedTime >= AttackCompletionNormalizedTime)
                    {
                        CompleteAttack(currentAttackSequenceId, true);
                    }

                    return;
                }
            }

            if (attackAnimationObserved)
            {
                CompleteAttack(currentAttackSequenceId, true);
            }
        }

        private bool EnsureReferencesReady()
        {
            InitializeAttackSequence();
            UnityEngine.Assertions.Assert.IsNotNull(combatantMarker, "EnemyMeleeCombat: CombatantMarker参照がありません。");
            UnityEngine.Assertions.Assert.IsNotNull(attackSequence, "EnemyMeleeCombat: 攻撃系列を初期化できません。");
            UnityEngine.Assertions.Assert.IsNotNull(attackWindowTracker, "EnemyMeleeCombat: AttackWindowTrackerを初期化できません。");
            UnityEngine.Assertions.Assert.IsNotNull(damageService, "EnemyMeleeCombat: DamageService参照がありません。");
            UnityEngine.Assertions.Assert.IsNotNull(weaponHitbox, "EnemyMeleeCombat: EnemyHitbox参照がありません。");
            return true;
        }



        private void InitializeAttackSequence()
        {
            if (attackSequence != null || combatantMarker == null)
            {
                return;
            }

            float effectiveRange = Mathf.Max(MinimumAttackRange, AttackRange);
            float effectiveClose = Mathf.Clamp(AttackWindowCloseNormalizedTime, 0.1f, MaximumCompletionNormalizedTime);
            float effectiveOpen = Mathf.Clamp(AttackWindowOpenNormalizedTime, 0.01f, effectiveClose - 0.05f);
            attackWindowTracker = new AttackWindowTracker(combatantMarker, effectiveRange);
            attackSequence = new AttackSequence(attackWindowTracker, effectiveClose);
            attackSequence.ConfigureTiming(effectiveClose, effectiveOpen);
            attackWindowTracker.TargetRegistered += HandleTargetRegistered;
            weaponHitbox?.SetWindowTracker(attackWindowTracker);
        }

        private void SubscribeToDependencies()
        {
            if (subscribed)
            {
                return;
            }

            if (enemyBrain != null)
            {
                enemyBrain.AttackRequested += HandleBrainAttackRequested;
                enemyBrain.AttackCompleted += HandleBrainAttackCompleted;
                enemyBrain.AttackCancelled += HandleBrainAttackCancelled;
            }

            subscribed = true;
        }

        private void UnsubscribeFromDependencies()
        {
            if (!subscribed)
            {
                return;
            }

            if (enemyBrain != null)
            {
                enemyBrain.AttackRequested -= HandleBrainAttackRequested;
                enemyBrain.AttackCompleted -= HandleBrainAttackCompleted;
                enemyBrain.AttackCancelled -= HandleBrainAttackCancelled;
            }

            subscribed = false;
        }

        private void CancelAttackWithoutNotifyingBrain()
        {
            if (attackSequence == null || !attackSequence.IsActive)
            {
                currentAttackSequenceId = 0;
                return;
            }

            int sequenceId = currentAttackSequenceId;
            attackSequence.Cancel();
            currentAttackSequenceId = 0;
            AttackCancelled?.Invoke(sequenceId);
        }

        private GameplayState CurrentGameplayState => gameFlowController != null
            ? gameFlowController.CurrentState
            : fallbackGameplayState;

        private double CurrentGameTime => gameplayClock != null
            ? gameplayClock.Now
            : Time.timeAsDouble;

        private void ClampConfiguration()
        {
        }

        private static string GetCombatantName(CombatantMarker target)
        {
            return target != null && !string.IsNullOrWhiteSpace(target.CombatantId)
                ? target.CombatantId
                : "不明";
        }

        private string GetCombatantName()
        {
            return GetCombatantName(combatantMarker);
        }
    }
}
