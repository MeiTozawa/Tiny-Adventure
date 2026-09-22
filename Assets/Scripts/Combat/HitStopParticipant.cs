using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// キャラクター用ヒットストップ参加者コンポーネント。
    /// プレイヤーおよび敵にアタッチされ、ヒットストップ中に自身の Animator および NavMeshAgent の移動を一時停止し、
    /// 終了時に元の速度と状態へ復帰させます。Time.timeScale には一切関与せず、対象キャラクター自身のみを制御します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopParticipant : MonoBehaviour, IHitStopParticipant
    {
        [Header("コンポーネント参照（任意、未設定時は自動検索）")]
        [SerializeField]
        private Animator targetAnimator;

        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private CharacterController characterController;

        [SerializeField]
        private HitStopController hitStopController;

        private float savedAnimatorSpeed = 1f;
        private bool wasNavMeshAgentStopped;
        private bool isPaused;

        public bool IsHitStopParticipant => isActiveAndEnabled;
        public bool IsPaused => isPaused;

        private Animator TargetAnimator => targetAnimator != null ? targetAnimator : (targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true));
        private NavMeshAgent NavAgent => navMeshAgent != null ? navMeshAgent : (navMeshAgent = GetComponent<NavMeshAgent>());
        private CharacterController CharController => characterController != null ? characterController : (characterController = GetComponent<CharacterController>());

        [Inject]
        public void Construct(HitStopController controller = null)
        {
            if (controller != null) hitStopController = controller;
        }

        private void Awake()
        {
            if (targetAnimator == null) targetAnimator = TargetAnimator;
            if (navMeshAgent == null) navMeshAgent = NavAgent;
            if (characterController == null) characterController = CharController;
        }

        private void OnEnable()
        {
            if (hitStopController != null)
            {
                hitStopController.RegisterParticipant(this);
            }
        }

        private void OnDisable()
        {
            if (hitStopController != null)
            {
                hitStopController.UnregisterParticipant(this);
            }

            if (isPaused)
            {
                EndHitStop(default);
            }
        }

        /// <summary>
        /// 局所ヒットストップを開始します。
        /// </summary>
        public void BeginHitStop(HitStopToken token)
        {
            if (isPaused) return;

            isPaused = true;

            var anim = TargetAnimator;
            if (anim != null)
            {
                savedAnimatorSpeed = anim.speed;
                anim.speed = 0f;
            }

            var agent = NavAgent;
            if (agent != null && agent.isOnNavMesh)
            {
                wasNavMeshAgentStopped = agent.isStopped;
                agent.isStopped = true;
            }
        }

        /// <summary>
        /// 局所ヒットストップを終了し、元の速度と状態を復帰します。
        /// </summary>
        public void EndHitStop(HitStopToken token)
        {
            if (!isPaused) return;

            isPaused = false;

            var anim = TargetAnimator;
            if (anim != null)
            {
                anim.speed = savedAnimatorSpeed;
            }

            var agent = NavAgent;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = wasNavMeshAgentStopped;
            }
        }

    }
}
