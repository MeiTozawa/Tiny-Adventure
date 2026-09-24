using System;
using UnityEngine;
using UnityEngine.AI;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の移動、NavMesh 経路計算、経路再試行、旋回、アニメーション速度反映を担当する移動制御コンポーネントです。
    /// EnemyBrain から移動判断と運動制御の責務を分離し、NavMeshAgent の安全な操作を提供します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class EnemyMotor : MonoBehaviour, IKnockbackReceiver
    {
        private const float MovementEpsilon = 0.01f;

        [Header("参照")]
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private EnemyAnimationDriver animationDriver;

        [Header("運動設定")]
        [SerializeField, Min(0f)]
        private float turnSpeed;

        [SerializeField, Min(0f)]
        private float configuredStoppingDistance;

        [Header("経路失敗時の安全待機")]
        [SerializeField, Min(1)]
        private int maximumPathRetries;

        [SerializeField, Min(0f)]
        private float pathRetryInterval;

        [SerializeField, Min(0f)]
        private float pathRetryWaitDuration;

        private NavMeshPathStatus lastPathStatus = NavMeshPathStatus.PathInvalid;
        private Vector3 lastValidNavMeshPosition;
        private bool hasLastValidNavMeshPosition;
        private int pathRetryCount;
        private bool pathRetryWaitActive;
        private double nextPathAttemptTime;
        private NavMeshPath reusablePath;

        public NavMeshAgent Agent => navMeshAgent;
        public NavMeshPathStatus LastPathStatus => lastPathStatus;
        public Vector3 LastValidNavMeshPosition => lastValidNavMeshPosition;
        public bool HasLastValidNavMeshPosition => hasLastValidNavMeshPosition;
        public bool IsNavigationActive => navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
        public bool IsStopped => navMeshAgent == null || !navMeshAgent.enabled || navMeshAgent.isStopped;
        public int PathRetryCount => pathRetryCount;
        public bool IsRetryWaitActive => pathRetryWaitActive;
        public double NextPathAttemptTime => nextPathAttemptTime;
        public float ConfiguredStoppingDistance => configuredStoppingDistance;


        private void Awake()
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
            UnityEngine.Assertions.Assert.IsNotNull(navMeshAgent, "EnemyMotor: NavMeshAgentコンポーネントが必要です。");
            reusablePath = new NavMeshPath();
            EnsureAgentConfiguration();
        }

        private void OnEnable()
        {
            EnsureAgentConfiguration();
        }

        /// <summary>NavMeshAgentの初期設定を適用します。</summary>
        public void EnsureAgentConfiguration()
        {
            if (navMeshAgent == null)
            {
                return;
            }

            navMeshAgent.updateRotation = false;
            navMeshAgent.stoppingDistance = Mathf.Max(0.01f, configuredStoppingDistance);
            CaptureCurrentNavMeshPosition();
        }

        /// <summary>外部から運動パラメータを同期・設定します。</summary>
        public void Configure(
            float newTurnSpeed,
            float newStoppingDistance,
            int newMaxRetries,
            float newRetryInterval,
            float newRetryWaitDuration)
        {
            turnSpeed = Mathf.Max(1f, newTurnSpeed);
            configuredStoppingDistance = Mathf.Max(0.01f, newStoppingDistance);
            maximumPathRetries = Mathf.Max(1, newMaxRetries);
            pathRetryInterval = Mathf.Max(0f, newRetryInterval);
            pathRetryWaitDuration = Mathf.Max(0f, newRetryWaitDuration);

            if (navMeshAgent != null)
            {
                navMeshAgent.stoppingDistance = configuredStoppingDistance;
            }
        }

        /// <summary>指定された目標座標へ向けてNavMesh経路を計算し、追従を開始します。</summary>
        public Result NavigateTo(Vector3 targetPosition, double currentGameTime, bool queryPath)
        {
            if (!IsNavigationActive)
            {
                return GameError.InvalidState;
            }

            CaptureCurrentNavMeshPosition();

            if (pathRetryWaitActive)
            {
                if (currentGameTime < nextPathAttemptTime)
                {
                    MaintainExistingNavigation();
                    return GameError.ActionCooldownActive;
                }

                pathRetryWaitActive = false;
                pathRetryCount = 0;
                nextPathAttemptTime = currentGameTime;
            }

            if (queryPath)
            {
                bool calculated = navMeshAgent.CalculatePath(targetPosition, reusablePath);
                lastPathStatus = reusablePath.status;

                if (!calculated || reusablePath.status != NavMeshPathStatus.PathComplete)
                {
                    HandleInvalidPath(lastPathStatus, "Knightまでの有効なNavMesh経路がありません。", currentGameTime);
                    KeepAtLastValidNavMeshPosition();
                    return GameError.EnemyNavMeshFailure;
                }

                pathRetryCount = 0;
                pathRetryWaitActive = false;
                navMeshAgent.stoppingDistance = configuredStoppingDistance;
                navMeshAgent.isStopped = false;
                if (!navMeshAgent.SetDestination(targetPosition))
                {
                    lastPathStatus = NavMeshPathStatus.PathInvalid;
                    HandleInvalidPath(lastPathStatus, "NavMeshAgentが目的地を設定できませんでした。", currentGameTime);
                    KeepAtLastValidNavMeshPosition();
                    return GameError.EnemyNavMeshFailure;
                }
            }
            else
            {
                MaintainExistingNavigation();
            }

            FaceMovementDirection();
            UpdateMovementAnimation();
            return Result.Ok();
        }

        /// <summary>経路失敗カウンタと状態をリセットします。</summary>
        public void ResetPathFailureState()
        {
            pathRetryCount = 0;
            pathRetryWaitActive = false;
            nextPathAttemptTime = 0d;
            lastPathStatus = NavMeshPathStatus.PathInvalid;
        }

        /// <summary>ナビゲーションを停止し、現在のアニメーションを待機へ戻します。</summary>
        public void StopNavigation()
        {
            if (!navMeshAgent.enabled)
            {
                animationDriver.SetMovementState(false, 0f);
                return;
            }

            if (navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
                navMeshAgent.ResetPath();
            }

            animationDriver.SetMovementState(false, 0f);
        }

        /// <summary>
        /// 受撃インパルスによる微小ノックバックを安全に適用します。
        /// NavMeshAgent.Move を用いて NavMesh 境界を遵守しつつ後退させます。
        /// </summary>
        public void ApplyKnockback(Vector3 direction, float distance)
        {
            if (!IsNavigationActive)
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < MovementEpsilon * MovementEpsilon)
            {
                return;
            }

            Vector3 displacement = direction.normalized * Mathf.Max(0f, distance);
            navMeshAgent.Move(displacement);
            CaptureCurrentNavMeshPosition();
        }

        /// <summary>目標位置の方向へY軸回転でスムーズに向き直ります。</summary>
        public void FaceTarget(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            RotateTowards(direction);
        }

        /// <summary>現在のNavMesh移動速度ベクトルの向きへ回転します。</summary>
        public void FaceMovementDirection()
        {
            Vector3 direction = navMeshAgent.desiredVelocity;
            direction.y = 0f;
            RotateTowards(direction);
        }

        /// <summary>指定方向へ角速度に従って回転します。</summary>
        public void RotateTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude <= MovementEpsilon * MovementEpsilon)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
        }

        /// <summary>現在の実移動速度からアニメーションの走行状態を更新します。</summary>
        public void UpdateMovementAnimation()
        {
            if (!IsNavigationActive)
            {
                animationDriver.SetMovementState(false, 0f);
                return;
            }

            float actualSpeed = navMeshAgent.velocity.magnitude;
            float normalizedSpeed = navMeshAgent.speed > MovementEpsilon
                ? Mathf.Clamp01(actualSpeed / navMeshAgent.speed)
                : 0f;
            animationDriver.SetMovementState(actualSpeed > MovementEpsilon, normalizedSpeed);
        }

        /// <summary>現在NavMesh上にいる位置を安全位置として記録します。</summary>
        public void CaptureCurrentNavMeshPosition()
        {
            if (!IsNavigationActive)
            {
                return;
            }

            lastValidNavMeshPosition = navMeshAgent.nextPosition;
            hasLastValidNavMeshPosition = IsFiniteVector(lastValidNavMeshPosition);
        }

        /// <summary>経路異常時に最後に有効だった安全位置へ維持・復帰させます。</summary>
        public void KeepAtLastValidNavMeshPosition()
        {
            if (!navMeshAgent.enabled)
            {
                return;
            }

            if (navMeshAgent.isOnNavMesh)
            {
                if (navMeshAgent.hasPath || navMeshAgent.pathPending)
                {
                    MaintainExistingNavigation();
                    return;
                }

                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
                animationDriver.SetMovementState(false, 0f);
                return;
            }

            if (!hasLastValidNavMeshPosition)
            {
                return;
            }

            if (NavMesh.SamplePosition(
                    lastValidNavMeshPosition,
                    out NavMeshHit hit,
                    Mathf.Max(configuredStoppingDistance, 1f),
                    navMeshAgent.areaMask) && navMeshAgent.Warp(hit.position))
            {
                lastValidNavMeshPosition = hit.position;
                navMeshAgent.isStopped = true;
                animationDriver.SetMovementState(false, 0f);
                return;
            }
        }

        /// <summary>既存の有効経路を維持して追従を継続します。</summary>
        public void MaintainExistingNavigation()
        {
            if (!IsNavigationActive)
            {
                return;
            }

            if (navMeshAgent.hasPath || navMeshAgent.pathPending)
            {
                navMeshAgent.isStopped = false;
                FaceMovementDirection();
                UpdateMovementAnimation();
                return;
            }

            navMeshAgent.isStopped = true;
            animationDriver.SetMovementState(false, 0f);
        }

        private void HandleInvalidPath(NavMeshPathStatus pathStatus, string reason, double now)
        {
            lastPathStatus = pathStatus;
            if (pathRetryWaitActive && now < nextPathAttemptTime)
            {
                return;
            }

            if (pathRetryCount < maximumPathRetries)
            {
                pathRetryCount++;
                nextPathAttemptTime = now + pathRetryInterval;
                return;
            }

            pathRetryWaitActive = true;
            nextPathAttemptTime = now + pathRetryWaitDuration;
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
