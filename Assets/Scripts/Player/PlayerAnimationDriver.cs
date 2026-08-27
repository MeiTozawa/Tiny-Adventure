using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// PlayerControllerの移動状態をKayKit KnightのAnimatorへ反映します。
    /// Idle、Locomotion、Attack、Hit、DeathはInspectorで明示的に割り当てたAnimationClipを
    /// Animator Controller側の状態に接続して使用し、文字列によるclip名の推測は行いません。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimationDriver : MonoBehaviour
    {
        private const float MinimumSpeedMultiplier = 0.1f;
        private const float MaximumSpeedMultiplier = 3f;
        private const float ReferenceMoveSpeed = 1f;

        private static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
        private static readonly int AttackTriggerParameter = Animator.StringToHash("AttackTrigger");
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");
        private static readonly int DeathTriggerParameter = Animator.StringToHash("DeathTrigger");

        [Header("参照")]
        [Tooltip("KayKit Knightの実際のAnimatorです。ModelRoot配下のKayKitKnightに割り当てます。")]
        [SerializeField]
        private Animator targetAnimator;

        [Tooltip("移動速度と入力を提供するPlayerControllerです。未設定時は自動的に親から検索します。")]
        [SerializeField]
        private PlayerController playerController;

        [Header("Idle / Locomotion clip")]
        [Tooltip("Animator Controller側のIdle状態に割り当てるKayKitの実際のAnimationClipです。診断のみに使用し、再生自体はAnimator Controllerが担います。")]
        [SerializeField]
        private AnimationClip idleClip;

        [Tooltip("Animator Controller側のLocomotion状態に割り当てるKayKitの実際のAnimationClipです。")]
        [SerializeField]
        private AnimationClip locomotionClip;

        [Tooltip("Animator Controller側のAttack状態に割り当てるKayKitの実際のAnimationClipです。専用の攻撃clipが無い場合は既存の挥剑clipを仮に使用します。")]
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

        private bool missingAnimatorReported;
        private bool missingControllerReported;
        private bool missingClipsReported;
        private bool missingPlayerControllerReported;

        private void Awake()
        {
            ResolveReferences();
            ValidateClipReferences();
        }

        private void OnValidate()
        {
            locomotionSpeedMultiplier = Mathf.Clamp(locomotionSpeedMultiplier, MinimumSpeedMultiplier, MaximumSpeedMultiplier);
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            bool isMoving = playerController.IsMoving;
            float configuredSpeed = Mathf.Max(0.01f, playerController.MoveSpeed);
            float playbackRate = Mathf.Clamp(
                configuredSpeed / ReferenceMoveSpeed,
                MinimumSpeedMultiplier,
                MaximumSpeedMultiplier) * locomotionSpeedMultiplier;

            targetAnimator.SetBool(IsMovingParameter, isMoving);
            targetAnimator.SetFloat(MoveSpeedParameter, playerController.NormalizedMoveAmount);
            targetAnimator.speed = isMoving ? Mathf.Clamp(playbackRate, MinimumSpeedMultiplier, MaximumSpeedMultiplier) : 1f;
        }

        /// <summary>
        /// 攻撃アクションが発生したときにAttackTriggerを発火します。
        /// 攻撃ウィンドウ自体の管理は本タスクの範囲外で別コンポーネントが行います。
        /// </summary>
        public void TriggerAttack()
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            targetAnimator.SetTrigger(AttackTriggerParameter);
        }

        /// <summary>
        /// 被撃時にHitTriggerを発火します。
        /// </summary>
        public void TriggerHit()
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            targetAnimator.SetTrigger(HitTriggerParameter);
        }

        /// <summary>
        /// 死亡時にDeathTriggerを発火します。
        /// </summary>
        public void TriggerDeath()
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            targetAnimator.SetTrigger(DeathTriggerParameter);
        }

        private bool EnsureReferencesReady()
        {
            ResolveReferences();

            if (targetAnimator == null)
            {
                ReportMissingAnimator();
                return false;
            }

            if (targetAnimator.runtimeAnimatorController == null)
            {
                ReportMissingController();
                return false;
            }

            if (playerController == null)
            {
                ReportMissingPlayerController();
                return false;
            }

            missingAnimatorReported = false;
            missingControllerReported = false;
            missingPlayerControllerReported = false;
            return true;
        }

        private void ResolveReferences()
        {
            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }

            if (playerController == null)
            {
                playerController = GetComponentInParent<PlayerController>();
                if (playerController == null)
                {
                    playerController = GetComponent<PlayerController>();
                }
            }
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
                    $"[アニメーション診断] 専用のAttack clipがKayKit資産に存在しないため、'{attackClip.name}'を仮のAttack clipとして使用しています。",
                    this);
            }
        }

        private void ReportMissingAnimator()
        {
            if (missingAnimatorReported)
            {
                return;
            }

            missingAnimatorReported = true;
            Debug.LogError("[アニメーション診断] PlayerAnimationDriverにKayKit KnightのAnimatorが見つかりません。", this);
        }

        private void ReportMissingController()
        {
            if (missingControllerReported)
            {
                return;
            }

            missingControllerReported = true;
            Debug.LogError(
                "[アニメーション診断] AnimatorのruntimeAnimatorControllerが設定されていません。KnightがT-poseのまま停止します。",
                this);
        }

        private void ReportMissingPlayerController()
        {
            if (missingPlayerControllerReported)
            {
                return;
            }

            missingPlayerControllerReported = true;
            Debug.LogError("[アニメーション診断] PlayerAnimationDriverにPlayerControllerが見つかりません。", this);
        }
    }
}
