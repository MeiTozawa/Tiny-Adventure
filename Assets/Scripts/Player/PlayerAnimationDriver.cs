using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// PlayerControllerの移動状態をKayKit KnightのAnimatorへ反映します。
    /// Idle、Locomotion、Attack、Hit、DeathはInspectorで明示的に割り当てたAnimationClipを
    /// Animator Controller側の状態に接続して使用し、文字列によるclip名の推測は行いません。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class PlayerAnimationDriver : MonoBehaviour, IHitAnimationReceiver
    {
        private const float MinimumSpeedMultiplier = 0.1f;
        private const float MaximumSpeedMultiplier = 3f;
        private const float ReferenceMoveSpeed = 1f;

        private static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
        private static readonly int AttackTriggerParameter = Animator.StringToHash("AttackTrigger");
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");
        private static readonly int DeathTriggerParameter = Animator.StringToHash("DeathTrigger");
        private static readonly int ComboIndexParameter = Animator.StringToHash("ComboIndex");
        private static readonly int IsEnemyParameter = Animator.StringToHash("IsEnemy");

        [Header("参照")]
        [Tooltip("KayKit Knightの実際のAnimatorです。ModelRoot配下のKayKitKnightに割り当てます。")]
        [SerializeField]
        private Animator targetAnimator;

        [Tooltip("移動速度と入力を提供するPlayerControllerです。未設定時は自動的に親から検索します。")]
        [SerializeField]
        private PlayerController playerController;

        public Animator TargetAnimator => targetAnimator;
        public PlayerController PlayerController => playerController;

        public void Construct(Animator animator = null, PlayerController controller = null)
        {
            if (animator != null) targetAnimator = animator;
            if (controller != null) playerController = controller;
        }

        [Header("Idle / Locomotion clip")]
        [Tooltip("Animator Controller側のIdle状態に割り当てるKayKitの実際のAnimationClipです。診断のみに使用し、再生自体はAnimator Controllerが担います。")]
        [SerializeField]
        private AnimationClip idleClip;

        [Tooltip("Animator Controller側のLocomotion状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip locomotionClip;

        [Tooltip("Animator Controller側のAttack状態に割り当てるKayKitの実際のAnimationClipです。専用のAttack clipが無い場合は既存の攻撃clipを代替として使用します。")]
        [SerializeField]
        private AnimationClip attackClip;

        [Tooltip("Animator Controller側のHit状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip hitClip;

        [Tooltip("Animator Controller側のDeath状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip deathClip;

        [Header("再生速度")]
        [Tooltip("移動速度をLocomotion再生倍率へ変換する係数です。安全な範囲にクランプされます。")]
        [SerializeField, Range(MinimumSpeedMultiplier, MaximumSpeedMultiplier)]
        private float locomotionSpeedMultiplier = 1f;

        private bool missingClipsReported;

        private bool isAttackSpeedOverridden;
        private float attackSpeedMultiplierOverride = 1f;

        /// <summary>現在攻撃アニメーション速度がオーバーライドされているかを示します。</summary>
        public bool IsAttackSpeedOverridden => isAttackSpeedOverridden;

        /// <summary>現在適用されている攻撃アニメーション速度倍率です。</summary>
        public float CurrentAttackSpeedMultiplier => isAttackSpeedOverridden ? attackSpeedMultiplierOverride : 1f;

        /// <summary>
        /// 攻撃動作中のアニメーション速度オーバーライドを設定します。
        /// </summary>
        public void SetAttackSpeedMultiplier(float multiplier)
        {
            attackSpeedMultiplierOverride = Mathf.Clamp(multiplier, MinimumSpeedMultiplier, MaximumSpeedMultiplier);
            isAttackSpeedOverridden = true;
            var anim = TargetAnimator;
            if (anim != null)
            {
                anim.speed = attackSpeedMultiplierOverride;
            }
        }

        /// <summary>
        /// 攻撃完了時にアニメーション速度を通常時（歩行・待機）へ復帰させます。
        /// </summary>
        public void ClearAttackSpeedMultiplier()
        {
            isAttackSpeedOverridden = false;
            attackSpeedMultiplierOverride = 1f;
            var anim = TargetAnimator;
            if (anim != null)
            {
                anim.speed = 1f;
            }
        }

        private void Awake()
        {
            targetAnimator = GetComponentInChildren<Animator>(true);
            playerController = GetComponentInParent<PlayerController>();

            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetBool(IsEnemyParameter, false);
            }

            if (Application.isPlaying)
            {
                ValidateClipReferences();
            }
        }

        private void OnValidate()
        {
            locomotionSpeedMultiplier = Mathf.Clamp(locomotionSpeedMultiplier, MinimumSpeedMultiplier, MaximumSpeedMultiplier);
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                if (targetAnimator == null) targetAnimator = GetComponentInChildren<Animator>();
                if (playerController == null) playerController = GetComponentInParent<PlayerController>();
                UnityEngine.Assertions.Assert.IsNotNull(targetAnimator, "PlayerAnimationDriver: Animatorが必要です。");
                UnityEngine.Assertions.Assert.IsNotNull(playerController, "PlayerAnimationDriver: PlayerControllerが必要です。");
            }
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            bool isMoving = playerController.IsMoving;
            float configuredSpeed = Mathf.Max(0.01f, playerController.MoveSpeed);
            float playbackRate = Mathf.Clamp(
                configuredSpeed / ReferenceMoveSpeed,
                MinimumSpeedMultiplier,
                MaximumSpeedMultiplier) * locomotionSpeedMultiplier;

            targetAnimator.SetBool(IsMovingParameter, isMoving);
            targetAnimator.SetFloat(MoveSpeedParameter, playerController.NormalizedMoveAmount);

            if (isAttackSpeedOverridden)
            {
                targetAnimator.speed = attackSpeedMultiplierOverride;
            }
            else
            {
                targetAnimator.speed = isMoving ? Mathf.Clamp(playbackRate, MinimumSpeedMultiplier, MaximumSpeedMultiplier) : 1f;
            }
        }

        /// <summary>
        /// 攻撃アクションが発生したときにAttackTriggerを発火します。
        /// 攻撃ウィンドウ自体の管理は本タスクの範囲外で別コンポーネントが行います。
        /// </summary>
        public void TriggerAttack()
        {
            var anim = TargetAnimator;
            if (anim == null)
            {
                return;
            }

            anim.SetTrigger(AttackTriggerParameter);
        }

        /// <summary>
        /// コンボ段数（0: 横薙ぎ, 1: 縦斬り, 2: 突進刺突）をAnimatorに設定します。
        /// </summary>
        public void SetComboIndex(int comboIndex)
        {
            var anim = TargetAnimator;
            if (anim == null)
            {
                return;
            }

            anim.SetInteger(ComboIndexParameter, comboIndex);
        }

        /// <summary>
        /// Animatorが現在攻撃ステート（または攻撃への遷移中）にあるかを返します。
        /// </summary>
        public bool IsInAttackState()
        {
            var anim = TargetAnimator;
            if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null)
            {
                return false;
            }

            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (IsAttackStateName(stateInfo))
            {
                return true;
            }

            if (anim.IsInTransition(0))
            {
                AnimatorStateInfo nextState = anim.GetNextAnimatorStateInfo(0);
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
            var anim = TargetAnimator;
            if (anim == null || !anim.isActiveAndEnabled)
            {
                return GameError.InvalidState;
            }

            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
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

        /// <summary>
        /// 被撃時にHitTriggerを発火します。
        /// </summary>
        public void TriggerHit()
        {
            var anim = TargetAnimator;
            if (anim == null)
            {
                return;
            }

            anim.SetTrigger(HitTriggerParameter);
        }

        /// <summary>
        /// 死亡時にDeathTriggerを発火します。
        /// </summary>
        public void TriggerDeath()
        {
            var anim = TargetAnimator;
            if (anim == null)
            {
                return;
            }

            anim.SetTrigger(DeathTriggerParameter);
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
                    "[アニメーション診断] PlayerAnimationDriverにIdle/Locomotion/Attack/Hit/Deathの" +
                    "KayKit AnimationClip参照が不足しています。Inspectorで明示的に割り当ててください。",
                    this);
            }

            if (attackClip != null && attackClip.name != "Attack")
            {
                Debug.LogWarning(
                    $"[アニメーション診断] KayKitに専用のAttack clipが存在しないため、実際にインポート済みの代替clip「{attackClip.name}」をAttack状態で使用しています。専用clipを追加した場合はInspectorの参照を更新してください。",
                    this);
            }
        }
    }
}
