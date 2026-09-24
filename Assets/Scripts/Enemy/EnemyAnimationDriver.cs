using UnityEngine;
using UnityEngine.AI;

namespace TinyAdventure
{
    /// <summary>
    /// 敵キャラクターのAnimatorへ移動・攻撃・被撃・死亡状態を反映します。
    /// Idle、Locomotion、Attack、Hit、DeathはInspectorで明示的に割り当てたKayKitの
    /// 実際のAnimationClipをAnimator Controller側の状態に接続して使用します。
    /// EnemyBrain等の上位コンポーネントが未実装の間でも単独で動作できるように、
    /// NavMeshAgentが存在する場合はその速度から移動状態を自動的に推定します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnimationDriver : MonoBehaviour, IHitAnimationReceiver
    {
        private const float MovementEpsilon = 0.01f;

        private static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
        private static readonly int AttackTriggerParameter = Animator.StringToHash("AttackTrigger");
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");
        private static readonly int DeathTriggerParameter = Animator.StringToHash("DeathTrigger");
        private static readonly int IsEnemyParameter = Animator.StringToHash("IsEnemy");

        [Header("参照")]
        [Tooltip("敵モデルの実際のAnimatorです。")]
        [SerializeField]
        private Animator targetAnimator;

        [Tooltip("移動速度の自動推定に使うNavMeshAgentです。EnemyBrainが未実装の間の暫定手段として使用します。未設定時は自動検索します。")]
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        public Animator TargetAnimator => targetAnimator;
        public NavMeshAgent NavMeshAgent => navMeshAgent;


        [Header("Idle / Locomotion clip")]
        [Tooltip("Animator Controller側のIdle状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip idleClip;

        [Tooltip("Animator Controller側のLocomotion状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip locomotionClip;

        [Tooltip("Animator Controller側のAttack状態に割り当てるKayKitの実際のAnimationClipです。専用の攻撃clipが無い場合は既存の剣攻撃clipを仮に使用します。")]
        [SerializeField]
        private AnimationClip attackClip;

        [Tooltip("Animator Controller側のHit状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip hitClip;

        [Tooltip("Animator Controller側のDeath状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip deathClip;

        [Header("再生速度")]
        [Tooltip("移動速度をLocomotion再生倍率へ変換する係数です。")]
        [SerializeField, Min(0.01f)]
        private float locomotionSpeedMultiplier;

        private bool hasExternalMovementOverride;
        private bool externalIsMoving;
        private float externalNormalizedSpeed;

        private bool missingClipsReported;

        private void Awake()
        {
            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }

            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetBool(IsEnemyParameter, true);
            }

            ValidateClipReferences();
        }

        private void OnValidate()
        {
            locomotionSpeedMultiplier = Mathf.Max(0.01f, locomotionSpeedMultiplier);
        }

        private void OnEnable()
        {
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                UnityEngine.Assertions.Assert.IsNotNull(targetAnimator, "EnemyAnimationDriver: Animatorが必要です。");
            }
        }

        private void Update()
        {
            GetCurrentMovement(out bool isMoving, out float normalizedSpeed);

            float mult = locomotionSpeedMultiplier > 0f ? locomotionSpeedMultiplier : 1f;
            float playbackRate = normalizedSpeed * mult;

            targetAnimator.SetBool(IsMovingParameter, isMoving);
            targetAnimator.SetFloat(MoveSpeedParameter, normalizedSpeed);
            targetAnimator.speed = isMoving ? Mathf.Max(0.01f, playbackRate) : 1f;
        }

        /// <summary>
        /// EnemyBrain等の上位コンポーネントから移動状態を明示的に設定します。
        /// 呼び出された場合はNavMeshAgentからの自動推定を上書きします。
        /// </summary>
        public void SetMovementState(bool isMoving, float normalizedSpeed)
        {
            hasExternalMovementOverride = true;
            externalIsMoving = isMoving;
            externalNormalizedSpeed = Mathf.Clamp01(normalizedSpeed);
        }

        /// <summary>攻撃時にAttackTriggerを発火します。</summary>
        public void TriggerAttack()
        {
            if (targetAnimator != null)
            {
                targetAnimator.SetTrigger(AttackTriggerParameter);
            }
        }

        /// <summary>被撃時にHitTriggerを発火します。</summary>
        public void TriggerHit()
        {
            if (targetAnimator != null)
            {
                targetAnimator.SetTrigger(HitTriggerParameter);
            }
        }

        /// <summary>死亡時にDeathTriggerを発火します。</summary>
        public void TriggerDeath()
        {
            if (targetAnimator != null)
            {
                targetAnimator.SetTrigger(DeathTriggerParameter);
            }
        }

        /// <summary>
        /// Animatorが現在攻撃ステート（または攻撃への遷移中）にあるかを返します。
        /// </summary>
        public bool IsInAttackState()
        {
            if (targetAnimator == null || !targetAnimator.isActiveAndEnabled || targetAnimator.runtimeAnimatorController == null)
            {
                return false;
            }

            AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
            if (IsAttackStateName(stateInfo))
            {
                return true;
            }

            if (targetAnimator.IsInTransition(0))
            {
                AnimatorStateInfo nextState = targetAnimator.GetNextAnimatorStateInfo(0);
                if (IsAttackStateName(nextState))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 現在の攻撃アニメーションのnormalizedTimeを取得します。攻撃ステートでない場合はエラーを返します。
        /// </summary>
        public Result<float> GetAttackNormalizedTime()
        {
            if (targetAnimator == null || !targetAnimator.isActiveAndEnabled)
            {
                return GameError.InvalidState;
            }

            AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
            if (IsAttackStateName(stateInfo))
            {
                return stateInfo.normalizedTime;
            }

            return GameError.InvalidState;
        }

        private static bool IsAttackStateName(AnimatorStateInfo stateInfo)
        {
            return stateInfo.IsName("Attack") ||
                   stateInfo.IsName("Attack_Horizontal") ||
                   stateInfo.IsName("Attack_Vertical") ||
                   stateInfo.IsName("Attack_Thrust") ||
                   stateInfo.IsTag("Attack");
        }

private void GetCurrentMovement(out bool isMoving, out float normalizedSpeed)
        {
            // Brainから停止を明示された場合は、NavMeshAgentに残った速度値より
            // 停止指示を優先します。それ以外は実際のAgent速度を読み、
            // 経路の再問い合わせ間隔でも見かけのLocomotionを固定しません。
            if (hasExternalMovementOverride && !externalIsMoving)
            {
                isMoving = false;
                normalizedSpeed = 0f;
                return;
            }

            if (navMeshAgent != null && navMeshAgent.speed > MovementEpsilon &&
                navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                float speedRatio = navMeshAgent.velocity.magnitude / navMeshAgent.speed;
                isMoving = speedRatio > MovementEpsilon;
                normalizedSpeed = Mathf.Clamp01(speedRatio);
                return;
            }

            if (hasExternalMovementOverride)
            {
                isMoving = externalIsMoving;
                normalizedSpeed = externalNormalizedSpeed;
                return;
            }

            isMoving = false;
            normalizedSpeed = 0f;
        }

        private void ValidateClipReferences()
        {
            if (missingClipsReported)
            {
                return;
            }

            if (idleClip == null || locomotionClip == null || attackClip == null || hitClip == null || deathClip == null)
            {
                missingClipsReported = true;
                Debug.LogError(
                    "[アニメーション診断] EnemyAnimationDriverにIdle/Locomotion/Attack/Hit/Deathの" +
                    "KayKit AnimationClip参照が不足しています。Inspectorで明示的に割り当ててください。",
                    this);
            }

            if (attackClip != null && attackClip.name != "Attack")
            {
                Debug.LogWarning(
                    $"[アニメーション診断] 専用のAttack clipがKayKit資産に存在しないため、'{attackClip.name}'を仮のAttack clipとして使用しています。",
                    this);
            }
        }
    }
}
