using System;
using System.Collections.Generic;
using UnityEngine;

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
        private const float MinimumAttackRange = 0.01f;
        private const float DefaultAttackRange = 3.0f;
        private const float DefaultAttackDamage = 25f;
        private const float DefaultAttackCompletionNormalizedTime = 0.70f;
        private const float DefaultAttackSpeedMultiplier = 1.6f;
        private const double AttackAnimationFallbackDuration = 1.5d;

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

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private DamageService damageService;

        [SerializeField]
        private CombatHitbox swordHitbox;

        [SerializeField]
        private FirstPersonViewmodelController viewmodelController;

        [Header("攻撃設定")]
        [Tooltip("攻撃動作の数値設定アセットです。未設定時は下記の個別値を使用します。")]
        [SerializeField]
        private AttackConfig attackConfig;

        [SerializeField, Min(MinimumAttackRange)]
        private float attackRange = DefaultAttackRange;

        [SerializeField, Min(MinimumAttackRange)]
        private float attackDamage = DefaultAttackDamage;

        [SerializeField, Range(0.1f, 0.99f)]
        private float attackCompletionNormalizedTime = DefaultAttackCompletionNormalizedTime;

        [SerializeField]
        private GameplayState fallbackGameplayState = GameplayState.Running;

        private AttackWindowTracker attackWindowTracker;
        private AttackSequence attackSequence;
        private bool initialized;
        private bool dead;
        private bool combatantRegistered;
        private int nextAttackSequenceId;
        private bool attackAnimationObserved;
        private double attackAnimationStartedTime;
        private int comboIndex;
        private int activeAttackComboIndex;
        private double comboExpirationTime;
        private readonly HashSet<string> reportedErrorDiagnostics = new();

        public event Action<int> AttackSequenceStarted;
        public event Action<int> AttackSequenceCompleted;
        public event Action<int> AttackSequenceCancelled;
        public event Action<CombatantMarker, int> HitCandidateAccepted;
        public event Action<string> DiagnosticReported;

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

                return new AttackConfigStep
                {
                    Damage = attackConfig != null ? attackConfig.AttackDamage : attackDamage,
                    Range = attackConfig != null ? attackConfig.AttackRange : attackRange,
                    SpeedMultiplier = attackConfig != null ? attackConfig.AttackSpeedMultiplier : DefaultAttackSpeedMultiplier,
                    WindowCloseNormalizedTime = attackConfig != null ? attackConfig.AttackWindowCloseNormalizedTime : AttackConfig.DefaultWindowCloseNormalizedTime,
                    CompletionNormalizedTime = attackConfig != null ? attackConfig.AttackCompletionNormalizedTime : DefaultAttackCompletionNormalizedTime
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
        public InputBuffer Buffer => inputHandler.Buffer;

        private readonly PlayerCombatInputHandler inputHandler = new PlayerCombatInputHandler(0.25f);

        private void Awake()
        {
            ResolveReferences();
            InitializeAttackSequence();
            RegisterCombatant();
        }

        private void OnEnable()
        {
            ResolveReferences();
            InitializeAttackSequence();
            RegisterCombatant();
        }

        private void Start()
        {
            ResolveReferences();
            InitializeAttackSequence();
            RegisterCombatant();
            ValidateRequiredReferences(out _);
        }

        private void OnDisable()
        {
            CancelAttack();
        }

        private void Update()
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            double now = Time.timeAsDouble;
            inputHandler.ProcessFrameInput(inputReader, now, out bool attackPressedThisFrame);

            if (!IsAttacking && comboIndex > 0 && now >= comboExpirationTime)
            {
                ResetCombo();
            }

            bool startedFromSnapshot = attackPressedThisFrame && TryStartAttack(out _);
            TickAttackAnimation();

            if (!startedFromSnapshot && !IsAttacking && inputHandler.ConsumeBufferedAttack(now))
            {
                TryStartAttack(out _);
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
        public bool ProcessInput(GameplayInputSnapshot snapshot)
        {
            if (!snapshot.AttackPressed)
            {
                return false;
            }

            return TryStartAttack(out _);
        }

        /// <summary>
        /// Running中、非攻撃中、非死亡時だけ新しい系列を開始します。
        /// </summary>
        public bool TryStartAttack(out string diagnostic)
        {
            if (!EnsureReferencesReady())
            {
                diagnostic = LastDiagnostic;
                return false;
            }

            if (CurrentGameplayState != GameplayState.Running)
            {
                diagnostic = "終局状態のため攻撃入力を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (IsDead)
            {
                diagnostic = "死亡状態のため攻撃入力を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (IsAttacking)
            {
                diagnostic = $"攻撃系列{LastAttackSequenceId}が進行中のため、再入力を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            // コンボ進行中（次の段へ進む場合）は直前の攻撃からの遷移を許可します。
            // 初段（comboIndex == 0）開始時は、前回の攻撃動作復帰完了まで入力を受け付けません。
            if (isActiveAndEnabled && comboIndex == 0 && animationDriver != null && animationDriver.IsInAttackState())
            {
                diagnostic = $"攻撃系列{LastAttackSequenceId}の動作復帰中のため、再入力を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (comboIndex > 0 && Time.timeAsDouble >= comboExpirationTime)
            {
                ResetCombo();
            }

            int sequenceId = ++nextAttackSequenceId;
            if (!attackSequence.StartSequence(sequenceId, out diagnostic))
            {
                return false;
            }

            activeAttackComboIndex = comboIndex;
            LastAttackSequenceId = sequenceId;
            attackAnimationObserved = false;
            attackAnimationStartedTime = Time.timeAsDouble;
            swordHitbox?.SetWindowTracker(attackWindowTracker);
            swordHitbox?.ResetForNewSequence();

            AttackConfigStep step = CurrentStep;
            attackRange = step.Range;
            attackDamage = step.Damage;
            if (attackWindowTracker != null)
            {
                attackWindowTracker.AttackRange = step.Range;
            }

            if (animationDriver != null)
            {
                animationDriver.SetAttackSpeedMultiplier(step.SpeedMultiplier);
                animationDriver.SetComboIndex(comboIndex);
            }

            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetInteger("ComboIndex", comboIndex);
            }

            if (targetAnimator != null)
            {
                targetAnimator.SetTrigger("AttackTrigger");
            }

            viewmodelController?.TriggerAttack(comboIndex, step.SpeedMultiplier);
            AttackTriggerCount++;
            AttackSequenceStarted?.Invoke(sequenceId);
            return true;
        }

        /// <summary>Animatorの攻撃開始イベントから攻撃有効ウィンドウを開きます。</summary>
        public bool AnimationEventBeginAttackWindow()
        {
            if (!IsAttacking)
            {
                return false;
            }

            return attackSequence.OnAttackWindowOpenEvent(out _);
        }

        /// <summary>Animatorの攻撃終了イベントから攻撃有効ウィンドウを閉じます。</summary>
        public bool AnimationEventEndAttackWindow()
        {
            return attackSequence != null && attackSequence.OnAttackWindowCloseEvent();
        }

        /// <summary>Animatorの攻撃完了イベントから系列を完了します。</summary>
        public bool AnimationEventCompleteAttack()
        {
            return CompleteAttack();
        }

        /// <summary>
        /// Attack状態のnormalized timeが完了点へ到達したときの技術的フォールバックです。
        /// </summary>
        public bool CompleteAttack()
        {
            if (attackSequence == null || !attackSequence.IsActive)
            {
                return false;
            }

            if (!attackSequence.Complete(out string diagnostic))
            {
                if (!string.IsNullOrEmpty(diagnostic))
                {
                    ReportDiagnostic(diagnostic, false);
                }

                return false;
            }

            int sequenceId = LastAttackSequenceId;
            attackAnimationObserved = false;
            attackAnimationStartedTime = 0d;
            animationDriver?.ClearAttackSpeedMultiplier();

            if (attackConfig is ComboAttackConfig comboConfig && comboConfig.StepCount > 0)
            {
                comboIndex = (activeAttackComboIndex + 1) % comboConfig.StepCount;
                comboExpirationTime = Time.timeAsDouble + comboConfig.ComboResetTimeout;
                animationDriver?.SetComboIndex(comboIndex);
                if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
                {
                    targetAnimator.SetInteger("ComboIndex", comboIndex);
                }
            }
            else
            {
                comboIndex = 0;
                comboExpirationTime = 0d;
            }

            AttackSequenceCompleted?.Invoke(sequenceId);
            return true;
        }

        /// <summary>終局、無効化、アニメーション異常時に攻撃を閉じます。</summary>
        public void CancelAttack()
        {
            inputHandler.Clear();
            ResetCombo();
            if (attackSequence == null || !attackSequence.IsActive)
            {
                return;
            }

            int sequenceId = LastAttackSequenceId;
            attackSequence.Cancel();
            attackAnimationObserved = false;
            attackAnimationStartedTime = 0d;
            animationDriver?.ClearAttackSpeedMultiplier();
            playerController?.CancelLunge();
            viewmodelController?.CancelAttack();
            AttackSequenceCancelled?.Invoke(sequenceId);
        }

        private void ResetCombo()
        {
            comboIndex = 0;
            activeAttackComboIndex = 0;
            comboExpirationTime = 0d;
            viewmodelController?.CancelAttack();
            animationDriver?.SetComboIndex(0);
            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetInteger("ComboIndex", 0);
            }
        }

        public void SimulateComboTimeoutForTests()
        {
            comboExpirationTime = 0d;
            ResetCombo();
        }

        /// <summary>HealthComponentが死亡遷移へ入ったときに呼び出します。</summary>
        public void SetDead(bool value)
        {
            dead = value;
            if (dead)
            {
                inputHandler.Clear();
                CancelAttack();
            }
        }

        /// <summary>
        /// Knightオブジェクト自体を検査し、PlayerCombatControllerの欠落を明示します。
        /// </summary>
        public static bool ValidateKnightObject(GameObject knight, out IReadOnlyList<string> diagnostics)
        {
            return KnightCombatValidator.ValidateKnightObject(knight, out diagnostics);
        }

        /// <summary>
        /// Knightの攻撃経路に必要な参照と日本語診断をまとめて検査します。
        /// </summary>
        public bool ValidateRequiredReferences(out IReadOnlyList<string> diagnostics)
        {
            ResolveReferences();
            InitializeAttackSequence();
            reportedErrorDiagnostics.Clear();

            bool isValid = KnightCombatValidator.ValidateRequiredReferences(this, out diagnostics);
            if (diagnostics.Count > 0)
            {
                ReportDiagnostic(diagnostics[0], true);
                for (int index = 1; index < diagnostics.Count; index++)
                {
                    PublishDiagnostic(diagnostics[index]);
                }
            }

            return isValid;
        }

        /// <summary>テスト用にフロー状態の代替値を設定します。実行シーンではGameFlowControllerを使用します。</summary>
        public void SetFallbackGameplayState(GameplayState state)
        {
            fallbackGameplayState = state;
        }

        /// <summary>テスト用に必須参照を注入します。</summary>
        public void ConfigureForTests(
            InputReader reader,
            PlayerAnimationDriver driver,
            Animator animator,
            CombatantMarker marker,
            GameFlowController flow,
            CombatHitbox hitbox,
            DamageService service = null,
            PlayerController controller = null)
        {
            inputReader = reader;
            animationDriver = driver;
            targetAnimator = animator;
            combatantMarker = marker;
            gameFlowController = flow;
            swordHitbox = hitbox;
            damageService = service;
            playerController = controller;
            ResolveReferences();
            InitializeAttackSequence();
            RegisterCombatant();
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

            if (Time.timeAsDouble - attackAnimationStartedTime >= AttackAnimationFallbackDuration)
            {
                ReportDiagnostic(
                    $"対象「{gameObject.name}」のAttack状態を検出できなかったため、攻撃系列{LastAttackSequenceId}を安全に完了しました。",
                    false);
                CompleteAttack();
            }
        }

        private bool EnsureReferencesReady()
        {
            ResolveReferences();
            InitializeAttackSequence();

            if (inputReader == null)
            {
                ReportDiagnostic("PlayerCombatControllerのInputReader参照がありません。", true);
                return false;
            }

            if (animationDriver == null)
            {
                ReportDiagnostic("PlayerCombatControllerのPlayerAnimationDriver参照がありません。", true);
                return false;
            }

            if (targetAnimator == null)
            {
                ReportDiagnostic("PlayerCombatControllerのAnimator参照がありません。", true);
                return false;
            }

            if (attackSequence == null)
            {
                ReportDiagnostic("PlayerCombatControllerのAttackSequenceを初期化できません。", true);
                return false;
            }

            return inputReader.TryInitialize();
        }

        private void ResolveReferences()
        {
            if (inputReader == null)
            {
                inputReader = GetComponent<InputReader>();
            }

            if (animationDriver == null)
            {
                animationDriver = GetComponent<PlayerAnimationDriver>();
            }

            if (playerController == null)
            {
                playerController = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            }

            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }

            if (combatantMarker == null)
            {
                combatantMarker = GetComponent<CombatantMarker>();
            }

            if (healthComponent == null)
            {
                healthComponent = GetComponent<HealthComponent>();
            }

            var registry = SceneReferenceRegistry.ActiveInstance;
            if (gameFlowController == null)
            {
                gameFlowController = registry != null && registry.GameFlowController != null
                    ? registry.GameFlowController
                    : FindAnyObjectByType<GameFlowController>();
            }

            if (damageService == null)
            {
                damageService = registry != null && registry.DamageService != null
                    ? registry.DamageService
                    : FindAnyObjectByType<DamageService>();
            }

            if (swordHitbox == null)
            {
                swordHitbox = GetComponentInChildren<CombatHitbox>(true);
            }

            if (viewmodelController == null)
            {
                viewmodelController = GetComponentInChildren<FirstPersonViewmodelController>(true);
            }
        }

        private void InitializeAttackSequence()
        {
            if (attackSequence != null || combatantMarker == null)
            {
                return;
            }

            float effectiveRange = Mathf.Max(MinimumAttackRange, AttackRange);
            float effectiveDamage = Mathf.Max(MinimumAttackRange, AttackDamage);
            attackRange = effectiveRange;
            attackDamage = effectiveDamage;
            attackWindowTracker = new AttackWindowTracker(combatantMarker, effectiveRange);
            float fallbackCloseTime = attackConfig != null ? attackConfig.AttackWindowCloseNormalizedTime : 0.55f;
            attackSequence = new AttackSequence(attackWindowTracker, fallbackCloseTime);
            attackWindowTracker.TargetRegistered += HandleTargetRegistered;
            swordHitbox?.SetWindowTracker(attackWindowTracker);
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
                ReportDiagnostic("プレイヤー攻撃のDamageService参照またはRunning状態がないため、命中を無視しました。", false);
                return;
            }

            if (damageService.Submit(
                    combatantMarker,
                    target,
                    AttackDamage,
                    sequenceId,
                    AttackKinds.KnightSword,
                    attackWindowTracker,
                    target.transform.position,
                    out string diagnostic))
            {
                DiagnosticReported?.Invoke($"攻撃系列{sequenceId}が対象「{target.CombatantId}」へダメージを送信しました。");
                return;
            }

            if (!string.IsNullOrEmpty(diagnostic))
            {
                ReportDiagnostic(diagnostic, false);
            }
        }

        private void RegisterCombatant()
        {
            if (combatantRegistered || damageService == null || combatantMarker == null)
            {
                return;
            }

            combatantRegistered = damageService.RegisterCombatant(combatantMarker);
        }

        private void PublishDiagnostic(string message)
        {
            LastDiagnostic = message;
            if (!string.IsNullOrEmpty(message))
            {
                DiagnosticReported?.Invoke(message);
            }
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (asError && !reportedErrorDiagnostics.Add(message))
            {
                return;
            }

            if (asError)
            {
                Debug.LogError($"[PlayerCombatController診断] {message}", this);
            }
            else
            {
                Debug.Log($"[PlayerCombatController診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }

        public string LastDiagnostic { get; private set; }
    }
}
