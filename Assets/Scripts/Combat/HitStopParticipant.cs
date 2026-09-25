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
    [ExecuteAlways]
    public sealed class HitStopParticipant : MonoBehaviour, IHitStopParticipant
    {
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private NavMeshAgent navMeshAgent;
        private IHitStopController hitStopController;

        private float savedAnimatorSpeed = 1f;
        private bool wasNavMeshAgentStopped;
        private bool isPaused;

        public bool IsHitStopParticipant => isActiveAndEnabled;
        public bool IsPaused => isPaused;

        [Inject]
        public void Construct(IHitStopController controller = null)
        {
            if (controller != null) hitStopController = controller;
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
        /// 局所ヒットストップを終了し、元の速度と状態を復帰します。
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

    }
}
