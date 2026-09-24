using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// GameFlowControllerから攻撃可否を読み取るための契約です。
    /// </summary>
    public interface IGameplayStateProvider
    {
        GameplayState CurrentState { get; }
    }

    /// <summary>
    /// 左クリックを含むGameplay入力を一回のAttackSequenceへ変換します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatController : MonoBehaviour
    {

        [Header("参照")]
        [SerializeField]
        private InputReader inputReader;

        [SerializeField]
        private PlayerAnimationDriver animationDriver;

        [SerializeField]
        private PlayerController playerController;

        [SerializeField]
        private Animator targetAnimator;

        [SerializeField]
        private CombatantMarker combatantMarker;

        [SerializeField]
        private HealthComponent healthComponent;

        private GameFlowController gameFlowController;
        private DamageService damageService;

        [SerializeField]
        private CombatHitbox swordHitbox;

        [SerializeField]
        private FirstPersonViewmodelController viewmodelController;

        [Header("攻撃設定")]
        [Tooltip("攻撃動作の数値設定アセットです。ダメージ、射程、前後揺・判定タイミング、速度倍率の唯一の真実性源泉（Single Source of Truth）です。")]
        [SerializeField]
        private AttackConfig attackConfig;

        [SerializeField]
        private GameplayState fallbackGameplayState = GameplayState.Running;

        [Header("先行入力（Input Buffer）")]
        [Tooltip("攻撃ボタンの先行入力有効時間（秒）です。")]
        [SerializeField, Min(0f)]
        private float attackBufferDuration;

        [Header("フォールバック設定")]
        [Tooltip("アニメーション完了イベントが通知されない場合の安全フォールバック待機時間（秒）です。")]
        [SerializeField, Min(0.1f)]
        private double attackAnimationFallbackDuration;

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
        public void SetViewmodelController(FirstPersonViewmodelController controller) => viewmodelController = controller;
        public AttackSequence CurrentAttackSequence => attackSequence;
        public bool IsAttacking => attackSequence != null && attackSequence.IsActive;
        public bool IsDead => dead ||
            (healthComponent != null && !healthComponent.IsAlive) ||
            combatantMarker == null ||
            !combatantMarker.IsAvailableForCombat;
        public int LastAttackSequenceId { get; private set; }
        public int AttackTriggerCount { get; private set; }
        public int ComboIndex => comboIndex;
        public GameplayState CurrentGameplayState => gameFlowController != null ? gameFlowController.CurrentState : fallbackGameplayState;
        public PlayerController PlayerController => playerController;

        private AttackConfigStep CurrentStep
        {
            get
            {
                if (attackConfig is ComboAttackConfig comboConfig && comboConfig.StepCount > 0)
                {
                    return comboConfig.GetStep(IsAttacking ? activeAttackComboIndex : comboIndex);
                }

                if (attackConfig != null)
                {
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

                return new AttackConfigStep
                {
                    Damage = attackConfig != null ? attackConfig.AttackDamage : 0f,
                    Range = attackConfig != null ? attackConfig.AttackRange : 0f,
                    SpeedMultiplier = attackConfig != null ? attackConfig.AttackSpeedMultiplier : 1f,
                    WindowOpenNormalizedTime = attackConfig != null ? attackConfig.AttackWindowOpenNormalizedTime : 0f,
                    WindowCloseNormalizedTime = attackConfig != null ? attackConfig.AttackWindowCloseNormalizedTime : 0f,
                    CompletionNormalizedTime = attackConfig != null ? attackConfig.AttackCompletionNormalizedTime : 0f
                };
            }
        }

        public float AttackSpeedMultiplier => CurrentStep.SpeedMultiplier;

        public AttackConfig AttackConfig
        {
            get => attackConfig;
            set => attackConfig = value;
        }

        public float AttackRange => CurrentStep.Range;
        public float AttackDamage => CurrentStep.Damage;
        public float AttackCompletionNormalizedTime => CurrentStep.CompletionNormalizedTime;
        private float attackBufferTimer;

        public float AttackBufferTimer => attackBufferTimer;
        public bool HasBufferedAttack => attackBufferTimer > 0f;
        public void BufferAttack(float duration = 0f) => attackBufferTimer = duration > 0f ? duration : attackBufferDuration;
        public void ClearBuffer() => attackBufferTimer = 0f;

        private void Awake()
        {
            SetupAttackSequence();
        }

        private void OnEnable()
        {
            RegisterCombatant();
        }

        private void Start()
        {
            RegisterCombatant();
            UnityEngine.Assertions.Assert.IsNotNull(inputReader, "PlayerCombatController: InputReader参照がありません。");
            UnityEngine.Assertions.Assert.IsNotNull(animationDriver, "PlayerCombatController: PlayerAnimationDriver参照がありません。");
            UnityEngine.Assertions.Assert.IsNotNull(targetAnimator, "PlayerCombatController: Animator参照がありません。");
            if (attackSequence == null)
            {
                SetupAttackSequence();
            }
            UnityEngine.Assertions.Assert.IsNotNull(attackSequence, "PlayerCombatController: AttackSequenceを初期化できません。");
        }

        private void OnDisable()
        {
            CancelAttack();
        }

        private void Update()
        {
            double now = Time.timeAsDouble;
            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            bool attackPressedThisFrame = snapshot.AttackPressed;
            bool attackHeldThisFrame = snapshot.AttackHeld;

            if (attackPressedThisFrame)
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
                StartAttack()
                    .Tap(() =>
                    {
                        attackBufferTimer = 0f;
                        startedThisFrame = true;
                    })
                    .TapErr(err =>
                    {
                        if (err == GameError.TargetDead || err == GameError.InvalidState)
                        {
                            attackBufferTimer = 0f;
                        }
                    });
            }

            TickAttackAnimation();

            // 後摇（recovery）期間中：IsAttacking=false でコンボ有効時間内（now < comboExpirationTime）に
            // 攻撃ボタンが押しっぱなしであれば自動的に次のコンボ段（3段目からは初段へ循環）を起動します。
            // これにより、ボタン長押しで3段攻撃が無限にループし、単発クリックは1段のみになります。
            bool inRecovery = !IsAttacking && comboExpirationTime > 0d && now < comboExpirationTime;
            if (!startedThisFrame && inRecovery && attackHeldThisFrame)
            {
                StartAttack()
                    .Tap(() => attackBufferTimer = 0f);
            }
        }

        private void OnDestroy()
        {
            attackSequence = null;
            attackWindowTracker = null;
        }

        /// <summary>
        /// 入力スナップショットを攻撃開始へ変換します。テストと実行時の入口を同一に保ちます。
        /// </summary>
        public Result ProcessInput(GameplayInputSnapshot snapshot)
        {
            if (!snapshot.AttackPressed)
            {
                return GameError.ActionNotRequested;
            }

            return StartAttack();
        }

        /// <summary>
        /// Running中、非攻撃中、非死亡時だけ新しい系列を開始します。
        /// </summary>
        public Result StartAttack()
        {
            if (CurrentGameplayState != GameplayState.Running)
            {
                LastDiagnostic = "終局状態のため攻撃入力を無視しました。";
                return GameError.StateAlreadyTerminal;
            }

            if (IsDead)
            {
                LastDiagnostic = "死亡状態のため攻撃入力を無視しました。";
                return GameError.TargetDead;
            }

            if (IsAttacking)
            {
                LastDiagnostic = $"攻撃系列{LastAttackSequenceId}が進行中のため、再入力を無視しました。";
                return GameError.ActionInProgress;
            }

            // コンボ進行中（次の段へ進む場合、および3段目から初段へループする場合）は直前の攻撃からの遷移を許可します。
            // アイドル状態からの初段（comboIndex == 0 かつ直前のコンボ継続中ではない）開始時のみ、前回の攻撃動作復帰完了まで入力を受け付けません。
            // Viewmodelがある場合はViewmodelの進行度を真実源泉とします。
            // Viewmodelがない場合はAnimatorのステート情報にフォールバックします。
            bool isComboChaining = comboExpirationTime > 0d && Time.timeAsDouble < comboExpirationTime;
            bool isStillRecovering = viewmodelController != null && viewmodelController.isActiveAndEnabled
                ? viewmodelController.IsAttacking
                : (animationDriver != null && animationDriver.IsInAttackState());
            if (isActiveAndEnabled && comboIndex == 0 && !isComboChaining && isStillRecovering)
            {
                LastDiagnostic = $"攻撃系列{LastAttackSequenceId}の動作復帰中のため、再入力を無視しました。";
                return GameError.RecoveryInProgress;
            }

            if (comboExpirationTime > 0d && Time.timeAsDouble >= comboExpirationTime)
            {
                ResetCombo();
            }

            int sequenceId = ++nextAttackSequenceId;
            Result startResult = attackSequence.StartSequence(sequenceId);
            if (startResult.IsErr)
            {
                return startResult;
            }

            activeAttackComboIndex = comboIndex;
            LastAttackSequenceId = sequenceId;
            attackAnimationObserved = false;
            attackAnimationStartedTime = Time.timeAsDouble;
            swordHitbox.SetWindowTracker(attackWindowTracker);
            swordHitbox.ResetForNewSequence();

            AttackConfigStep step = CurrentStep;
            if (attackWindowTracker != null)
            {
                attackWindowTracker.AttackRange = step.Range;
            }

            // 攻撃有効ウィンドウの開閉タイミングを現在のコンボ段に合わせて設定します。
            // 収刀時（progress >= closeTime）にダメージが残留しないよう、出刀の打撃フェーズに限定します。
            float openTime = step.WindowOpenNormalizedTime;
            float closeTime = step.WindowCloseNormalizedTime;
            attackSequence.ConfigureTiming(closeTime, openTime);

            animationDriver.SetAttackSpeedMultiplier(step.SpeedMultiplier);
            animationDriver.SetComboIndex(comboIndex);

            SetAnimatorComboIndex(comboIndex);
            targetAnimator.SetTrigger("AttackTrigger");

            viewmodelController?.TriggerAttack(comboIndex, step.SpeedMultiplier, openTime, closeTime);
            AttackTriggerCount++;
            AttackSequenceStarted?.Invoke(sequenceId);
            return Result.Ok();
        }

        /// <summary>Animatorの攻撃開始イベントから攻撃有効ウィンドウを開きます。</summary>
        public Result AnimationEventBeginAttackWindow()
        {
            if (!IsAttacking)
            {
                return GameError.InvalidState;
            }

            return attackSequence.OnAttackWindowOpenEvent()
                .LogIfErr(this, "[PlayerCombat] 攻撃ウィンドウ開始拒絶");
        }

        /// <summary>Animatorの攻撃終了イベントから攻撃有効ウィンドウを閉じます。</summary>
        public Result AnimationEventEndAttackWindow()
        {
            return attackSequence.OnAttackWindowCloseEvent()
                .LogIfErr(this, "[PlayerCombat] 攻撃ウィンドウ終了拒絶");
        }

        /// <summary>Animatorの攻撃完了イベントから系列を完了します。</summary>
        public Result AnimationEventCompleteAttack()
        {
            return CompleteAttack()
                .LogIfErr(this, "[PlayerCombat] 攻撃完了処理拒絶");
        }

        // --- Unity Animator Event 専用の void ブリッジ ---
        public void OnAnimationEvent_BeginAttackWindow() => AnimationEventBeginAttackWindow();
        public void OnAnimationEvent_EndAttackWindow() => AnimationEventEndAttackWindow();
        public void OnAnimationEvent_CompleteAttack() => AnimationEventCompleteAttack();

        /// <summary>
        /// Attack状態のnormalized timeが完了点へ到達したときの技術的フォールバックです。
        /// </summary>
        public Result CompleteAttack()
        {
            if (!IsAttacking)
            {
                return GameError.InvalidState;
            }

            Result completeResult = attackSequence.Complete();
            if (completeResult.IsErr)
            {
                return completeResult;
            }

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

        /// <summary>終局、無効化、アニメーション異常時に攻撃を閉じます。</summary>
        public void CancelAttack()
        {
            ClearBuffer();
            ResetCombo();
            if (!IsAttacking)
            {
                return;
            }

            int sequenceId = LastAttackSequenceId;
            attackSequence.Cancel();
            attackAnimationObserved = false;
            attackAnimationStartedTime = 0d;
            animationDriver.ClearAttackSpeedMultiplier();
            playerController?.CancelLunge();
            viewmodelController?.CancelAttack();
            AttackSequenceCancelled?.Invoke(sequenceId);
        }

        internal void ResetCombo()
        {
            comboIndex = 0;
            activeAttackComboIndex = 0;
            comboExpirationTime = 0d;
            viewmodelController?.CancelAttack();
            animationDriver.SetComboIndex(0);
            SetAnimatorComboIndex(0);
        }

        private void SetAnimatorComboIndex(int index)
        {
            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetInteger("ComboIndex", index);
            }
        }

        /// <summary>HealthComponentが死亡遷移へ入ったときに呼び出します。</summary>
        public void SetDead(bool value)
        {
            dead = value;
            if (dead)
            {
                ClearBuffer();
                CancelAttack();
            }
        }



        /// <summary>フロー状態の代替値を設定します。実行シーンではGameFlowControllerを使用します。</summary>
        internal void SetFallbackGameplayState(GameplayState state)
        {
            fallbackGameplayState = state;
        }

        /// <summary>
        /// VContainer によるシーン外部サービスの依存注入です。
        /// </summary>
        [Inject]
        public void Construct(DamageService damageService, GameFlowController gameFlowController)
        {
            this.damageService = damageService;
            this.gameFlowController = gameFlowController;
            RegisterCombatant();
        }

        /// <summary>
        /// 依存関係を明示的に注入・設定します。テストおよびモック注入で使用します。
        /// </summary>
        public void Construct(
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

        internal void TickAttackAnimation()
        {
            if (!IsAttacking)
            {
                return;
            }

            float normalizedTime = 0f;
            bool hasNormalizedTime = false;

            // 第一人称視点下では、画面上の武器出刀運動（Viewmodel）の進行度を最優先の真実性源泉とします。
            // 視口武器が出刀中（IsAttacking）であれば、その進行度（0.0〜1.0）に基づいて判定窓の開閉と完了を評価します。
            if (viewmodelController != null && viewmodelController.isActiveAndEnabled)
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

            // Attackのexit transition後にnormalized timeの最後のフレームを
            // 取り逃しても、系列を永久にActiveへ残さないようにします。
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
            if (attackSequence != null || combatantMarker == null)
            {
                return;
            }

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
            if (target == null || target == combatantMarker)
            {
                return;
            }

            HitCandidateAccepted?.Invoke(target, sequenceId);
            if (damageService == null || combatantMarker == null || CurrentGameplayState != GameplayState.Running)
            {
                return;
            }

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
            if (combatantRegistered || damageService == null || combatantMarker == null)
            {
                return;
            }

            if (damageService.CombatantRegistry == null)
            {
                return;
            }

            damageService.RegisterCombatant(combatantMarker);
            combatantRegistered = true;
        }

        public string LastDiagnostic { get; private set; } = string.Empty;
    }
}
