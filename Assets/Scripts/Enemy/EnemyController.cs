using System;
using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 敵キャラクターの総合コントローラー。
    /// 索敵、追跡、近接攻撃判定、受撃・ノックバック、死亡演出のライフサイクルを一元管理します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CombatantMarker))]
    [RequireComponent(typeof(HealthComponent))]
    [RequireComponent(typeof(EnemyAnimationDriver))]
    public sealed class EnemyController : MonoBehaviour, IKnockbackReceiver, IAttackHitListener
    {
        public enum EnemyState
        {
            Idle,
            Chase,
            Attack,
            DeathTransition,
            Removed
        }

        [Header("コンポーネント参照")]
        [SerializeField] private NavMeshAgent navMeshAgent;
        [SerializeField] private CombatantMarker combatantMarker;
        [SerializeField] private HealthComponent healthComponent;
        [SerializeField] private EnemyAnimationDriver animationDriver;
        [SerializeField] private CombatHitbox weaponHitbox;
        [SerializeField] private Animator targetAnimator;

        private IDamageService damageService;
        private IGameplayStateProvider gameFlowController;
        private IGameplayClock gameplayClock;
        private ICombatantRegistry sceneReferenceRegistry;

        [Header("戦闘・AI設定")]
        [Tooltip("攻撃動作の数値設定アセットです。未設定時はデフォルト値を使用します。")]
        [SerializeField] private AttackConfig attackConfig;

        [Tooltip("追跡を停止して攻撃可能とみなす距離")]
        [SerializeField, Min(0f)] private float meleeRange = 2.35f;

        [Tooltip("NavMeshAgentの停止距離")]
        [SerializeField, Min(0f)] private float configuredStoppingDistance = 2.1f;

        [Tooltip("敵がターゲットを向く旋回速度 (deg/sec)")]
        [SerializeField, Min(0f)] private float turnSpeed = 540f;

        [Tooltip("攻撃終了後のクールダウン時間 (秒)")]
        [SerializeField, Min(0f)] private float attackCooldown = 1.25f;

        [Tooltip("死亡アニメーション安全フォールバック待機時間 (秒)")]
        [SerializeField, Min(0f)] private float deathFallbackDuration = 1.5f;

        [Tooltip("死亡完了後にGameObjectを非アクティブ化するか")]
        [SerializeField] private bool disableAfterRemoval = true;

        [Header("単体テスト・フォールバック用")]
        [SerializeField] private GameplayState fallbackGameplayState = GameplayState.Running;

        // 実行時状態
        private EnemyState state = EnemyState.Idle;
        [SerializeField] private CombatantMarker playerTarget;
        private Transform playerTransform;
        private float meleeRangeSqr;
        private AttackWindowTracker attackWindowTracker;
        private AttackSequence attackSequence;
        private int currentAttackSequenceId;
        private double nextAttackAllowedTime;
        private double deathStartedTime;
        private bool attackAnimationObserved;
        private Vector3 knockbackVelocity;

        public EnemyState State => state;
        public bool IsAlive => state != EnemyState.DeathTransition && state != EnemyState.Removed;
        public NavMeshAgent Agent => navMeshAgent;

        public event Action<EnemyState> StateChanged;

        [Inject]
        public void Construct(
            IGameplayStateProvider flow = null,
            IGameplayClock clock = null,
            IDamageService damage = null,
            ICombatantRegistry registry = null)
        {
            if (flow != null) gameFlowController = flow;
            if (clock != null) gameplayClock = clock;
            if (damage != null) damageService = damage;
            if (registry != null)
            {
                sceneReferenceRegistry = registry;
                if (playerTarget == null && registry.Player != null)
                {
                    SetPlayerTarget(registry.Player);
                }
            }
        }

        public void SetPlayerTarget(CombatantMarker target)
        {
            playerTarget = target;
            playerTransform = target.transform;
        }

        private void Awake()
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
            combatantMarker = GetComponent<CombatantMarker>();
            healthComponent = GetComponent<HealthComponent>();
            animationDriver = GetComponent<EnemyAnimationDriver>();

            meleeRangeSqr = meleeRange * meleeRange;
            navMeshAgent.stoppingDistance = configuredStoppingDistance;
            navMeshAgent.updateRotation = false;
            SetupAttackSequence();
        }

        private void Start()
        {
            if (playerTarget == null && sceneReferenceRegistry != null)
            {
                SetPlayerTarget(sceneReferenceRegistry.Player);
            }
            SubscribeEvents();
            SetState(EnemyState.Chase);
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            CancelAttack();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            UnregisterFromRegistries();
        }

        private void Update()
        {
            if (state == EnemyState.Removed)
            {
                return;
            }

            if (state == EnemyState.DeathTransition)
            {
                UpdateDeathTransition();
                return;
            }

            if (CurrentGameplayState != GameplayState.Running || !healthComponent.IsAlive)
            {
                if (navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                {
                    navMeshAgent.isStopped = true;
                }
                return;
            }

            // ノックバック処理
            if (knockbackVelocity.sqrMagnitude > 0.01f)
            {
                ApplyKnockbackMotion();
            }

            Vector3 playerPosition = playerTransform.position;
            Vector3 toTarget = playerPosition - transform.position;
            toTarget.y = 0f;
            float sqrDistance = toTarget.sqrMagnitude;

            switch (state)
            {
                case EnemyState.Idle:
                case EnemyState.Chase:
                    if (sqrDistance <= meleeRangeSqr)
                    {
                        RotateTowards(playerPosition);
                        if (navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                        {
                            navMeshAgent.isStopped = true;
                        }

                        if (CurrentGameTime >= nextAttackAllowedTime)
                        {
                            ExecuteAttack();
                        }
                    }
                    else
                    {
                        SetState(EnemyState.Chase);
                        if (navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                        {
                            navMeshAgent.isStopped = false;
                            navMeshAgent.SetDestination(playerPosition);
                        }

                        Vector3 moveDir = navMeshAgent.hasPath ? navMeshAgent.desiredVelocity : Vector3.zero;
                        moveDir.y = 0f;
                        if (moveDir.sqrMagnitude > 0.05f)
                        {
                            RotateTowards(transform.position + moveDir);
                        }
                        else
                        {
                            RotateTowards(playerPosition);
                        }
                    }
                    break;

                case EnemyState.Attack:
                    RotateTowards(playerPosition);
                    TickAttackAnimation();
                    break;
            }
        }

        #region Attack Logic

        private void SetupAttackSequence()
        {
            float range = attackConfig.AttackRange;
            float openTime = attackConfig.AttackWindowOpenNormalizedTime;
            float closeTime = attackConfig.AttackWindowCloseNormalizedTime;

            attackWindowTracker = new AttackWindowTracker(combatantMarker, range, this);
            attackSequence = new AttackSequence(attackWindowTracker, closeTime);
            attackSequence.ConfigureTiming(closeTime, openTime);

            weaponHitbox.SetWindowTracker(attackWindowTracker);
        }

        public void ExecuteAttack()
        {
            if (state == EnemyState.Attack || !IsAlive || CurrentGameplayState != GameplayState.Running)
            {
                return;
            }

            SetState(EnemyState.Attack);
            currentAttackSequenceId++;
            if (currentAttackSequenceId <= 0) currentAttackSequenceId = 1;

            attackAnimationObserved = false;
            nextAttackAllowedTime = CurrentGameTime + attackCooldown;

            float range = attackConfig.AttackRange;
            float openTime = attackConfig.AttackWindowOpenNormalizedTime;
            float closeTime = attackConfig.AttackWindowCloseNormalizedTime;

            attackSequence.StartSequence(currentAttackSequenceId);
            attackSequence.ConfigureTiming(closeTime, openTime);
            attackWindowTracker.AttackRange = range;
            weaponHitbox.ResetForNewSequence();

            animationDriver.TriggerAttack();
        }

        void IAttackHitListener.OnTargetHit(CombatantMarker target, int sequenceId)
        {
            if (target.Faction != CombatantMarker.CombatantFaction.Player)
            {
                return;
            }

            if (CurrentGameplayState != GameplayState.Running)
            {
                return;
            }

            float damage = attackConfig.AttackDamage;
            damageService.Submit(
                combatantMarker,
                target,
                damage,
                sequenceId,
                AttackKinds.EnemyMelee,
                attackWindowTracker,
                target.transform.position);
        }

        private void TickAttackAnimation()
        {
            if (targetAnimator != null)
            {
                AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName("Attack"))
                {
                    attackAnimationObserved = true;
                    attackSequence?.Tick(stateInfo.normalizedTime);

                    float completionTime = attackConfig != null ? attackConfig.AttackCompletionNormalizedTime : 0.95f;
                    if (stateInfo.normalizedTime >= completionTime)
                    {
                        CompleteAttack();
                    }
                    return;
                }
            }

            if (attackAnimationObserved)
            {
                CompleteAttack();
            }
        }

        public void CompleteAttack()
        {
            if (state != EnemyState.Attack) return;

            attackSequence?.Complete();
            int completedId = currentAttackSequenceId;
            SetState(EnemyState.Chase);
        }

        public void CancelAttack()
        {
            if (state == EnemyState.Attack)
            {
                attackSequence?.Cancel();
                SetState(EnemyState.Idle);
            }
        }

        // Animator Event ブリッジ
        public void OnAnimationEvent_BeginAttackWindow() => attackSequence?.OnAttackWindowOpenEvent();
        public void OnAnimationEvent_EndAttackWindow() => attackSequence?.OnAttackWindowCloseEvent();
        public void OnAnimationEvent_CompleteAttack() => CompleteAttack();

        #endregion

        #region Knockback & Hit Reactions

        public void ApplyKnockback(Vector3 direction, float force)
        {
            if (!IsAlive) return;

            Vector3 dir = direction.normalized;
            dir.y = 0f;
            knockbackVelocity = dir * force;
            CancelAttack();
        }

        private void ApplyKnockbackMotion()
        {
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.Move(knockbackVelocity * Time.deltaTime);
            }
            else
            {
                transform.position += knockbackVelocity * Time.deltaTime;
            }

            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, Time.deltaTime * 10f);
        }

        private float lastKnownHealth;

        private void HandleHealthChanged(float current, float max)
        {
            if (!IsAlive) return;
            if (current < lastKnownHealth)
            {
                animationDriver.TriggerHit();
            }
            lastKnownHealth = current;
        }

        #endregion

        #region Death & Lifecycle

        private void HandleDied()
        {
            if (state == EnemyState.DeathTransition || state == EnemyState.Removed)
            {
                return;
            }

            CancelAttack();
            SetState(EnemyState.DeathTransition);
            deathStartedTime = CurrentGameTime;

            if (navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.enabled = false;
            }

            // 当たり判定と武器判定を無効化
            weaponHitbox.gameObject.SetActive(false);
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            animationDriver.TriggerDeath();
            healthComponent.CompleteDeath();
            UnregisterFromRegistries();
        }

        private void UpdateDeathTransition()
        {
            bool completed = false;
            if (targetAnimator != null)
            {
                AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName("Death") && stateInfo.normalizedTime >= 0.95f)
                {
                    completed = true;
                }
            }

            if (!completed && (CurrentGameTime - deathStartedTime >= deathFallbackDuration))
            {
                completed = true;
            }

            if (completed)
            {
                SetState(EnemyState.Removed);
                if (disableAfterRemoval)
                {
                    gameObject.SetActive(false);
                }
            }
        }

        public void OnAnimationEvent_CompleteDeath()
        {
            SetState(EnemyState.Removed);
            if (disableAfterRemoval)
            {
                gameObject.SetActive(false);
            }
        }

        private void UnregisterFromRegistries()
        {
            if (combatantMarker != null)
            {
                damageService?.UnregisterCombatant(combatantMarker);
                sceneReferenceRegistry?.Unregister(combatantMarker);
            }
        }

        #endregion

        #region Helper Methods

        private void RotateTowards(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }
        }

        private void SetState(EnemyState nextState)
        {
            if (state == nextState) return;
            state = nextState;
            StateChanged?.Invoke(state);
        }

        private void SubscribeEvents()
        {
            lastKnownHealth = healthComponent.CurrentHealth;
            healthComponent.Died -= HandleDied;
            healthComponent.Died += HandleDied;
            healthComponent.HealthChanged -= HandleHealthChanged;
            healthComponent.HealthChanged += HandleHealthChanged;
        }

        private void UnsubscribeEvents()
        {
            healthComponent.Died -= HandleDied;
            healthComponent.HealthChanged -= HandleHealthChanged;
        }

        private GameplayState CurrentGameplayState => gameFlowController != null
            ? gameFlowController.CurrentState
            : fallbackGameplayState;

        private double CurrentGameTime => gameplayClock != null
            ? gameplayClock.Now
            : Time.timeAsDouble;

        #endregion
    }
}
