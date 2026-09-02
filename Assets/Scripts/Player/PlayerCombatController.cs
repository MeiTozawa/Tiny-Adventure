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
        private const float DefaultAttackRange = 2.2f;
        private const float DefaultAttackDamage = 25f;
        private const float DefaultAttackCompletionNormalizedTime = 0.95f;

        [Header("参照")]
        [SerializeField]
        private InputReader inputReader;

        [SerializeField]
        private PlayerAnimationDriver animationDriver;

        [SerializeField]
        private Animator targetAnimator;

        [SerializeField]
        private CombatantMarker combatantMarker;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private CombatHitbox swordHitbox;

        [Header("攻撃設定")]
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
        private int nextAttackSequenceId;
        private bool missingReferenceDiagnosticReported;

        public event Action<int> AttackSequenceStarted;
        public event Action<int> AttackSequenceCompleted;
        public event Action<int> AttackSequenceCancelled;
        public event Action<CombatantMarker, int> HitCandidateAccepted;
        public event Action<string> DiagnosticReported;

        public InputReader InputReader => inputReader;
        public Animator TargetAnimator => targetAnimator;
        public CombatantMarker CombatantMarker => combatantMarker;
        public GameFlowController GameFlowController => gameFlowController;
        public CombatHitbox SwordHitbox => swordHitbox;
        public AttackSequence CurrentAttackSequence => attackSequence;
        public bool IsAttacking => attackSequence != null && attackSequence.IsActive;
        public bool IsDead => dead || combatantMarker == null || !combatantMarker.IsAvailableForCombat;
        public int LastAttackSequenceId { get; private set; }
        public int AttackTriggerCount { get; private set; }
        public GameplayState CurrentGameplayState => gameFlowController != null ? gameFlowController.CurrentState : fallbackGameplayState;

        private void Awake()
        {
            ResolveReferences();
            InitializeAttackSequence();
            ValidateRequiredReferences(out _);
        }

        private void OnEnable()
        {
            ResolveReferences();
            InitializeAttackSequence();
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

            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            ProcessInput(snapshot);
            TickAttackAnimation();
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

            int sequenceId = ++nextAttackSequenceId;
            if (!attackSequence.StartSequence(sequenceId, out diagnostic))
            {
                return false;
            }

            LastAttackSequenceId = sequenceId;
            swordHitbox?.SetWindowTracker(attackWindowTracker);
            swordHitbox?.ResetForNewSequence();
            targetAnimator.SetTrigger("AttackTrigger");
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
            AttackSequenceCompleted?.Invoke(sequenceId);
            return true;
        }

        /// <summary>終局、無効化、アニメーション異常時に攻撃を閉じます。</summary>
        public void CancelAttack()
        {
            if (attackSequence == null || !attackSequence.IsActive)
            {
                return;
            }

            int sequenceId = LastAttackSequenceId;
            attackSequence.Cancel();
            AttackSequenceCancelled?.Invoke(sequenceId);
        }

        /// <summary>HealthComponentが死亡遷移へ入ったときに呼び出します。</summary>
        public void SetDead(bool value)
        {
            dead = value;
            if (dead)
            {
                CancelAttack();
            }
        }

        /// <summary>
        /// Knightオブジェクト自体を検査し、PlayerCombatControllerの欠落を明示します。
        /// </summary>
        public static bool ValidateKnightObject(GameObject knight, out IReadOnlyList<string> diagnostics)
        {
            var results = new List<string>();
            if (knight == null)
            {
                results.Add("KnightにPlayerCombatControllerがありません。");
            }
            else
            {
                PlayerCombatController controller = knight.GetComponent<PlayerCombatController>();
                if (controller == null)
                {
                    results.Add("KnightにPlayerCombatControllerがありません。");
                }
                else
                {
                    return controller.ValidateRequiredReferences(out diagnostics);
                }
            }

            diagnostics = results;
            foreach (string message in results)
            {
                Debug.LogError($"[PlayerCombatController診断] {message}", knight);
            }

            return false;
        }

        /// <summary>
        /// Knightの攻撃経路に必要な参照と日本語診断をまとめて検査します。
        /// </summary>
        public bool ValidateRequiredReferences(out IReadOnlyList<string> diagnostics)
        {
            var results = new List<string>();
            if (inputReader == null)
            {
                results.Add("PlayerCombatControllerのInputReader参照がありません。");
            }

            if (targetAnimator == null)
            {
                results.Add("PlayerCombatControllerのAnimator参照がありません。");
            }

            if (animationDriver == null)
            {
                results.Add("PlayerCombatControllerのPlayerAnimationDriver参照がありません。");
            }

            if (gameFlowController == null)
            {
                results.Add("PlayerCombatControllerのGameFlow参照がありません。");
            }

            if (combatantMarker == null)
            {
                results.Add("PlayerCombatControllerのCombatantMarker参照がありません。");
            }

            if (swordHitbox == null)
            {
                results.Add("PlayerCombatControllerのSwordHitbox参照がありません。");
            }
            else
            {
                Collider collider = swordHitbox.GetComponent<Collider>();
                if (collider == null || !collider.isTrigger)
                {
                    results.Add("SwordHitboxがTrigger Colliderではありません。");
                }
            }

            if (targetAnimator != null && !HasAnimatorParameter(targetAnimator, "AttackTrigger", AnimatorControllerParameterType.Trigger))
            {
                results.Add("KnightのAttackTriggerがAnimatorにありません。");
            }

            if (inputReader != null && !inputReader.TryInitialize())
            {
                string inputDiagnostic = inputReader.LastDiagnostic;
                if (string.IsNullOrEmpty(inputDiagnostic))
                {
                    inputDiagnostic = "Gameplay/Attackアクションが有効になっていません。";
                }

                results.Add(inputDiagnostic);
            }

            diagnostics = results;
            foreach (string message in results)
            {
                ReportDiagnostic(message, true);
            }

            return results.Count == 0;
        }

        /// <summary>テスト用にフロー状態の代替値を設定します。実行シーンではGameFlowControllerを使用します。</summary>
        public void SetFallbackGameplayState(GameplayState state)
        {
            fallbackGameplayState = state;
        }

        /// <summary>テスト用に必須参照を注入します。</summary>
        public void ConfigureForTests(InputReader reader, PlayerAnimationDriver driver, Animator animator, CombatantMarker marker, GameFlowController flow, CombatHitbox hitbox)
        {
            inputReader = reader;
            animationDriver = driver;
            targetAnimator = animator;
            combatantMarker = marker;
            gameFlowController = flow;
            swordHitbox = hitbox;
            ResolveReferences();
            InitializeAttackSequence();
        }

        private void TickAttackAnimation()
        {
            if (!IsAttacking || targetAnimator == null)
            {
                return;
            }

            AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName("Attack"))
            {
                attackSequence.Tick(stateInfo.normalizedTime);
                if (stateInfo.normalizedTime >= attackCompletionNormalizedTime)
                {
                    CompleteAttack();
                }
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

            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }

            if (combatantMarker == null)
            {
                combatantMarker = GetComponent<CombatantMarker>();
            }

            if (gameFlowController == null)
            {
                gameFlowController = FindAnyObjectByType<GameFlowController>();
            }

            if (swordHitbox == null)
            {
                swordHitbox = GetComponentInChildren<CombatHitbox>(true);
            }
        }

        private void InitializeAttackSequence()
        {
            if (attackSequence != null || combatantMarker == null)
            {
                return;
            }

            attackRange = Mathf.Max(MinimumAttackRange, attackRange);
            attackDamage = Mathf.Max(MinimumAttackRange, attackDamage);
            attackWindowTracker = new AttackWindowTracker(combatantMarker, attackRange);
            attackSequence = new AttackSequence(attackWindowTracker);
            attackWindowTracker.TargetRegistered += HandleTargetRegistered;
            swordHitbox?.SetWindowTracker(attackWindowTracker);
        }

        private void HandleTargetRegistered(CombatantMarker target, int sequenceId)
        {
            if (target == null || target == combatantMarker)
            {
                return;
            }

            // DamageService.Submitは正式なダメージ入口です。DamageServiceが存在する構成では
            // この通知をDamageRequestへ変換するアダプターを接続し、Healthを直接変更しません。
            DiagnosticReported?.Invoke($"攻撃系列{sequenceId}が対象「{target.CombatantId}」を受理しました。");
            HitCandidateAccepted?.Invoke(target, sequenceId);
        }

        private void ReportDiagnostic(string message, bool asError)
        {
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
                Debug.LogError($"[PlayerCombatController診断] {message}", this);
            }
            else
            {
                Debug.Log($"[PlayerCombatController診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }

        private static bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType type)
        {
            if (animator == null)
            {
                return false;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == parameterName && parameter.type == type)
                {
                    return true;
                }
            }

            return false;
        }

        public string LastDiagnostic { get; private set; }
    }
}
