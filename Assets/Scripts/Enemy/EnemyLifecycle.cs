using System;
using UnityEngine;
using VContainer;

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

        private DamageService damageService;

        [Header("死亡設定")]
        [Tooltip("Death状態がこのnormalized timeに到達したら除去します。")]
        [SerializeField, Range(0f, 1f)]
        private float deathCompletionNormalizedTime;

        [Tooltip("AnimatorがDeath状態を報告できない場合の安全な待機時間です。")]
        [SerializeField, Min(0f)]
        private float deathFallbackDuration;

        [Tooltip("死亡clip完了後に敵GameObjectを非アクティブ化します。")]
        [SerializeField]
        private bool disableAfterRemoval = true;

        private bool subscribed;
        private bool deathStarted;
        private bool removalCompleted;
        private double deathStartedTime;
        private bool unregisterAttempted;
        /// <summary>死亡アニメーションの再生を開始済みかを返します。</summary>
        public bool IsInDeathTransition => deathStarted && !removalCompleted;

        /// <summary>死亡clip完了後の除去済み状態です。</summary>
        public bool IsRemoved => removalCompleted;

        /// <summary>死亡開始、除去完了の通知です。</summary>
        public event Action DeathStarted;
        public event Action Removed;

        [Inject]
        public void Construct(DamageService damage = null, SceneReferenceRegistry registry = null)
        {
            if (damage != null) damageService = damage;
            else if (registry != null && registry.DamageService != null) damageService = registry.DamageService;
        }

        private void Awake()
        {
            ClampConfiguration();
            healthComponent = GetComponent<HealthComponent>();
            combatantMarker = GetComponent<CombatantMarker>();
            enemyBrain = GetComponent<EnemyBrain>();
            enemyMeleeCombat = GetComponent<EnemyMeleeCombat>();
            animationDriver = GetComponent<EnemyAnimationDriver>();
            targetAnimator = GetComponentInChildren<Animator>();

            UnityEngine.Assertions.Assert.IsNotNull(healthComponent, "EnemyLifecycle: HealthComponentコンポーネントが必要です。");
            UnityEngine.Assertions.Assert.IsNotNull(combatantMarker, "EnemyLifecycle: CombatantMarkerコンポーネントが必要です。");
            UnityEngine.Assertions.Assert.IsNotNull(enemyBrain, "EnemyLifecycle: EnemyBrainコンポーネントが必要です。");
            UnityEngine.Assertions.Assert.IsNotNull(enemyMeleeCombat, "EnemyLifecycle: EnemyMeleeCombatコンポーネントが必要です。");

            SubscribeToDependencies();
            RegisterCombatant();
            enabled = false;
        }

        private void OnDestroy()
        {
            UnsubscribeFromDependencies();
            UnregisterCombatant();
        }

        private void Update()
        {
            if (!deathStarted || removalCompleted)
            {
                enabled = false;
                return;
            }

            AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
            bool isDeathState = stateInfo.IsName("Death");
            bool completed = (isDeathState && stateInfo.normalizedTime >= deathCompletionNormalizedTime) ||
                             (!isDeathState && (CurrentGameTime - deathStartedTime >= deathFallbackDuration));

            if (completed)
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
        public Result BeginDeathTransition()
        {
            if (removalCompleted || deathStarted)
            {
                return GameError.AlreadyExecuted;
            }

            deathStarted = true;
            deathStartedTime = CurrentGameTime;
            enemyMeleeCombat?.CancelAttack();
            enemyBrain?.BeginDeathTransition();
            animationDriver?.TriggerDeath();
            DeathStarted?.Invoke();
            enabled = true;
            return Result.Ok();
        }

        /// <summary>
        /// Death clipのAnimator eventから除去完了を明示します。
        /// </summary>
        public Result AnimationEventCompleteDeath()
        {
            return CompleteDeathAnimation();
        }

        /// <summary>
        /// Death clip完了後に活動登録簿、Health、EnemyBrainを順にRemovedへ遷移させます。
        /// </summary>
        public Result CompleteDeathAnimation()
        {
            if (removalCompleted || !deathStarted)
            {
                return GameError.InvalidState;
            }

            removalCompleted = true;
            enemyMeleeCombat?.CancelAttack();
            enemyBrain?.CompleteDeathTransition();

            // 先に活動中の戦闘登録を解除し、HealthのRemoved通知を受けるGameFlowと
            // 同じ敵を二重に解除しないようにします。
            UnregisterCombatant();

            if (healthComponent != null && healthComponent.State == HealthState.DeathTransition)
            {
                healthComponent.CompleteDeath();
            }

            Removed?.Invoke();

            if (disableAfterRemoval && gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            enabled = false;
            return Result.Ok();
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


        private double CurrentGameTime => damageService != null && damageService.Clock != null
            ? damageService.Clock.Now
            : Time.timeAsDouble;

        private void ClampConfiguration()
        {
            deathCompletionNormalizedTime = Mathf.Clamp01(deathCompletionNormalizedTime);
            deathFallbackDuration = Mathf.Max(0.01f, deathFallbackDuration);
        }


    }
}
