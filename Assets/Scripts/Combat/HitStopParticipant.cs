using UnityEngine;
using UnityEngine.AI;

namespace TinyAdventure
{
    /// <summary>
    /// 角色 Hit Stop 局部顿挫参与者组件。
    /// 挂载在玩家与敌人身上，在受击顿挫期间暂停自身 Animator 和寻路/移动，并在顿挫结束时精确恢复。
    /// 绝不写入 Time.timeScale，确保只影响参与角色自身。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopParticipant : MonoBehaviour, IHitStopParticipant
    {
        [Header("组件引用（选填，未填时自动查找）")]
        [SerializeField]
        private Animator targetAnimator;

        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private CharacterController characterController;

        private float savedAnimatorSpeed = 1f;
        private bool wasNavMeshAgentStopped;
        private bool isPaused;

        public bool IsHitStopParticipant => isActiveAndEnabled;
        public bool IsPaused => isPaused;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            var controller = FindAnyObjectByType<HitStopController>();
            if (controller != null)
            {
                controller.RegisterParticipant(this);
            }
        }

        private void OnDisable()
        {
            var controller = FindAnyObjectByType<HitStopController>();
            if (controller != null)
            {
                controller.UnregisterParticipant(this);
            }

            if (isPaused)
            {
                EndHitStop(default);
            }
        }

        /// <summary>
        /// 开始局部顿挫。
        /// </summary>
        public void BeginHitStop(HitStopToken token)
        {
            if (isPaused) return;

            isPaused = true;

            if (targetAnimator == null)
            {
                ResolveReferences();
            }

            if (targetAnimator != null)
            {
                savedAnimatorSpeed = targetAnimator.speed;
                targetAnimator.speed = 0f;
            }

            if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
            {
                wasNavMeshAgentStopped = navMeshAgent.isStopped;
                navMeshAgent.isStopped = true;
            }
        }

        /// <summary>
        /// 结束局部顿挫，恢复原始速度与状态。
        /// </summary>
        public void EndHitStop(HitStopToken token)
        {
            if (!isPaused) return;

            isPaused = false;

            if (targetAnimator != null)
            {
                targetAnimator.speed = savedAnimatorSpeed;
            }

            if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = wasNavMeshAgentStopped;
            }
        }

        private void ResolveReferences()
        {
            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }

            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>() ?? GetComponentInParent<NavMeshAgent>();
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>() ?? GetComponentInParent<CharacterController>();
            }
        }
    }
}
