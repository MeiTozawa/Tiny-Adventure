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
        [Header("参照")]
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
        private GameFlowController gameFlowController;
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
        private bool completionInProgress;
        private bool attackAnimationObserved;

        public AttackConfig AttackConfig
        {
            get => attackConfig;
            set => attackConfig = value;
        }

        public float AttackRange => attackConfig != null ? attackConfig.AttackRange : 0f;
        public float AttackDamage => attackConfig != null ? attackConfig.AttackDamage : 0f;
        public float AttackCooldown => attackConfig != null ? attackConfig.AttackCooldown : 0f;
        public float AttackWindowOpenNormalizedTime => attackConfig != null ? attackConfig.AttackWindowOpenNormalizedTime : 0f;
        public float AttackWindowCloseNormalizedTime => attackConfig != null ? attackConfig.AttackWindowCloseNormalizedTime : 0f;
        public float AttackCompletionNormalizedTime => attackConfig != null ? attackConfig.AttackCompletionNormalizedTime : 0f;

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
            healthComponent ??= GetComponent<HealthComponent>();
            animationDriver ??= GetComponent<EnemyAnimationDriver>();
            targetAnimator ??= GetComponentInChildren<Animator>();
            weaponHitbox ??= GetComponentInChildren<CombatHitbox>();

            UnityEngine.Assertions.Assert.IsNotNull(combatantMarker, "EnemyMeleeCombat: CombatantMarkerが必要です。");
            UnityEngine.Assertions.Assert.IsTrue(combatantMarker.Faction == CombatantMarker.CombatantFaction.Enemy, "EnemyMeleeCombat: CombatantMarkerはEnemy陣営である必要があります。");
            SetupAttackSequence();
        }

        private void OnEnable()
        {
        }

        private void OnDisable()
        {
            CancelAttack();
        }

        private void Start()
        {
            SetupAttackSequence();
            damageService ??= FindAnyObjectByType<DamageService>();
            gameFlowController ??= FindAnyObjectByType<GameFlowController>();
            gameplayClock ??= FindAnyObjectByType<GameplayClock>();
            UnityEngine.Assertions.Assert.IsNotNull(combatantMarker, "EnemyMeleeCombat: CombatantMarker参照がありません。");
            UnityEngine.Assertions.Assert.IsNotNull(attackSequence, "EnemyMeleeCombat: 攻撃系列を初期化できません。");
            UnityEngine.Assertions.Assert.IsNotNull(attackWindowTracker, "EnemyMeleeCombat: AttackWindowTrackerを初期化できません。");
            UnityEngine.Assertions.Assert.IsNotNull(damageService, "EnemyMeleeCombat: DamageService参照がありません。");
            UnityEngine.Assertions.Assert.IsNotNull(weaponHitbox, "EnemyMeleeCombat: EnemyHitbox参照がありません。");
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



        /// <summary>
        /// EnemyBrain 等の上位制御から直接実行される近接攻撃コマンドです。
        /// </summary>
        public Result ExecuteAttack(int sequenceId)
        {
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

            if (sequenceId <= 0)
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
            float effectiveClose = AttackWindowCloseNormalizedTime;
            float effectiveOpen = AttackWindowOpenNormalizedTime;
            attackSequence.ConfigureTiming(effectiveClose, effectiveOpen);
            attackWindowTracker.AttackRange = AttackRange;
            weaponHitbox.SetWindowTracker(attackWindowTracker);
            weaponHitbox.ResetForNewSequence();
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

            return attackSequence.OnAttackWindowOpenEvent()
                .LogIfErr(this, "[EnemyMeleeCombat] 攻撃ウィンドウ開始拒絶");
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃ウィンドウを閉じます。</summary>
        public Result AnimationEventEndAttackWindow()
        {
            if (attackSequence == null)
            {
                return GameError.InvalidState;
            }

            return attackSequence.OnAttackWindowCloseEvent()
                .LogIfErr(this, "[EnemyMeleeCombat] 攻撃ウィンドウ終了拒絶");
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃系列を完了します。</summary>
        public Result AnimationEventCompleteAttack()
        {
            return CompleteAttack(currentAttackSequenceId)
                .LogIfErr(this, "[EnemyMeleeCombat] 攻撃完了処理拒絶");
        }

        // --- Unity Animator Event 専用の void ブリッジ ---
        public void OnAnimationEvent_BeginAttackWindow() => AnimationEventBeginAttackWindow();
        public void OnAnimationEvent_EndAttackWindow() => AnimationEventEndAttackWindow();
        public void OnAnimationEvent_CompleteAttack() => AnimationEventCompleteAttack();

        /// <summary>終局、死亡、無効化時に攻撃と攻撃ウィンドウを閉じます。</summary>
        public void CancelAttack()
        {
            if (!IsAttacking)
            {
                currentAttackSequenceId = 0;
                return;
            }

            int sequenceId = currentAttackSequenceId;
            attackSequence.Cancel();
            attackAnimationObserved = false;
            currentAttackSequenceId = 0;
            AttackCancelled?.Invoke(sequenceId);
        }


        public Result CompleteAttack(int sequenceId = 0)
        {
            int targetSequenceId = sequenceId > 0 ? sequenceId : currentAttackSequenceId;
            if (completionInProgress || !IsAttacking || targetSequenceId == 0 || targetSequenceId != currentAttackSequenceId)
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
                int completedId = currentAttackSequenceId;
                currentAttackSequenceId = 0;
                AttackCompleted?.Invoke(completedId);
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
                        CompleteAttack(currentAttackSequenceId);
                    }

                    return;
                }
            }

            if (targetAnimator != null)
            {
                AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
                bool isAttackState = stateInfo.IsName("Attack");
                if (isAttackState)
                {
                    attackAnimationObserved = true;
                    float normalizedTime = stateInfo.normalizedTime;
                    attackSequence.Tick(normalizedTime);
                    if (normalizedTime >= AttackCompletionNormalizedTime)
                    {
                        CompleteAttack(currentAttackSequenceId);
                    }

                    return;
                }
            }

            if (attackAnimationObserved)
            {
                CompleteAttack(currentAttackSequenceId);
            }
        }





        private void SetupAttackSequence()
        {
            if (attackSequence != null || combatantMarker == null)
            {
                return;
            }

            float effectiveRange = AttackRange;
            float effectiveClose = AttackWindowCloseNormalizedTime;
            float effectiveOpen = AttackWindowOpenNormalizedTime;
            attackWindowTracker = new AttackWindowTracker(combatantMarker, effectiveRange);
            attackSequence = new AttackSequence(attackWindowTracker, effectiveClose);
            attackSequence.ConfigureTiming(effectiveClose, effectiveOpen);
            attackWindowTracker.TargetRegistered += HandleTargetRegistered;
            if (weaponHitbox != null)
            {
                weaponHitbox.SetWindowTracker(attackWindowTracker);
            }
        }

        private GameplayState CurrentGameplayState => gameFlowController != null
            ? gameFlowController.CurrentState
            : fallbackGameplayState;

        private double CurrentGameTime => gameplayClock != null
            ? gameplayClock.Now
            : Time.timeAsDouble;
    }
}
