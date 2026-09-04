using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の死亡アニメーションと戦闘対象の登録解除を管理します。
    /// 死亡後はAttack、NavMesh、DamageServiceへの登録を停止し、
    /// Death clip完了後にHealthをRemovedへ遷移させて敵を除去します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyLifecycle : MonoBehaviour
    {
        private const float MinimumCompletionNormalizedTime = 0.5f;
        private const float MaximumCompletionNormalizedTime = 1f;
        private const float MinimumFallbackDuration = 0.1f;

        [Header("参照")]
        [SerializeField]
        private HealthComponent healthComponent;

        [SerializeField]
        private CombatantMarker combatantMarker;

        [SerializeField]
        private EnemyBrain enemyBrain;

        [SerializeField]
        private EnemyMeleeCombat enemyMeleeCombat;

        [SerializeField]
        private EnemyAnimationDriver animationDriver;

        [SerializeField]
        private Animator targetAnimator;

        [SerializeField]
        private DamageService damageService;

        [Header("死亡設定")]
        [Tooltip("Death状態がこのnormalized timeに到達したら除去します。")]
        [SerializeField, Range(MinimumCompletionNormalizedTime, MaximumCompletionNormalizedTime)]
        private float deathCompletionNormalizedTime = 0.95f;

        [Tooltip("AnimatorがDeath状態を報告できない場合の安全な待機時間です。")]
        [SerializeField, Min(MinimumFallbackDuration)]
        private float deathFallbackDuration = 1.5f;

        [Tooltip("死亡clip完了後に敵GameObjectを非アクティブ化します。")]
        [SerializeField]
        private bool disableAfterRemoval = true;

        private bool subscribed;
        private bool deathStarted;
        private bool removalCompleted;
        private double deathStartedTime;
        private bool unregisterAttempted;
        private bool missingReferenceDiagnosticReported;

        /// <summary>死亡アニメーションの再生を開始済みかを返します。</summary>
        public bool IsInDeathTransition => deathStarted && !removalCompleted;

        /// <summary>死亡clip完了後の除去済み状態です。</summary>
        public bool IsRemoved => removalCompleted;

        /// <summary>最後に記録した日本語診断です。</summary>
        public string LastDiagnostic { get; private set; } = string.Empty;

        /// <summary>死亡開始、除去完了、診断の通知です。</summary>
        public event Action DeathStarted;
        public event Action Removed;
        public event Action<string> DiagnosticReported;

        private void Awake()
        {
            ResolveReferences();
            ClampConfiguration();
            ValidateConfiguration();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeToDependencies();
            RegisterCombatant();
        }

        private void OnDisable()
        {
            UnsubscribeFromDependencies();
        }

        private void Update()
        {
            if (!deathStarted || removalCompleted)
            {
                return;
            }

            if (HasDeathClipCompleted() || HasFallbackDeathDurationElapsed())
            {
                CompleteDeathAnimation();
            }
        }

        private void OnValidate()
        {
            ClampConfiguration();
        }

        /// <summary>
        /// HealthComponentの死亡通知から死亡遷移を開始します。二重開始は無視します。
        /// </summary>
        public bool BeginDeathTransition()
        {
            if (removalCompleted || deathStarted)
            {
                return false;
            }

            deathStarted = true;
            deathStartedTime = CurrentGameTime;
            enemyMeleeCombat?.CancelAttack();
            enemyBrain?.BeginDeathTransition();
            animationDriver?.TriggerDeath();
            DeathStarted?.Invoke();
            return true;
        }

        /// <summary>
        /// Death clipのAnimator eventから除去完了を明示します。
        /// </summary>
        public bool AnimationEventCompleteDeath()
        {
            return CompleteDeathAnimation();
        }

        /// <summary>
        /// Death clip完了後に活動登録簿、Health、EnemyBrainを順にRemovedへ遷移させます。
        /// </summary>
        public bool CompleteDeathAnimation()
        {
            if (removalCompleted || !deathStarted)
            {
                return false;
            }

            removalCompleted = true;
            enemyMeleeCombat?.CancelAttack();
            enemyBrain?.CompleteDeathTransition();

            // 先に活動中の戦闘登録を解除し、HealthのRemoved通知を受けるGameFlowと
            // 同じ敵を二重に解除しないようにします。
            UnregisterCombatant();

            if (healthComponent != null && healthComponent.State == HealthState.DeathTransition)
            {
                healthComponent.CompleteDeath(out string healthDiagnostic);
                if (!string.IsNullOrEmpty(healthDiagnostic))
                {
                    ReportDiagnostic(healthDiagnostic, false);
                }
            }

            Removed?.Invoke();

            if (disableAfterRemoval && gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            return true;
        }

        /// <summary>テスト用に依存関係を明示的に差し替えます。</summary>
        public void ConfigureForTests(
            HealthComponent health,
            CombatantMarker marker,
            EnemyBrain brain,
            EnemyMeleeCombat melee,
            EnemyAnimationDriver driver,
            Animator animator,
            DamageService damage)
        {
            UnsubscribeFromDependencies();
            healthComponent = health;
            combatantMarker = marker;
            enemyBrain = brain;
            enemyMeleeCombat = melee;
            animationDriver = driver;
            targetAnimator = animator;
            damageService = damage;
            SubscribeToDependencies();
        }

        private void HandleHealthDied()
        {
            BeginDeathTransition();
        }

        private void HandleHealthStateChanged(HealthState nextState)
        {
            if (nextState == HealthState.DeathTransition)
            {
                BeginDeathTransition();
            }
        }

        private void SubscribeToDependencies()
        {
            if (subscribed)
            {
                return;
            }

            if (healthComponent != null)
            {
                healthComponent.Died += HandleHealthDied;
                healthComponent.StateChanged += HandleHealthStateChanged;
            }

            subscribed = true;
        }

        private void UnsubscribeFromDependencies()
        {
            if (!subscribed)
            {
                return;
            }

            if (healthComponent != null)
            {
                healthComponent.Died -= HandleHealthDied;
                healthComponent.StateChanged -= HandleHealthStateChanged;
            }

            subscribed = false;
        }

        private void ResolveReferences()
        {
            if (healthComponent == null)
            {
                healthComponent = GetComponent<HealthComponent>();
            }

            if (combatantMarker == null)
            {
                combatantMarker = GetComponent<CombatantMarker>();
            }

            if (enemyBrain == null)
            {
                enemyBrain = GetComponent<EnemyBrain>();
            }

            if (enemyMeleeCombat == null)
            {
                enemyMeleeCombat = GetComponent<EnemyMeleeCombat>();
            }

            if (animationDriver == null)
            {
                animationDriver = GetComponent<EnemyAnimationDriver>();
            }

            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }

            if (damageService == null)
            {
                damageService = FindAnyObjectByType<DamageService>();
            }
        }

        private void RegisterCombatant()
        {
            if (combatantMarker == null || damageService == null || !combatantMarker.IsIdentityValid)
            {
                return;
            }

            damageService.RegisterCombatant(combatantMarker);
        }

        private void UnregisterCombatant()
        {
            if (unregisterAttempted)
            {
                return;
            }

            unregisterAttempted = true;
            if (damageService != null && combatantMarker != null && damageService.CombatantRegistry.IsRegistered(combatantMarker))
            {
                damageService.UnregisterCombatant(combatantMarker);
            }
        }

        private bool HasDeathClipCompleted()
        {
            if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
            {
                return false;
            }

            AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.IsName("Death") && stateInfo.normalizedTime >= deathCompletionNormalizedTime;
        }

        private bool HasFallbackDeathDurationElapsed()
        {
            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName("Death"))
                {
                    return false;
                }
            }

            return CurrentGameTime - deathStartedTime >= deathFallbackDuration;
        }

        private double CurrentGameTime => damageService != null && damageService.Clock != null
            ? damageService.Clock.Now
            : Time.timeAsDouble;

        private void ClampConfiguration()
        {
            deathCompletionNormalizedTime = Mathf.Clamp(
                deathCompletionNormalizedTime,
                MinimumCompletionNormalizedTime,
                MaximumCompletionNormalizedTime);
            deathFallbackDuration = Mathf.Max(MinimumFallbackDuration, deathFallbackDuration);
        }

        private void ValidateConfiguration()
        {
            if (healthComponent == null)
            {
                ReportDiagnostic("EnemyLifecycleにHealthComponent参照がありません。", true);
            }

            if (combatantMarker == null)
            {
                ReportDiagnostic("EnemyLifecycleにCombatantMarker参照がありません。", true);
            }

            if (enemyBrain == null)
            {
                ReportDiagnostic("EnemyLifecycleにEnemyBrain参照がありません。", true);
            }

            if (enemyMeleeCombat == null)
            {
                ReportDiagnostic("EnemyLifecycleにEnemyMeleeCombat参照がありません。", true);
            }

            if (animationDriver == null)
            {
                ReportDiagnostic("EnemyLifecycleにEnemyAnimationDriver参照がありません。", true);
            }

            if (damageService == null)
            {
                ReportDiagnostic("EnemyLifecycleにDamageService参照がありません。", true);
            }
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
                Debug.LogError($"[敵ライフサイクル診断] {message}", this);
            }
            else
            {
                Debug.Log($"[敵ライフサイクル診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
