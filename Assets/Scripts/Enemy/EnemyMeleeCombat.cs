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
        public CombatantMarker CombatantMarker => combatantMarker != null ? combatantMarker : (combatantMarker = GetComponent<CombatantMarker>());

        /// <summary>最後に記録した診断です。</summary>
        public string LastDiagnostic { get; private set; } = string.Empty;

        /// <summary>攻撃開始、完了、取消の通知です。</summary>
        public event Action<int> AttackStarted;
        public event Action<int> AttackCompleted;
        public event Action<int> AttackCancelled;

        /// <summary>DamageServiceへ送信された有効なPlayer命中の通知です。</summary>
        public event Action<CombatantMarker, int> PlayerHitSubmitted;

        /// <summary>日本語の攻撃診断です。</summary>
        public event Action<string> DiagnosticReported;

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
            ClampConfiguration();
            InitializeAttackSequence();
            ValidateConfiguration();
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
        public bool TryBeginAttack(int sequenceId, out string diagnostic)
        {
            if (!EnsureReferencesReady())
            {
                diagnostic = LastDiagnostic;
                return false;
            }

            if (CurrentGameplayState != GameplayState.Running)
            {
                diagnostic = "終局または初期化中のため、敵の攻撃を開始できません。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (healthComponent != null && !healthComponent.IsAlive)
            {
                diagnostic = "死亡状態の敵は攻撃を開始できません。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (IsAttacking)
            {
                diagnostic = $"攻撃系列{currentAttackSequenceId}が進行中のため、新しい敵攻撃を開始できません。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (CurrentGameTime < nextAttackAllowedTime)
            {
                diagnostic = "敵の攻撃が冷却中のため、再開を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (!DamageRequest.IsValidAttackSequenceId(sequenceId))
            {
                diagnostic = "攻撃系列IDが無効なため、敵攻撃を開始できません。";
                ReportDiagnostic(diagnostic, true);
                return false;
            }

            if (!attackSequence.StartSequence(sequenceId, out diagnostic))
            {
                ReportDiagnostic(diagnostic, false);
                return false;
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
            return true;
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃ウィンドウを開きます。</summary>
        public bool AnimationEventBeginAttackWindow()
        {
            if (!IsAttacking || CurrentGameplayState != GameplayState.Running)
            {
                return false;
            }

            return attackSequence.OnAttackWindowOpenEvent(out string diagnostic) || ReportDiagnosticAndReturnFalse(diagnostic);
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃ウィンドウを閉じます。</summary>
        public bool AnimationEventEndAttackWindow()
        {
            if (attackSequence == null)
            {
                return false;
            }

            return attackSequence.OnAttackWindowCloseEvent();
        }

        /// <summary>敵Attack clipのAnimator eventから攻撃系列を完了します。</summary>
        public bool AnimationEventCompleteAttack()
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
            if (!TryBeginAttack(sequenceId, out string diagnostic) && enemyBrain != null)
            {
                if (!string.IsNullOrEmpty(diagnostic))
                {
                    ReportDiagnostic(diagnostic, false);
                }

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

        private bool CompleteAttack(int sequenceId, bool notifyBrain)
        {
            if (completionInProgress || attackSequence == null || !attackSequence.IsActive || sequenceId == 0 || sequenceId != currentAttackSequenceId)
            {
                return false;
            }

            completionInProgress = true;
            try
            {
                if (!attackSequence.Complete(out string diagnostic))
                {
                    if (!string.IsNullOrEmpty(diagnostic))
                    {
                        ReportDiagnostic(diagnostic, false);
                    }

                    return false;
                }

                attackAnimationObserved = false;
                currentAttackSequenceId = 0;
                AttackCompleted?.Invoke(sequenceId);
                if (notifyBrain && enemyBrain != null)
                {
                    enemyBrain.NotifyAttackCompleted(sequenceId);
                }

                return true;
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
                ReportDiagnostic(
                    $"敵攻撃系列{sequenceId}はPlayer以外の対象を無視しました。対象「{GetCombatantName(target)}」。",
                    false);
                return;
            }

            if (CurrentGameplayState != GameplayState.Running || combatantMarker == null || damageService == null)
            {
                ReportDiagnostic("敵攻撃の必須参照またはRunning状態がないため、Playerへの命中を無視しました。", false);
                return;
            }

            Vector3 hitPoint = target.transform.position;
            if (damageService.Submit(
                    combatantMarker,
                    target,
                    AttackDamage,
                    sequenceId,
                    AttackKinds.EnemyMelee,
                    attackWindowTracker,
                    hitPoint,
                    out string diagnostic))
            {
                PlayerHitSubmitted?.Invoke(target, sequenceId);
                return;
            }

            if (!string.IsNullOrEmpty(diagnostic))
            {
                ReportDiagnostic(diagnostic, false);
            }
        }

        private void TickAttackAnimation()
        {
            if (!IsAttacking)
            {
                return;
            }

            if (animationDriver != null && animationDriver.TryGetAttackNormalizedTime(out float normalizedTime))
            {
                attackAnimationObserved = true;
                attackSequence.Tick(normalizedTime);
                if (normalizedTime >= AttackCompletionNormalizedTime)
                {
                    CompleteAttack(currentAttackSequenceId, true);
                }

                return;
            }

            if (attackAnimationObserved)
            {
                CompleteAttack(currentAttackSequenceId, true);
            }
        }

        private bool EnsureReferencesReady()
        {
            InitializeAttackSequence();

            if (combatantMarker == null)
            {
                ReportDiagnostic("EnemyMeleeCombatにCombatantMarker参照がありません。", true);
                return false;
            }

            if (attackSequence == null || attackWindowTracker == null)
            {
                ReportDiagnostic("EnemyMeleeCombatの攻撃系列を初期化できません。", true);
                return false;
            }

            if (damageService == null)
            {
                ReportDiagnostic("EnemyMeleeCombatにDamageService参照がありません。", true);
                return false;
            }

            if (weaponHitbox == null)
            {
                ReportDiagnostic("EnemyMeleeCombatにEnemyHitbox参照がありません。", true);
                return false;
            }

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

        private void ValidateConfiguration()
        {
            if (combatantMarker == null)
            {
                ReportDiagnostic("EnemyMeleeCombatにCombatantMarker参照がありません。", true);
            }
            else if (combatantMarker.Faction != CombatantMarker.CombatantFaction.Enemy)
            {
                ReportDiagnostic("EnemyMeleeCombatのCombatantMarkerがEnemy陣営ではありません。", true);
            }

            if (weaponHitbox == null)
            {
                ReportDiagnostic("EnemyMeleeCombatにEnemyHitbox参照がありません。", true);
            }

            if (damageService == null)
            {
                ReportDiagnostic("EnemyMeleeCombatにDamageService参照がありません。", true);
            }
        }

        private bool ReportDiagnosticAndReturnFalse(string diagnostic)
        {
            if (!string.IsNullOrEmpty(diagnostic))
            {
                ReportDiagnostic(diagnostic, false);
            }

            return false;
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            LastDiagnostic = message;
            if (asError && missingReferenceDiagnosticReported && message.Contains("参照"))
            {
                return;
            }

            if (asError && message.Contains("参照"))
            {
                missingReferenceDiagnosticReported = true;
            }

            if (asError)
            {
                Debug.LogError($"[敵攻撃診断] {message}", this);
            }
            else
            {
                Debug.Log($"[敵攻撃診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
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
