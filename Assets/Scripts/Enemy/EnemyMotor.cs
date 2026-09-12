using System;
using UnityEngine;
using UnityEngine.AI;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の移動、NavMesh 経路計算、経路再試行、旋回、アニメーション速度反映を担当する底盤コンポーネントです。
    /// EnemyBrain から移動判断と運動制御の責務を分離し、NavMeshAgent の安全な操作を提供します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class EnemyMotor : MonoBehaviour
    {
        private const float MovementEpsilon = 0.01f;

        [Header("参照")]
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private EnemyAnimationDriver animationDriver;

        [Header("運動設定")]
        [SerializeField, Min(1f)]
        private float turnSpeed = 540f;

        [SerializeField, Min(0.01f)]
        private float configuredStoppingDistance = 1.55f;

        [Header("経路失敗時の安全待機")]
        [SerializeField, Min(1)]
        private int maximumPathRetries = 3;

        [SerializeField, Min(0f)]
        private float pathRetryInterval = 0.5f;

        [SerializeField, Min(0f)]
        private float pathRetryWaitDuration = 2f;

        private NavMeshPathStatus lastPathStatus = NavMeshPathStatus.PathInvalid;
        private Vector3 lastValidNavMeshPosition;
        private bool hasLastValidNavMeshPosition;
        private int pathRetryCount;
        private bool pathRetryWaitActive;
        private double nextPathAttemptTime;
        private NavMeshPath reusablePath;
        private readonly Vector3[] reusablePathCorners = new Vector3[32];

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

        public event Action<string> PathDiagnosticReported;

        private void Awake()
        {
            ResolveReferences();
            EnsureAgentConfiguration();
        }

        private void OnEnable()
        {
            ResolveReferences();
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
            reusablePath ??= new NavMeshPath();
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
        public bool NavigateTo(Vector3 targetPosition, double currentGameTime, bool queryPath)
        {
            if (!EnsureAgentReady())
            {
                return false;
            }

            CaptureCurrentNavMeshPosition();

            if (pathRetryWaitActive)
            {
                if (currentGameTime < nextPathAttemptTime)
                {
                    MaintainExistingNavigation();
                    return false;
                }

                pathRetryWaitActive = false;
                pathRetryCount = 0;
                nextPathAttemptTime = currentGameTime;
            }

            if (queryPath)
            {
                reusablePath ??= new NavMeshPath();
                bool calculated = navMeshAgent.CalculatePath(targetPosition, reusablePath);
                lastPathStatus = reusablePath.status;

                if (!calculated || reusablePath.status != NavMeshPathStatus.PathComplete)
                {
                    HandleInvalidPath(lastPathStatus, "Knightまでの有効なNavMesh経路がありません。", currentGameTime);
                    KeepAtLastValidNavMeshPosition();
                    return false;
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
                    return false;
                }
            }
            else
            {
                MaintainExistingNavigation();
            }

            FaceMovementDirection();
            UpdateMovementAnimation();
            return true;
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
            if (navMeshAgent == null || !navMeshAgent.enabled)
            {
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            if (navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
            }

            animationDriver?.SetMovementState(false, 0f);
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
            if (navMeshAgent == null)
            {
                return;
            }

            Vector3 direction = navMeshAgent.desiredVelocity;
            direction.y = 0f;
            if (direction.sqrMagnitude > MovementEpsilon * MovementEpsilon)
            {
                RotateTowards(direction);
            }
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
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            float actualSpeed = navMeshAgent.velocity.magnitude;
            float normalizedSpeed = navMeshAgent.speed > MovementEpsilon
                ? Mathf.Clamp01(actualSpeed / navMeshAgent.speed)
                : 0f;
            animationDriver?.SetMovementState(actualSpeed > MovementEpsilon, normalizedSpeed);
        }

        /// <summary>現在NavMesh上にいる位置を安全位置として記録します。</summary>
        public void CaptureCurrentNavMeshPosition()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                return;
            }

            lastValidNavMeshPosition = navMeshAgent.nextPosition;
            hasLastValidNavMeshPosition = IsFiniteVector(lastValidNavMeshPosition);
        }

        /// <summary>経路異常時に最後に有効だった安全位置へ維持・復帰させます。</summary>
        public void KeepAtLastValidNavMeshPosition()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled)
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
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            if (!hasLastValidNavMeshPosition)
            {
                ReportPathDiagnostic("最後に確認したNavMesh上の位置がないため、安全位置へ戻せませんでした。", true);
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
                animationDriver?.SetMovementState(false, 0f);
                return;
            }

            ReportPathDiagnostic("最後のNavMesh位置を再取得できなかったため、敵を移動させず待機します。", true);
        }

        /// <summary>既存の有効経路を維持して追従を継続します。</summary>
        public void MaintainExistingNavigation()
        {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
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
            animationDriver?.SetMovementState(false, 0f);
        }

        /// <summary>テスト用に参照を注入します。</summary>
        public void ConfigureForTests(NavMeshAgent agent, EnemyAnimationDriver driver)
        {
            navMeshAgent = agent;
            animationDriver = driver;
            EnsureAgentConfiguration();
        }

        private bool EnsureAgentReady()
        {
            ResolveReferences();
            return navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
        }

        private void ResolveReferences()
        {
            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (animationDriver == null)
            {
                animationDriver = GetComponent<EnemyAnimationDriver>();
            }
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
                ReportPathDiagnostic($"{reason} 再試行{pathRetryCount}/{maximumPathRetries}回。", false);
                return;
            }

            pathRetryWaitActive = true;
            nextPathAttemptTime = now + pathRetryWaitDuration;
            ReportPathDiagnostic($"{reason} 有限回数の再試行が終了したため、{pathRetryWaitDuration:F1}秒間NavMesh上で待機します。", false);
        }

        private void ReportPathDiagnostic(string message, bool asError)
        {
            if (asError)
            {
                Debug.LogError($"[敵移動診断] {message}", this);
            }
            else
            {
                Debug.LogWarning($"[敵移動診断] {message}", this);
            }

            PathDiagnosticReported?.Invoke(message);
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
