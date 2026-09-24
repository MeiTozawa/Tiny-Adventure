using System;
using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の追跡、近接距離への遷移、攻撃評価、死亡遷移を管理する状態マシンです。
    /// 移動・経路計算・旋回制御の物理操作は EnemyMotor へ委譲し、
    /// ダメージ処理は EnemyMeleeCombat および DamageService へ委譲します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyMotor))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CombatantMarker))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class EnemyBrain : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField]
        private EnemyMotor enemyMotor;

        [SerializeField]
        private EnemyMeleeCombat enemyMeleeCombat;

        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private CombatantMarker combatantMarker;

        [Tooltip("敵が追跡するKnightです。未設定時は起動時にPlayer陣営から一体だけ解決します。")]
        private CombatantMarker playerTarget;

        [SerializeField]
        private HealthComponent healthComponent;

        [SerializeField]
        private EnemyAnimationDriver animationDriver;

        private GameFlowController gameFlowController;
        private GameplayClock gameplayClock;

        [Header("追跡設定")]
        [Tooltip("この距離以内では追跡を停止してKnightの方向を向きます。")]
        [SerializeField, Min(0f)]
        private float meleeRange;

        [Tooltip("NavMeshAgentの停止距離です。")]
        [SerializeField, Min(0f)]
        private float configuredStoppingDistance;

        [Tooltip("敵が向きを変える最大角速度です。")]
        [SerializeField, Min(0f)]
        private float turnSpeed;

        [Tooltip("NavMeshを評価する固定ゲーム時間の間隔です。")]
        [SerializeField, Min(0.001f)]
        private float aiTickInterval;

        [Tooltip("NavMesh経路を再計算する最小のゲーム時間間隔です。固定AI tickより短くはなりません。")]
        [SerializeField, Min(0.001f)]
        private float pathQueryInterval;

        [Header("攻撃評価")]
        [Tooltip("攻撃評価の間隔です。実際の攻撃とダメージはEnemyMeleeCombatが担当します。")]
        [SerializeField, Min(0f)]
        private float attackCooldown;

        [Tooltip("攻撃開始後、完了通知がない場合にAttack状態を終了する時間です。")]
        [SerializeField, Min(0f)]
        private float attackStateDuration;

        [Header("経路失敗時の安全待機")]
        [Tooltip("経路が無効な場合に連続して試行する最大回数です。")]
        [SerializeField, Min(1)]
        private int maximumPathRetries;

        [Tooltip("経路失敗後の次回試行までの待機時間です。")]
        [SerializeField, Min(0f)]
        private float pathRetryInterval;

        [Tooltip("最大再試行回数後にNavMesh上で待機する時間です。")]
        [SerializeField, Min(0f)]
        private float pathRetryWaitDuration;

        [SerializeField]
        private bool resolvePlayerTargetAutomatically = true;

        [Header("単体テスト用")]
        [SerializeField]
        private GameplayState fallbackGameplayState = GameplayState.Running;

        private EnemyBrainState state = EnemyBrainState.Disabled;
        private NavMeshPathStatus lastPathStatus = NavMeshPathStatus.PathInvalid;
        private double nextPathAttemptTime;
        private double attackStartedTime;
        private double nextAttackAllowedTime;
        private int nextAttackSequenceId;
        private int currentAttackSequenceId;
        private float aiTickAccumulator;
        private bool deathAnimationTriggered;
        private bool subscribed;

        private bool gameplayTickSubscribed;
        private double lastGameplayTickTime;
        private bool hasLastGameplayTickTime;
        private FallbackTicker fallbackTicker;

        /// <summary>敵AIの状態です。</summary>
        public EnemyBrainState State => state;

        /// <summary>固定された追跡対象です。</summary>
        public CombatantMarker PlayerTarget => playerTarget;

        /// <summary>下位の移動制御コンポーネントです。</summary>
        public EnemyMotor Motor => enemyMotor;

        /// <summary>現在のNavMesh経路状態です。</summary>
        public NavMeshPathStatus LastPathStatus => enemyMotor != null ? enemyMotor.LastPathStatus : lastPathStatus;

        /// <summary>最後に確認できたNavMesh上の安全な位置です。</summary>
        public Vector3 LastValidNavMeshPosition => enemyMotor != null ? enemyMotor.LastValidNavMeshPosition : Vector3.zero;

        /// <summary>最後の診断に使った攻撃系列IDです。</summary>
        public int CurrentAttackSequenceId => currentAttackSequenceId;

        /// <summary>ナビゲーションが現在有効かを返します。</summary>
        public bool IsNavigationActive => state == EnemyBrainState.Chase &&
            enemyMotor != null && enemyMotor.IsNavigationActive;

        /// <summary>攻撃評価が現在有効かを返します。</summary>
        public bool IsAttackEvaluationActive => state == EnemyBrainState.PrepareAttack || state == EnemyBrainState.Attack;

        /// <summary>経路失敗の連続試行回数です。</summary>
        public int PathRetryCount => enemyMotor != null ? enemyMotor.PathRetryCount : 0;

        /// <summary>設定されたNavMeshAgent停止距離です。</summary>
        public float ConfiguredStoppingDistance => configuredStoppingDistance;

        /// <summary>現在のゲーム状態です。</summary>
        public GameplayState CurrentGameplayState => gameFlowController != null
            ? gameFlowController.CurrentState
            : fallbackGameplayState;

        /// <summary>最後に記録した日本語診断です。</summary>
        public string LastDiagnostic => state.ToString();

        /// <summary>状態変更通知です。</summary>
        public event Action<EnemyBrainState> StateChanged;

        /// <summary>敵攻撃の開始をEnemyMeleeCombatへ通知します。</summary>
        public event Action<int> AttackRequested;

        /// <summary>攻撃評価が完了した通知です。</summary>
        public event Action<int> AttackCompleted;

        /// <summary>攻撃評価が取り消された通知です。</summary>
        public event Action<int> AttackCancelled;

        [Inject]
        public void Construct(
            GameFlowController flow = null,
            IGameplayClock clock = null,
            SceneReferenceRegistry registry = null)
        {
            if (flow != null) gameFlowController = flow;
            if (clock != null) gameplayClock = clock as GameplayClock;
            if (registry != null && playerTarget == null && registry.Player != null)
            {
                playerTarget = registry.Player;
            }

            if (isActiveAndEnabled)
            {
                SubscribeToDependencies();
            }
        }

        private void Awake()
        {
            enemyMotor = GetComponent<EnemyMotor>();
            navMeshAgent = GetComponent<NavMeshAgent>();
            combatantMarker = GetComponent<CombatantMarker>();
            healthComponent = GetComponent<HealthComponent>();
            enemyMeleeCombat ??= GetComponent<EnemyMeleeCombat>();

            UnityEngine.Assertions.Assert.IsNotNull(enemyMotor, "EnemyBrain: EnemyMotorコンポーネントが必要です。");
            UnityEngine.Assertions.Assert.IsNotNull(navMeshAgent, "EnemyBrain: NavMeshAgentコンポーネントが必要です。");
            UnityEngine.Assertions.Assert.IsNotNull(combatantMarker, "EnemyBrain: CombatantMarkerコンポーネントが必要です。");
            UnityEngine.Assertions.Assert.IsTrue(combatantMarker.Faction == CombatantMarker.CombatantFaction.Enemy, "EnemyBrain: CombatantMarkerはEnemy陣営である必要があります。");
            UnityEngine.Assertions.Assert.IsNotNull(healthComponent, "EnemyBrain: HealthComponentコンポーネントが必要です。");

            ClampConfiguration();
            SubscribeToDependencies();
            aiTickAccumulator = aiTickInterval;
            hasLastGameplayTickTime = false;
            enemyMotor.Configure(turnSpeed, configuredStoppingDistance, maximumPathRetries, pathRetryInterval, pathRetryWaitDuration);
        }

        private void OnEnable()
        {
            SubscribeToDependencies();
            aiTickAccumulator = aiTickInterval;
            enemyMotor?.Configure(turnSpeed, configuredStoppingDistance, maximumPathRetries, pathRetryInterval, pathRetryWaitDuration);
        }

        private void Start()
        {
            gameFlowController ??= FindAnyObjectByType<GameFlowController>();
            gameplayClock ??= FindAnyObjectByType<GameplayClock>();
            SubscribeToDependencies();

            // EnemyBrainのAwakeより後にPlayerが有効化される場合があるため、Startで再解決します。
            if (playerTarget == null)
            {
                ResolveFixedPlayerTarget();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromDependencies();
            StopNavigation();
            hasLastGameplayTickTime = false;
            if (state != EnemyBrainState.Removed && state != EnemyBrainState.DeathTransition)
            {
                SetState(EnemyBrainState.Disabled);
            }
        }


        private void HandleGameplayFixedTick(double fixedTime)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ProcessGameplayTick(fixedTime);
        }

        private void ProcessGameplayTick(double fixedTime)
        {
            if (fixedTime < 0d || double.IsNaN(fixedTime) || double.IsInfinity(fixedTime))
            {
                return;
            }

            double deltaTime = hasLastGameplayTickTime
                ? fixedTime - lastGameplayTickTime
                : 0d;
            lastGameplayTickTime = fixedTime;
            hasLastGameplayTickTime = true;
            aiTickAccumulator += Mathf.Max(0f, (float)deltaTime);
            if (aiTickAccumulator < aiTickInterval)
            {
                return;
            }

            aiTickAccumulator = 0f;
            EvaluateAiTick();
        }

        private void OnValidate()
        {
            ClampConfiguration();
            enemyMotor?.Configure(turnSpeed, configuredStoppingDistance, maximumPathRetries, pathRetryInterval, pathRetryWaitDuration);
        }

        /// <summary>
        /// 固定AI tickを手動実行します。編集モードの決定的テストでも使用できます。
        /// </summary>
        public void EvaluateNow()
        {
            EvaluateAiTick();
        }


        /// <summary>
        /// 外部のEnemyMeleeCombatが攻撃完了を通知します。
        /// </summary>
        public bool NotifyAttackCompleted(int sequenceId)
        {
            if (state != EnemyBrainState.Attack || sequenceId != currentAttackSequenceId)
            {
                return false;
            }

            int completedSequenceId = currentAttackSequenceId;
            currentAttackSequenceId = 0;
            SetState(EnemyBrainState.PrepareAttack);
            AttackCompleted?.Invoke(completedSequenceId);
            return true;
        }

        /// <summary>
        /// 外部のEnemyMeleeCombatが攻撃取消を通知します。
        /// </summary>
        public bool NotifyAttackCancelled(int sequenceId)
        {
            if (state != EnemyBrainState.Attack || sequenceId != currentAttackSequenceId)
            {
                return false;
            }

            int cancelledSequenceId = currentAttackSequenceId;
            currentAttackSequenceId = 0;
            SetState(EnemyBrainState.PrepareAttack);
            AttackCancelled?.Invoke(cancelledSequenceId);
            return true;
        }

        /// <summary>
        /// 死亡遷移を外部から開始します。HealthComponentのDied通知と同じ冪等契約です。
        /// </summary>
        public bool BeginDeathTransition()
        {
            if (state == EnemyBrainState.DeathTransition || state == EnemyBrainState.Removed)
            {
                return false;
            }

            CancelCurrentAttack();
            StopNavigation();
            SetState(EnemyBrainState.DeathTransition);
            TriggerDeathAnimationOnce();
            return true;
        }

        /// <summary>
        /// 死亡アニメーション完了時にRemoved状態へ遷移します。
        /// </summary>
        public bool CompleteDeathTransition()
        {
            if (state != EnemyBrainState.DeathTransition)
            {
                return false;
            }

            StopNavigation();
            SetState(EnemyBrainState.Removed);
            return true;
        }

        private void EvaluateAiTick()
        {
            if (!healthComponent.IsAlive)
            {
                if (healthComponent.IsRemoved)
                {
                    CompleteDeathTransition();
                }
                else
                {
                    BeginDeathTransition();
                }

                return;
            }

            if (state == EnemyBrainState.DeathTransition || state == EnemyBrainState.Removed)
            {
                StopNavigation();
                return;
            }

            if (CurrentGameplayState != GameplayState.Running)
            {
                CancelCurrentAttack();
                StopNavigation();
                SetState(EnemyBrainState.Disabled);
                return;
            }

            if (playerTarget == null && ResolveFixedPlayerTarget().IsErr)
            {
                StopNavigation();
                SetState(EnemyBrainState.Disabled);
                return;
            }

            if (!enemyMotor.IsNavigationActive)
            {
                lastPathStatus = NavMeshPathStatus.PathInvalid;
                SetState(EnemyBrainState.Disabled);
                return;
            }

            if (state == EnemyBrainState.Attack)
            {
                TickAttackState();
                return;
            }

            float distance = HorizontalDistance(transform.position, playerTarget.transform.position);
            if (distance <= meleeRange)
            {
                StopNavigation();
                FaceTarget();
                SetState(EnemyBrainState.PrepareAttack);
                BeginAttackEvaluation();
                return;
            }

            CancelPreparedAttackIfTargetLeftRange();
            SetState(EnemyBrainState.Chase);
            EvaluateChasePath();
        }

        private void EvaluateChasePath()
        {
            double now = CurrentFixedTime;
            bool shouldQuery = now >= nextPathAttemptTime;
            if (shouldQuery)
            {
                nextPathAttemptTime = now + Mathf.Max(pathQueryInterval, pathRetryInterval);
            }

            enemyMotor.NavigateTo(playerTarget.transform.position, now, shouldQuery);
            lastPathStatus = enemyMotor.LastPathStatus;
        }

        private void BeginAttackEvaluation()
        {
            double now = CurrentFixedTime;
            if (now < nextAttackAllowedTime || state == EnemyBrainState.Attack)
            {
                return;
            }

            int sequenceId = ++nextAttackSequenceId;
            if (sequenceId <= 0)
            {
                nextAttackSequenceId = 1;
                sequenceId = nextAttackSequenceId;
            }

            if (enemyMeleeCombat != null)
            {
                Result startResult = enemyMeleeCombat.BeginAttack(sequenceId);
                if (startResult.IsErr)
                {
                    startResult.LogIfErr(this, "[EnemyBrain] 攻撃開始要求拒絶");
                    return;
                }
            }

            currentAttackSequenceId = sequenceId;
            attackStartedTime = now;
            nextAttackAllowedTime = now + attackCooldown;
            SetState(EnemyBrainState.Attack);
            StopNavigation();
            FaceTarget();
            if (animationDriver != null)
            {
                animationDriver.TriggerAttack();
            }
            AttackRequested?.Invoke(sequenceId);
        }

        private void TickAttackState()
        {
            StopNavigation();
            FaceTarget();

            if (CurrentFixedTime - attackStartedTime >= attackStateDuration)
            {
                if (enemyMeleeCombat != null && enemyMeleeCombat.IsAttacking)
                {
                    enemyMeleeCombat.CompleteAttack(currentAttackSequenceId);
                }
                else
                {
                    NotifyAttackCompleted(currentAttackSequenceId);
                }
            }
        }

        private void CancelPreparedAttackIfTargetLeftRange()
        {
            if (state != EnemyBrainState.PrepareAttack)
            {
                return;
            }

            SetState(EnemyBrainState.Chase);
        }

        private void StopNavigation()
        {
            enemyMotor.StopNavigation();
        }

        private void FaceTarget()
        {
            if (playerTarget == null)
            {
                return;
            }

            enemyMotor.FaceTarget(playerTarget.transform.position);
        }

        private Result<CombatantMarker> ResolveFixedPlayerTarget()
        {
            if (playerTarget != null)
            {
                if (playerTarget.Faction == CombatantMarker.CombatantFaction.Player && playerTarget.IsIdentityValid && playerTarget.IsAvailableForCombat)
                {
                    return playerTarget;
                }

                return GameError.TargetUnavailable;
            }

            if (!resolvePlayerTargetAutomatically)
            {
                return GameError.TargetUnavailable;
            }

            if (gameFlowController != null && gameFlowController.SceneReferences != null && gameFlowController.SceneReferences.Player != null)
            {
                CombatantMarker registeredPlayer = gameFlowController.SceneReferences.Player;
                if (registeredPlayer.Faction == CombatantMarker.CombatantFaction.Player && registeredPlayer.IsIdentityValid && registeredPlayer.IsAvailableForCombat)
                {
                    playerTarget = registeredPlayer;
                    return playerTarget;
                }
            }

            // シーン内からPlayer陣営の参戦者マーカーを直接検索（フォールバック）
            CombatantMarker[] markers = FindObjectsByType<CombatantMarker>(FindObjectsInactive.Exclude);
            for (int index = 0; index < markers.Length; index++)
            {
                CombatantMarker marker = markers[index];
                if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Player && marker.IsIdentityValid && marker.IsAvailableForCombat)
                {
                    playerTarget = marker;
                    return playerTarget;
                }
            }

            return GameError.EnemyTargetLost;
        }

        private void HandleHealthStateChanged(HealthState nextState)
        {
            if (nextState == HealthState.DeathTransition)
            {
                BeginDeathTransition();
            }
            else if (nextState == HealthState.Removed)
            {
                StopNavigation();
                SetState(EnemyBrainState.Removed);
            }
        }

        private void HandleHealthDied()
        {
            BeginDeathTransition();
        }

        private void HandleFlowStateChanged(GameplayState nextState)
        {
            if (nextState != GameplayState.Running)
            {
                CancelCurrentAttack();
                StopNavigation();
                if (state != EnemyBrainState.DeathTransition && state != EnemyBrainState.Removed)
                {
                    SetState(EnemyBrainState.Disabled);
                }
            }
        }

        private void SubscribeToDependencies()
        {
            if (!subscribed)
            {
                if (healthComponent != null)
                {
                    healthComponent.Died += HandleHealthDied;
                    healthComponent.StateChanged += HandleHealthStateChanged;
                }

                if (gameFlowController != null)
                {
                    gameFlowController.StateChanged += HandleFlowStateChanged;
                }

                if (enemyMeleeCombat != null)
                {
                    enemyMeleeCombat.AttackCompleted += HandleMeleeAttackCompleted;
                    enemyMeleeCombat.AttackCancelled += HandleMeleeAttackCancelled;
                }

                subscribed = true;
            }

            if (!gameplayTickSubscribed && gameplayClock != null)
            {
                gameplayClock.FixedTick += HandleGameplayFixedTick;
                gameplayTickSubscribed = true;
                if (fallbackTicker != null)
                {
                    fallbackTicker.enabled = false;
                }
            }
            else if (!gameplayTickSubscribed && gameplayClock == null)
            {
                if (fallbackTicker == null)
                {
                    fallbackTicker = gameObject.AddComponent<FallbackTicker>();
                    fallbackTicker.Initialize(this);
                }
                fallbackTicker.enabled = true;
            }
        }

        private void UnsubscribeFromDependencies()
        {
            if (subscribed)
            {
                if (healthComponent != null)
                {
                    healthComponent.Died -= HandleHealthDied;
                    healthComponent.StateChanged -= HandleHealthStateChanged;
                }

                if (gameFlowController != null)
                {
                    gameFlowController.StateChanged -= HandleFlowStateChanged;
                }

                if (enemyMeleeCombat != null)
                {
                    enemyMeleeCombat.AttackCompleted -= HandleMeleeAttackCompleted;
                    enemyMeleeCombat.AttackCancelled -= HandleMeleeAttackCancelled;
                }

                subscribed = false;
            }

            if (gameplayTickSubscribed && gameplayClock != null)
            {
                gameplayClock.FixedTick -= HandleGameplayFixedTick;
                gameplayTickSubscribed = false;
            }

            if (fallbackTicker != null)
            {
                fallbackTicker.enabled = false;
            }
        }

        private void HandleMeleeAttackCompleted(int sequenceId)
        {
            NotifyAttackCompleted(sequenceId);
        }

        private void HandleMeleeAttackCancelled(int sequenceId)
        {
            NotifyAttackCancelled(sequenceId);
        }

        private void ClampConfiguration()
        {
            meleeRange = Mathf.Max(0f, meleeRange);
            configuredStoppingDistance = Mathf.Max(0f, configuredStoppingDistance);
            turnSpeed = Mathf.Max(0f, turnSpeed);
            aiTickInterval = Mathf.Max(0.001f, aiTickInterval);
            pathQueryInterval = Mathf.Max(0.001f, pathQueryInterval);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            attackStateDuration = Mathf.Max(0f, attackStateDuration);
            maximumPathRetries = Mathf.Max(1, maximumPathRetries);
            pathRetryInterval = Mathf.Max(0f, pathRetryInterval);
            pathRetryWaitDuration = Mathf.Max(0f, pathRetryWaitDuration);
        }

        private void SetState(EnemyBrainState nextState)
        {
            if (state == nextState)
            {
                return;
            }

            state = nextState;
            StateChanged?.Invoke(nextState);
        }

        private void CancelCurrentAttack()
        {
            if (state != EnemyBrainState.Attack || currentAttackSequenceId == 0)
            {
                return;
            }

            int cancelledSequenceId = currentAttackSequenceId;
            currentAttackSequenceId = 0;
            enemyMeleeCombat?.CancelAttack();
            AttackCancelled?.Invoke(cancelledSequenceId);
            SetState(EnemyBrainState.Disabled);
        }

        private void TriggerDeathAnimationOnce()
        {
            if (deathAnimationTriggered)
            {
                return;
            }

            deathAnimationTriggered = true;
            animationDriver.TriggerDeath();
        }

        private double CurrentFixedTime => gameplayClock != null ? gameplayClock.FixedNow : Time.fixedTimeAsDouble;

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private sealed class FallbackTicker : MonoBehaviour
        {
            private EnemyBrain brain;

            public void Initialize(EnemyBrain enemyBrain)
            {
                brain = enemyBrain;
                hideFlags = HideFlags.HideAndDontSave;
            }

            private void FixedUpdate()
            {
                if (brain.isActiveAndEnabled)
                {
                    brain.ProcessGameplayTick(Time.fixedTimeAsDouble);
                }
            }
        }
    }

    /// <summary>敵AIの状態です。</summary>
    public enum EnemyBrainState
    {
        Disabled,
        Chase,
        PrepareAttack,
        Attack,
        DeathTransition,
        Removed
    }
}
