using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    public interface IGameplayStateProvider
    {
        GameplayState CurrentState { get; }
    }

    /// <summary>
    /// プレイヤーの攻撃入力、コンボ遷移、攻撃判定ウィンドウおよびダメージ送出を制御します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InputReader))]
    [RequireComponent(typeof(PlayerAnimationDriver))]
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(CombatantMarker))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class PlayerCombatController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private InputReader inputReader;
        [SerializeField] private PlayerAnimationDriver animationDriver;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private CombatantMarker combatantMarker;
        [SerializeField] private HealthComponent healthComponent;
        [SerializeField] private CombatHitbox swordHitbox;
        [SerializeField] private FirstPersonViewmodelController viewmodelController;

        [Header("攻撃設定")]
        [SerializeField] private AttackConfig attackConfig;
        [SerializeField] private GameplayState fallbackGameplayState = GameplayState.Running;

        [Header("タイミング設定")]
        [SerializeField, Min(0f)] private float attackBufferDuration = 0.25f;
        [SerializeField, Min(0.1f)] private double attackAnimationFallbackDuration = 1.0;

        private GameFlowController gameFlowController;
        private DamageService damageService;
        private CombatFeedbackController feedbackController;

        private AttackWindowTracker attackWindowTracker;
        private AttackSequence attackSequence;
        private bool dead;
        private bool combatantRegistered;
        private int nextAttackSequenceId;
        private bool attackAnimationObserved;
        private double attackAnimationStartedTime;
        private int comboIndex;
        private int activeAttackComboIndex;
        private double comboExpirationTime;
        private float attackBufferTimer;

        public event Action<int> AttackSequenceStarted;
        public event Action<int> AttackSequenceCompleted;
        public event Action<int> AttackSequenceCancelled;
        public event Action<CombatantMarker, int> HitCandidateAccepted;

        public InputReader InputReader => inputReader;
        public PlayerAnimationDriver AnimationDriver => animationDriver;
        public Animator TargetAnimator => targetAnimator;
        public CombatantMarker CombatantMarker => combatantMarker;
        public HealthComponent HealthComponent => healthComponent;
        public GameFlowController GameFlowController => gameFlowController;
        public DamageService DamageService => damageService;
        public CombatHitbox SwordHitbox => swordHitbox;
        public FirstPersonViewmodelController ViewmodelController => viewmodelController;
        public PlayerController PlayerController => playerController;
        public AttackSequence CurrentAttackSequence => attackSequence;

        public bool IsAttacking => attackSequence != null && attackSequence.IsActive;
        public bool IsDead => dead || !healthComponent.IsAlive;
        public int LastAttackSequenceId { get; private set; }
        public int AttackTriggerCount { get; private set; }
        public int ComboIndex => comboIndex;
        public GameplayState CurrentGameplayState => gameFlowController.CurrentState;
        public float AttackBufferTimer => attackBufferTimer;
        public bool HasBufferedAttack => attackBufferTimer > 0f;
        public AttackConfig AttackConfig
        {
            get => attackConfig;
            set => attackConfig = value;
        }

        private AttackConfigStep CurrentStep
        {
            get
            {
                if (attackConfig is ComboAttackConfig comboConfig && comboConfig.StepCount > 0)
                {
                    return comboConfig.GetStep(IsAttacking ? activeAttackComboIndex : comboIndex);
                }

                return new AttackConfigStep
                {
                    Damage = attackConfig.AttackDamage,
                    Range = attackConfig.AttackRange,
                    SpeedMultiplier = attackConfig.AttackSpeedMultiplier,
                    WindowOpenNormalizedTime = attackConfig.AttackWindowOpenNormalizedTime,
                    WindowCloseNormalizedTime = attackConfig.AttackWindowCloseNormalizedTime,
                    CompletionNormalizedTime = attackConfig.AttackCompletionNormalizedTime
                };
            }
        }

        public float AttackSpeedMultiplier => CurrentStep.SpeedMultiplier;
        public float AttackRange => CurrentStep.Range;
        public float AttackDamage => CurrentStep.Damage;
        public float AttackCompletionNormalizedTime => CurrentStep.CompletionNormalizedTime;

        public void SetViewmodelController(FirstPersonViewmodelController controller) => viewmodelController = controller;
        public void BufferAttack(float duration = 0f) => attackBufferTimer = duration > 0f ? duration : attackBufferDuration;
        public void ClearBuffer() => attackBufferTimer = 0f;
        internal void SetFallbackGameplayState(GameplayState state) => fallbackGameplayState = state;

        [Inject]
        public void Construct(
            DamageService damageService,
            GameFlowController gameFlowController,
            CombatFeedbackController feedbackController = null)
        {
            this.damageService = damageService;
            this.gameFlowController = gameFlowController;
            this.feedbackController = feedbackController;
            RegisterCombatant();
        }

        public void ConstructForTesting(
            DamageService damageService,
            GameFlowController gameFlowController,
            InputReader inputReader = null,
            PlayerAnimationDriver animationDriver = null,
            Animator targetAnimator = null,
            CombatantMarker combatantMarker = null,
            CombatHitbox swordHitbox = null,
            PlayerController playerController = null,
            HealthComponent healthComponent = null,
            FirstPersonViewmodelController viewmodelController = null)
        {
            this.damageService = damageService;
            this.gameFlowController = gameFlowController;
            if (inputReader != null) this.inputReader = inputReader;
            if (animationDriver != null) this.animationDriver = animationDriver;
            if (targetAnimator != null) this.targetAnimator = targetAnimator;
            if (combatantMarker != null) this.combatantMarker = combatantMarker;
            if (swordHitbox != null) this.swordHitbox = swordHitbox;
            if (playerController != null) this.playerController = playerController;
            if (healthComponent != null) this.healthComponent = healthComponent;
            if (viewmodelController != null) this.viewmodelController = viewmodelController;

            SetupAttackSequence();
            RegisterCombatant();
        }

        private void Awake()
        {
            inputReader = GetComponent<InputReader>();
            animationDriver = GetComponent<PlayerAnimationDriver>();
            playerController = GetComponent<PlayerController>();
            combatantMarker = GetComponent<CombatantMarker>();
            healthComponent = GetComponent<HealthComponent>();
            SetupAttackSequence();
        }

        private void OnEnable()
        {
            RegisterCombatant();
        }

        private void Start()
        {
            RegisterCombatant();
            SetupAttackSequence();
        }

        private void OnDisable()
        {
            CancelAttack();
        }

        private void OnDestroy()
        {
            attackSequence = null;
            attackWindowTracker = null;
        }

        private void Update()
        {
            double now = Time.timeAsDouble;
            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();

            if (snapshot.AttackPressed)
            {
                attackBufferTimer = attackBufferDuration;
            }
            else if (attackBufferTimer > 0f)
            {
                attackBufferTimer -= Time.deltaTime;
            }

            if (!IsAttacking && comboExpirationTime > 0d && now >= comboExpirationTime)
            {
                ResetCombo();
            }

            bool startedThisFrame = false;
            if (!IsAttacking && attackBufferTimer > 0f)
            {
                Result res = StartAttack();
                if (res.IsOk)
                {
                    attackBufferTimer = 0f;
                    startedThisFrame = true;
                }
                else if (res.Error == GameError.TargetDead || res.Error == GameError.InvalidState)
                {
                    attackBufferTimer = 0f;
                }
            }

            TickAttackAnimation();

            // 後隙（リカバリー）期間中に攻撃ボタン長押しで次段（または初段へのループ）を自動継続
            bool inRecovery = !IsAttacking && comboExpirationTime > 0d && now < comboExpirationTime;
            if (!startedThisFrame && inRecovery && snapshot.AttackHeld)
            {
                if (StartAttack().IsOk)
                {
                    attackBufferTimer = 0f;
                }
            }
        }

        public Result ProcessInput(GameplayInputSnapshot snapshot)
        {
            if (!snapshot.AttackPressed) return GameError.ActionNotRequested;
            return StartAttack();
        }

        public Result StartAttack()
        {
            if (CurrentGameplayState != GameplayState.Running)
            {
                return GameError.StateAlreadyTerminal;
            }

            if (IsDead)
            {
                return GameError.TargetDead;
            }

            if (IsAttacking)
            {
                return GameError.ActionInProgress;
            }

            bool isComboChaining = comboExpirationTime > 0d && Time.timeAsDouble < comboExpirationTime;
            bool isStillRecovering = viewmodelController.isActiveAndEnabled
                ? viewmodelController.IsAttacking
                : animationDriver.IsInAttackState();

            if (isActiveAndEnabled && comboIndex == 0 && !isComboChaining && isStillRecovering)
            {
                return GameError.RecoveryInProgress;
            }

            if (comboExpirationTime > 0d && Time.timeAsDouble >= comboExpirationTime)
            {
                ResetCombo();
            }

            int sequenceId = ++nextAttackSequenceId;
            Result startResult = attackSequence.StartSequence(sequenceId);
            if (startResult.IsErr) return startResult;

            activeAttackComboIndex = comboIndex;
            LastAttackSequenceId = sequenceId;
            attackAnimationObserved = false;
            attackAnimationStartedTime = Time.timeAsDouble;

            swordHitbox.SetWindowTracker(attackWindowTracker);
            swordHitbox.ResetForNewSequence();

            AttackConfigStep step = CurrentStep;
            attackWindowTracker.AttackRange = step.Range;

            float openTime = step.WindowOpenNormalizedTime;
            float closeTime = step.WindowCloseNormalizedTime;
            attackSequence.ConfigureTiming(closeTime, openTime);

            animationDriver.SetAttackSpeedMultiplier(step.SpeedMultiplier);
            animationDriver.SetComboIndex(comboIndex);

            SetAnimatorComboIndex(comboIndex);
            targetAnimator.SetTrigger("AttackTrigger");

            viewmodelController.TriggerAttack(comboIndex, step.SpeedMultiplier, openTime, closeTime);
            AttackTriggerCount++;
            AttackSequenceStarted?.Invoke(sequenceId);
            feedbackController?.PlayAttackWhoosh();
            return Result.Ok();
        }

        public Result CompleteAttack()
        {
            if (!IsAttacking) return GameError.InvalidState;

            Result completeResult = attackSequence.Complete();
            if (completeResult.IsErr) return completeResult;

            int sequenceId = LastAttackSequenceId;
            attackAnimationObserved = false;
            attackAnimationStartedTime = 0d;
            animationDriver.ClearAttackSpeedMultiplier();

            if (attackConfig is ComboAttackConfig comboConfig && comboConfig.StepCount > 0)
            {
                comboIndex = (activeAttackComboIndex + 1) % comboConfig.StepCount;
                comboExpirationTime = Time.timeAsDouble + comboConfig.ComboResetTimeout;
                animationDriver.SetComboIndex(comboIndex);
                SetAnimatorComboIndex(comboIndex);
            }
            else
            {
                comboIndex = 0;
                comboExpirationTime = 0d;
            }

            AttackSequenceCompleted?.Invoke(sequenceId);
            return Result.Ok();
        }

        public void CancelAttack()
        {
            ClearBuffer();
            ResetCombo();
            if (!IsAttacking) return;

            int sequenceId = LastAttackSequenceId;
            attackSequence.Cancel();
            attackAnimationObserved = false;
            attackAnimationStartedTime = 0d;
            animationDriver.ClearAttackSpeedMultiplier();
            playerController.CancelLunge();
            viewmodelController.CancelAttack();
            AttackSequenceCancelled?.Invoke(sequenceId);
        }

        internal void ResetCombo()
        {
            comboIndex = 0;
            activeAttackComboIndex = 0;
            comboExpirationTime = 0d;
            viewmodelController.CancelAttack();
            animationDriver.SetComboIndex(0);
            SetAnimatorComboIndex(0);
        }

        private void SetAnimatorComboIndex(int index)
        {
            targetAnimator.SetInteger("ComboIndex", index);
        }

        public void SetDead(bool value)
        {
            dead = value;
            if (dead)
            {
                ClearBuffer();
                CancelAttack();
            }
        }

        internal void TickAttackAnimation()
        {
            if (!IsAttacking) return;

            float normalizedTime = 0f;
            bool hasNormalizedTime = false;

            if (viewmodelController.isActiveAndEnabled)
            {
                Result<float> vmResult = viewmodelController.GetAttackNormalizedTime();
                if (vmResult.IsOk)
                {
                    normalizedTime = vmResult.Value;
                    hasNormalizedTime = true;
                }
            }
            else
            {
                Result<float> animResult = animationDriver.GetAttackNormalizedTime();
                if (animResult.IsOk)
                {
                    normalizedTime = animResult.Value;
                    hasNormalizedTime = true;
                }
            }

            if (hasNormalizedTime)
            {
                attackAnimationObserved = true;
                attackSequence.Tick(normalizedTime);
                if (normalizedTime >= AttackCompletionNormalizedTime)
                {
                    CompleteAttack();
                }
                return;
            }

            if (attackAnimationObserved)
            {
                CompleteAttack();
                return;
            }

            if (Time.timeAsDouble - attackAnimationStartedTime >= attackAnimationFallbackDuration)
            {
                CompleteAttack();
            }
        }

        private void SetupAttackSequence()
        {
            if (attackSequence != null) return;

            float effectiveRange = AttackRange;
            attackWindowTracker = new AttackWindowTracker(combatantMarker, effectiveRange);
            float fallbackCloseTime = CurrentStep.WindowCloseNormalizedTime;
            float fallbackOpenTime = CurrentStep.WindowOpenNormalizedTime;
            attackSequence = new AttackSequence(attackWindowTracker, fallbackCloseTime);
            attackSequence.ConfigureTiming(fallbackCloseTime, fallbackOpenTime);
            attackWindowTracker.TargetRegistered += HandleTargetRegistered;
            swordHitbox.SetWindowTracker(attackWindowTracker);
        }

        private void HandleTargetRegistered(CombatantMarker target, int sequenceId)
        {
            if (target == combatantMarker) return;

            HitCandidateAccepted?.Invoke(target, sequenceId);
            if (CurrentGameplayState != GameplayState.Running) return;

            damageService.Submit(
                combatantMarker,
                target,
                AttackDamage,
                sequenceId,
                AttackKinds.KnightSword,
                attackWindowTracker,
                target.transform.position);
        }

        private void RegisterCombatant()
        {
            if (combatantRegistered) return;

            damageService.RegisterCombatant(combatantMarker);
            combatantRegistered = true;
        }

        // --- Animator Event Bridges ---
        public void AnimationEventBeginAttackWindow()
        {
            if (IsAttacking)
            {
                attackSequence.OnAttackWindowOpenEvent();
            }
        }

        public void AnimationEventEndAttackWindow() => attackSequence.OnAttackWindowCloseEvent();
        public void AnimationEventCompleteAttack() => CompleteAttack();

        public void OnAnimationEvent_BeginAttackWindow() => AnimationEventBeginAttackWindow();
        public void OnAnimationEvent_EndAttackWindow() => AnimationEventEndAttackWindow();
        public void OnAnimationEvent_CompleteAttack() => AnimationEventCompleteAttack();
    }
}
