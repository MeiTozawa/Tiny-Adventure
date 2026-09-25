using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// PlayerControllerの移動状態および攻撃・被撃・死亡をKayKit KnightのAnimatorへ反映します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerAnimationDriver : MonoBehaviour, IHitAnimationReceiver
    {
        private const float ReferenceMoveSpeed = 1f;

        private static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
        private static readonly int AttackTriggerParameter = Animator.StringToHash("AttackTrigger");
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");
        private static readonly int DeathTriggerParameter = Animator.StringToHash("DeathTrigger");
        private static readonly int ComboIndexParameter = Animator.StringToHash("ComboIndex");
        private static readonly int IsEnemyParameter = Animator.StringToHash("IsEnemy");

        [Header("参照")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private PlayerController playerController;

        [Header("再生速度")]
        [SerializeField, Min(0.01f)] private float locomotionSpeedMultiplier = 1f;

        public Animator TargetAnimator => targetAnimator;
        public PlayerController PlayerController => playerController;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            targetAnimator.SetBool(IsEnemyParameter, false);
        }

        private void Update()
        {
            bool isMoving = playerController.IsMoving;
            float configuredSpeed = Mathf.Max(0.01f, playerController.MoveSpeed);
            float mult = locomotionSpeedMultiplier > 0f ? locomotionSpeedMultiplier : 1f;
            float playbackRate = (configuredSpeed / ReferenceMoveSpeed) * mult;

            targetAnimator.SetBool(IsMovingParameter, isMoving);
            targetAnimator.SetFloat(MoveSpeedParameter, playerController.NormalizedMoveAmount);
            targetAnimator.speed = isMoving ? Mathf.Max(0.01f, playbackRate) : 1f;
        }

        public void TriggerAttack()
        {
            targetAnimator.SetTrigger(AttackTriggerParameter);
        }

        public void SetComboIndex(int comboIndex)
        {
            targetAnimator.SetInteger(ComboIndexParameter, comboIndex);
        }

        public bool IsInAttackState()
        {
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

        public Result<float> GetAttackNormalizedTime()
        {
            AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
            if (IsAttackStateName(stateInfo))
            {
                return stateInfo.normalizedTime;
            }

            return GameError.InvalidState;
        }

        public void TriggerHit()
        {
            targetAnimator.SetTrigger(HitTriggerParameter);
        }

        public void TriggerDeath()
        {
            targetAnimator.SetTrigger(DeathTriggerParameter);
        }

        public void SetAttackSpeedMultiplier(float multiplier)
        {
            targetAnimator.speed = Mathf.Max(0.01f, multiplier);
        }

        public void ClearAttackSpeedMultiplier()
        {
            targetAnimator.speed = 1f;
        }

        private static bool IsAttackStateName(AnimatorStateInfo stateInfo)
        {
            return stateInfo.IsName("Attack") ||
                   stateInfo.IsName("Attack_Horizontal") ||
                   stateInfo.IsName("Attack_Vertical") ||
                   stateInfo.IsName("Attack_Thrust") ||
                   stateInfo.IsTag("Attack");
        }
    }
}
