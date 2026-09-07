using System;
using UnityEngine;
using UnityEngine.AI;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の追跡、近接距離への遷移、攻撃評価、死亡遷移を管理します。
    /// 実際のダメージ処理はEnemyMeleeCombatとDamageServiceへ委譲し、
    /// このコンポーネントはNavMesh上の移動と状態の門番だけを担当します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyBrain : MonoBehaviour
    {
        private const float MinimumAiTickInterval = 0.02f;
        private const float MaximumAiTickInterval = 0.2f;
        private const float MinimumDistance = 0.01f;
        private const float MovementEpsilon = 0.01f;

        [Header("参照")]
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private CombatantMarker combatantMarker;

        [Tooltip("敵が追跡するKnightです。未設定時は起動時にPlayer陣営から一体だけ解決します。")]
        [SerializeField]
        private CombatantMarker playerTarget;

        [SerializeField]
        private HealthComponent healthComponent;

        [SerializeField]
        private EnemyAnimationDriver animationDriver;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        [Header("追跡設定")]
        [Tooltip("この距離以内では追跡を停止してKnightの方向を向きます。")]
        [SerializeField, Min(MinimumDistance)]
        private float meleeRange = 1.8f;

        [Tooltip("NavMeshAgentの停止距離です。")]
        [SerializeField, Min(MinimumDistance)]
        private float configuredStoppingDistance = 1.25f;

        [Tooltip("敵が向きを変える最大角速度です。")]
        [SerializeField, Min(1f)]
        private float turnSpeed = 540f;

        [Tooltip("NavMeshを評価する固定ゲーム時間の間隔です。0.2秒を超えないように制限されます。")]
        [SerializeField, Range(MinimumAiTickInterval, MaximumAiTickInterval)]
        private float aiTickInterval = 0.1f;

        [Tooltip("NavMesh経路を再計算する最小のゲーム時間間隔です。固定AI tickより短くはなりません。")]
        [SerializeField, Min(MinimumAiTickInterval)]
        private float pathQueryInterval = 0.25f;

        [Header("攻撃評価")]
        [Tooltip("攻撃評価の間隔です。実際の攻撃とダメージはEnemyMeleeCombatが担当します。")]
        [SerializeField, Min(0f)]
        private float attackCooldown = 1.25f;

        [Tooltip("攻撃開始後、完了通知がない場合にAttack状態を終了する時間です。")]
        [SerializeField, Min(MinimumDistance)]
        private float attackStateDuration = 0.8f;

        [Header("経路失敗時の安全待機")]
        [Tooltip("経路が無効な場合に連続して試行する最大回数です。")]
        [SerializeField, Min(1)]
        private int maximumPathRetries = 3;

        [Tooltip("経路失敗後の次回試行までの待機時間です。")]
        [SerializeField, Min(0f)]
        private float pathRetryInterval = 0.5f;

        [Tooltip("最大再試行回数後にNavMesh上で待機する時間です。")]
        [SerializeField, Min(0f)]
        private float pathRetryWaitDuration = 2f;

        [SerializeField]
        private bool resolvePlayerTargetAutomatically = true;

        [Header("単体テスト用")]
        [SerializeField]
        private GameplayState fallbackGameplayState = GameplayState.Running;

        private EnemyBrainState state = EnemyBrainState.Disabled;
        private NavMeshPathStatus lastPathStatus = NavMeshPathStatus.PathInvalid;
        private Vector3 lastValidNavMeshPosition;
        private bool hasLastValidNavMeshPosition;
        private int pathRetryCount;
        private bool pathRetryWaitActive;
        private double nextPathAttemptTime;
        private double attackStartedTime;
        private double nextAttackAllowedTime;
        private int nextAttackSequenceId;
        private int currentAttackSequenceId;
        private float aiTickAccumulator;
        private bool targetResolutionAttempted;
        private bool deathAnimationTriggered;
        private bool subscribed;

        private bool gameplayTickSubscribed;
        private double lastGameplayTickTime;
        private bool hasLastGameplayTickTime;
        private NavMeshPath reusablePath;
        private readonly Vector3[] reusablePathCorners = new Vector3[32];
        private int pathQueryCount;
        private bool targetDiagnosticReported;

        /// <summary>敵AIの状態です。</summary>
        public EnemyBrainState State => state;

        /// <summary>EnemyBrainStateの別名です。</summary>
        public EnemyBrainState CurrentState => state;

        /// <summary>固定された追跡対象です。</summary>
        public CombatantMarker PlayerTarget => playerTarget;

        /// <summary>現在のNavMesh経路状態です。</summary>
        public NavMeshPathStatus LastPathStatus => lastPathStatus;

        /// <summary>最後に確認できたNavMesh上の安全な位置です。</summary>
        public Vector3 LastValidNavMeshPosition => lastValidNavMeshPosition;

        /// <summary>最後の診断に使った攻撃系列IDです。</summary>
        public int CurrentAttackSequenceId => currentAttackSequenceId;

        /// <summary>ナビゲーションが現在有効かを返します。</summary>
        public bool IsNavigationActive => state == EnemyBrainState.Chase &&
            navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;

        /// <summary>攻撃評価が現在有効かを返します。</summary>
        public bool IsAttackEvaluationActive => state == EnemyBrainState.PrepareAttack || state == EnemyBrainState.Attack;

        /// <summary>経路失敗の連続試行回数です。</summary>
        public int PathRetryCount => pathRetryCount;

        /// <summary>現在のゲーム状態です。</summary>
        public GameplayState CurrentGameplayState => gameFlowController != null
            ? gameFlowController.CurrentState
            : fallbackGameplayState;

        /// <summary>最後に記録した日本語診断です。</summary>
        public string LastDiagnostic { get; private set; } = string.Empty;

        /// <summary>状態変更通知です。</summary>
        public event Action<EnemyBrainState> StateChanged;

        /// <summary>敵攻撃の開始をEnemyMeleeCombatへ通知します。</summary>
        public event Action<int> AttackRequested;

        /// <summary>攻撃評価が完了した通知です。</summary>
        public event Action<int> AttackCompleted;

        /// <summary>攻撃評価が取り消された通知です。</summary>
        public event Action<int> AttackCancelled;

        /// <summary>経路、参照、状態異常の日本語診断通知です。</summary>
        public event Action<string> DiagnosticReported;

        private void Awake()
        {
            ResolveReferences();
            ClampConfiguration();
            EnsurePathCache();
            SubscribeToDependencies();
            aiTickAccumulator = aiTickInterval;
            hasLastGameplayTickTime = false;
            CaptureCurrentNavMeshPosition();
            ValidateConfiguration();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeToDependencies();
            aiTickAccumulator = aiTickInterval;
        }

        private void Start()
        {
            // EnemyBrainのAwakeより後にPlayerが有効化される場合があるため、Startで再解決します。
            if (playerTarget == null)
            {
                targetResolutionAttempted = false;
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

        private void FixedUpdate()
        {
            if (gameplayClock != null || !isActiveAndEnabled)
            {
                return;
            }

            ProcessGameplayTick(Time.fixedTimeAsDouble);
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
            if (!GameplayClock.IsValidTimestamp(fixedTime))
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
        }

        /// <summary>
        /// 固定AI tickを手動実行します。編集モードの決定的テストでも使用できます。
        /// </summary>
        public void EvaluateNow()
        {
            EvaluateAiTick();
        }

        /// <summary>
        /// 実行時依存を明示的に差し替えます。NavMeshと状態遷移のテスト用です。
        /// </summary>
        public void ConfigureForTests(
            NavMeshAgent agent,
            CombatantMarker enemy,
            CombatantMarker target,
            HealthComponent health,
            GameFlowController flow,
            EnemyAnimationDriver driver = null,
            GameplayClock clock = null)
        {
            UnsubscribeFromDependencies();
            navMeshAgent = agent;
            combatantMarker = enemy;
            playerTarget = target;
            healthComponent = health;
            gameFlowController = flow;
            animationDriver = driver;
            gameplayClock = clock;
            targetResolutionAttempted = target != null;
            ResolveReferences();
            SubscribeToDependencies();
            ResetPathFailureState();
            aiTickAccumulator = aiTickInterval;
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
            if (healthComponent != null && !healthComponent.IsAlive)
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

            if (playerTarget == null || !ResolveFixedPlayerTarget())
            {
                StopNavigation();
                SetState(EnemyBrainState.Disabled);
                return;
            }

            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                HandleInvalidPath(NavMeshPathStatus.PathInvalid, "NavMeshAgentが有効でないか、NavMesh上にありません。");
                SetState(EnemyBrainState.Disabled);
                return;
            }

            CaptureCurrentNavMeshPosition();

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
                animationDriver?.SetMovementState(false, 0f);
                TryBeginAttackEvaluation();
                return;
            }

            CancelPreparedAttackIfTargetLeftRange();
            SetState(EnemyBrainState.Chase);
            EvaluateChasePath();
        }

        private void EvaluateChasePath()
        {
            double now = CurrentFixedTime;
            if (pathRetryWaitActive)
            {
                if (now < nextPathAttemptTime)
                {
                    MaintainExistingNavigation();
                    return;
                }

                pathRetryWaitActive = false;
                pathRetryCount = 0;
                nextPathAttemptTime = now;
            }

            if (now < nextPathAttemptTime)
            {
                // 経路問い合わせの節流中は既存のNavMesh経路を維持します。
                MaintainExistingNavigation();
                return;
            }

            EnsurePathCache();
            bool pathCalculated = NavMesh.CalculatePath(
                transform.position,
                playerTarget.transform.position,
                navMeshAgent.areaMask,
                reusablePath);
            pathQueryCount++;
            int cornerCount = reusablePath.GetCornersNonAlloc(reusablePathCorners);
            Vector3[] pathCorners = reusablePathCorners;

            lastPathStatus = pathCalculated ? reusablePath.status : NavMeshPathStatus.PathInvalid;
            if (!pathCalculated || reusablePath.status != NavMeshPathStatus.PathComplete || pathCorners == null || cornerCount == 0)
            {
                HandleInvalidPath(lastPathStatus, "Knightまでの有効なNavMesh経路がありません。");
                KeepAtLastValidNavMeshPosition();
                return;
            }

            pathRetryCount = 0;
            pathRetryWaitActive = false;
            nextPathAttemptTime = now + Mathf.Max(pathQueryInterval, pathRetryInterval);
            CaptureCurrentNavMeshPosition();

            navMeshAgent.stoppingDistance = configuredStoppingDistance;
            navMeshAgent.isStopped = false;
            if (!navMeshAgent.SetDestination(playerTarget.transform.position))
            {
                lastPathStatus = NavMeshPathStatus.PathInvalid;
                HandleInvalidPath(lastPathStatus, "NavMeshAgentが目的地を設定できませんでした。");
                KeepAtLastValidNavMeshPosition();
                return;
            }

            FaceMovementDirection();
            UpdateMovementAnimation();
        }

        private void TryBeginAttackEvaluation()
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

            currentAttackSequenceId = sequenceId;
            attackStartedTime = now;
            nextAttackAllowedTime = now + attackCooldown;
            SetState(EnemyBrainState.Attack);
            StopNavigation();
            FaceTarget();
            animationDriver?.SetMovementState(false, 0f);
            animationDriver?.TriggerAttack();
            AttackRequested?.Invoke(sequenceId);
        }

        private void TickAttackState()
        {
            StopNavigation();
            FaceTarget();
            animationDriver?.SetMovementState(false, 0f);

            if (CurrentFixedTime - attackStartedTime >= attackStateDuration)
            {
                NotifyAttackCompleted(currentAttackSequenceId);
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

        private void HandleInvalidPath(NavMeshPathStatus pathStatus, string reason)
        {
            lastPathStatus = pathStatus;
            double now = CurrentFixedTime;
            if (pathRetryWaitActive && now < nextPathAttemptTime)
            {
                return;
            }

            if (pathRetryCount < maximumPathRetries)
            {
                pathRetryCount++;
                nextPathAttemptTime = now + pathRetryInterval;
                ReportPathDiagnostic(
                    $"{reason} 再試行{pathRetryCount}/{maximumPathRetries}回。",
                    false);
                return;
            }

            pathRetryWaitActive = true;
            nextPathAttemptTime = now + pathRetryWaitDuration;
            ReportPathDiagnostic(
                $"{reason} 有限回数の再試行が終了したため、{pathRetryWaitDuration:F1}秒間NavMesh上で待機します。",
                false);
        }

        private void KeepAtLastValidNavMeshPosition()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled)
            {
                return;
            }

            if (navMeshAgent.isOnNavMesh)
            {
                // 新しい経路の計算に失敗しても、現在の有効経路が残っていれば
                // その経路を最後まで進めます。経路を持たない場合だけ待機します。
                if (navMeshAgent.hasPath || navMeshAgent.pathPending)
                {
                    MaintainExistingNavigation();
                    return;
                }

                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            if (!hasLastValidNavMeshPosition)
            {
                ReportPathDiagnostic("最後に確認したNavMesh上の位置がないため、安全位置へ戻せませんでした。", true);
                return;
            }

            NavMeshHit hit;
            bool sampled = NavMesh.SamplePosition(
                lastValidNavMeshPosition,
                out hit,
                Mathf.Max(configuredStoppingDistance, 1f),
                navMeshAgent.areaMask);
            if (sampled && navMeshAgent.Warp(hit.position))
            {
                lastValidNavMeshPosition = hit.position;
                navMeshAgent.isStopped = true;
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            ReportPathDiagnostic("最後のNavMesh位置を再取得できなかったため、敵を移動させず待機します。", true);
        }

        private void MaintainExistingNavigation()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                return;
            }

            if (navMeshAgent.hasPath || navMeshAgent.pathPending)
            {
                navMeshAgent.isStopped = false;
                FaceMovementDirection();
                UpdateMovementAnimation();
                return;
            }

            navMeshAgent.isStopped = true;
            animationDriver?.SetMovementState(false, 0f);
        }

        private void UpdateMovementAnimation()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            float actualSpeed = navMeshAgent.velocity.magnitude;
            float normalizedSpeed = navMeshAgent.speed > MovementEpsilon
                ? Mathf.Clamp01(actualSpeed / navMeshAgent.speed)
                : 0f;
            animationDriver?.SetMovementState(actualSpeed > MovementEpsilon, normalizedSpeed);
        }


        private void CaptureCurrentNavMeshPosition()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                return;
            }

            lastValidNavMeshPosition = navMeshAgent.nextPosition;
            hasLastValidNavMeshPosition = IsFiniteVector(lastValidNavMeshPosition);
        }

        private void StopNavigation()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled)
            {
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            if (navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
            }

            animationDriver?.SetMovementState(false, 0f);
        }

        private void FaceTarget()
        {
            if (playerTarget == null)
            {
                return;
            }

            Vector3 direction = playerTarget.transform.position - transform.position;
            direction.y = 0f;
            RotateTowards(direction);
        }

        private void FaceMovementDirection()
        {
            if (navMeshAgent == null)
            {
                return;
            }

            Vector3 direction = navMeshAgent.desiredVelocity;
            direction.y = 0f;
            if (direction.sqrMagnitude > MovementEpsilon * MovementEpsilon)
            {
                RotateTowards(direction);
            }
        }

        private void RotateTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude <= MovementEpsilon * MovementEpsilon)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
        }

        private bool ResolveFixedPlayerTarget()
        {
            if (playerTarget != null)
            {
                if (playerTarget.Faction == CombatantMarker.CombatantFaction.Player && playerTarget.IsIdentityValid && playerTarget.IsAvailableForCombat)
                {
                    targetDiagnosticReported = false;
                    return true;
                }

                ReportTargetDiagnostic("固定されたKnight対象が無効、非アクティブ、またはPlayer陣営ではありません。", true);
                return false;
            }

            if (!resolvePlayerTargetAutomatically)
            {
                ReportTargetDiagnostic("追跡対象のKnightが設定されていません。", true);
                targetResolutionAttempted = true;
                return false;
            }

            targetResolutionAttempted = true;
            if (gameFlowController != null && gameFlowController.SceneReferences != null && gameFlowController.SceneReferences.Player != null)
            {
                CombatantMarker registeredPlayer = gameFlowController.SceneReferences.Player;
                if (registeredPlayer.Faction == CombatantMarker.CombatantFaction.Player && registeredPlayer.IsIdentityValid && registeredPlayer.IsAvailableForCombat)
                {
                    playerTarget = registeredPlayer;
                    targetDiagnosticReported = false;
                    return true;
                }
            }

            CombatantMarker[] markers = FindObjectsByType<CombatantMarker>();
            for (int index = 0; index < markers.Length; index++)
            {
                CombatantMarker marker = markers[index];
                if (marker == null || marker == combatantMarker || !marker.IsAvailableForCombat || !marker.IsIdentityValid)
                {
                    continue;
                }

                if (marker.Faction == CombatantMarker.CombatantFaction.Player)
                {
                    playerTarget = marker;
                    targetDiagnosticReported = false;
                    return true;
                }
            }

            ReportTargetDiagnostic("追跡対象のKnightを自動解決できませんでした。", true);
            return false;
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

                subscribed = true;
            }

            if (!gameplayTickSubscribed && gameplayClock != null)
            {
                gameplayClock.FixedTick += HandleGameplayFixedTick;
                gameplayTickSubscribed = true;
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

                subscribed = false;
            }

            if (gameplayTickSubscribed && gameplayClock != null)
            {
                gameplayClock.FixedTick -= HandleGameplayFixedTick;
                gameplayTickSubscribed = false;
            }
        }

        private void ResolveReferences()
        {
            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (combatantMarker == null)
            {
                combatantMarker = GetComponent<CombatantMarker>();
            }

            if (healthComponent == null)
            {
                healthComponent = GetComponent<HealthComponent>();
            }

            if (animationDriver == null)
            {
                animationDriver = GetComponent<EnemyAnimationDriver>();
            }

            if (gameFlowController == null)
            {
                gameFlowController = FindAnyObjectByType<GameFlowController>();
            }

            if (gameplayClock == null)
            {
                gameplayClock = FindAnyObjectByType<GameplayClock>();
            }

            EnsurePathCache();
            if (playerTarget == null && !targetResolutionAttempted && resolvePlayerTargetAutomatically)
            {
                ResolveFixedPlayerTarget();
            }
        }

        private void EnsurePathCache()
        {
            if (reusablePath == null)
            {
                reusablePath = new NavMeshPath();
            }
        }


        private void ValidateConfiguration()
        {
            if (combatantMarker == null)
            {
                ReportDiagnostic("EnemyBrainにCombatantMarker参照がありません。", true);
            }
            else if (combatantMarker.Faction != CombatantMarker.CombatantFaction.Enemy)
            {
                ReportDiagnostic("EnemyBrainのCombatantMarkerがEnemy陣営ではありません。", true);
            }

            if (navMeshAgent == null)
            {
                ReportDiagnostic("EnemyBrainにNavMeshAgent参照がありません。", true);
            }

            if (healthComponent == null)
            {
                ReportDiagnostic("EnemyBrainにHealthComponent参照がありません。", true);
            }
        }

        private void ClampConfiguration()
        {
            meleeRange = Mathf.Max(MinimumDistance, meleeRange);
            configuredStoppingDistance = Mathf.Max(MinimumDistance, configuredStoppingDistance);
            turnSpeed = Mathf.Max(1f, turnSpeed);
            aiTickInterval = Mathf.Clamp(aiTickInterval, MinimumAiTickInterval, MaximumAiTickInterval);
            pathQueryInterval = Mathf.Max(MinimumAiTickInterval, pathQueryInterval);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            attackStateDuration = Mathf.Max(MinimumDistance, attackStateDuration);
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
            animationDriver?.TriggerDeath();
        }

        private void ResetPathFailureState()
        {
            pathRetryCount = 0;
            pathRetryWaitActive = false;
            nextPathAttemptTime = 0d;
            lastPathStatus = NavMeshPathStatus.PathInvalid;
            pathQueryCount = 0;
            LastDiagnostic = string.Empty;
        }

        private double CurrentFixedTime => gameplayClock != null ? gameplayClock.FixedNow : Time.fixedTimeAsDouble;

        private void ReportPathDiagnostic(string reason, bool asError)
        {
            string enemyName = GetEnemyName();
            string targetName = GetTargetName();
            string message = $"敵「{enemyName}」からKnight「{targetName}」への経路診断: {reason} 経路状態「{lastPathStatus}」。";
            ReportDiagnostic(message, asError);
        }

        private void ReportTargetDiagnostic(string reason, bool asError)
        {
            if (targetDiagnosticReported)
            {
                return;
            }

            targetDiagnosticReported = true;
            ReportDiagnostic($"敵「{GetEnemyName()}」の対象診断: {reason}", asError);
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            if (string.Equals(LastDiagnostic, message, StringComparison.Ordinal))
            {
                return;
            }

            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[敵AI診断] {message}", this);
            }
            else
            {
                Debug.LogWarning($"[敵AI診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }

        private string GetEnemyName()
        {
            return combatantMarker != null && !string.IsNullOrWhiteSpace(combatantMarker.CombatantId)
                ? combatantMarker.CombatantId
                : gameObject.name;
        }

        private string GetTargetName()
        {
            return playerTarget != null && !string.IsNullOrWhiteSpace(playerTarget.CombatantId)
                ? playerTarget.CombatantId
                : "不明";
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
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
