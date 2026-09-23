using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// カメラ基準の入力でKnightを移動し、接地とプレイ可能領域を安全に維持します。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float MinimumMoveSpeed = 0.01f;
        public const float DefaultMoveSpeed = 5.0f;
        private const float DirectionEpsilon = 0.0001f;

        /// <summary>第一人称カメラが敵モデル内部へ侵入（めり込み）するのを防ぐ最小中心間安全間距（メートル）です。</summary>
        public const float MinimumEnemyClearance = 1.10f;
        private static readonly Collider[] ProximityBuffer = new Collider[16];

        [Header("参照")]
        [SerializeField]
        private InputReader inputReader;

        [SerializeField]
        private Camera movementCamera;

        [Tooltip("Knightの移動を許可する領域を表すColliderです。未設定時は地面接触のみを検査します。")]
        [SerializeField]
        private Collider playableArea;

        [Header("ステータス設定")]
        [Tooltip("キャラクターの基礎ステータスアセットです。未設定時はデフォルト値を使用します。")]
        [SerializeField]
        private CharacterStatsConfig statsConfig;

        [SerializeField]
        private readonly float gravity = -25f;

        [SerializeField]
        private float groundedVerticalSpeed = -2f;

        [Header("地面検査")]
        [SerializeField]
        private LayerMask groundLayers = ~0;

        [SerializeField, Min(0.01f)]
        private float groundProbeStartHeight = 2f;

        [SerializeField, Min(0.01f)]
        private float groundProbeDistance = 4f;

        [SerializeField]
        private FirstPersonViewmodelController viewmodelController;

        private CharacterController characterController;
        private float verticalVelocity;
        private Vector3 lastValidPosition;
        private bool hasLastValidPosition;
        private float? moveSpeedOverride;

        private bool isLunging;
        private Vector3 lungeDirection = Vector3.forward;
        private float lungeTotalDistance;
        private float lungeTotalDuration;
        private float lungeRemainingTime;

        public CharacterStatsConfig StatsConfig
        {
            get => statsConfig;
            set => statsConfig = value;
        }

        /// <summary>実際に用いる正の移動速度です。StatsConfig設定時はそちらを優先します。</summary>
        public float MoveSpeed
        {
            get => moveSpeedOverride ?? (statsConfig != null ? statsConfig.MoveSpeed : DefaultMoveSpeed);
            set => moveSpeedOverride = Mathf.Max(MinimumMoveSpeed, value);
        }

        /// <summary>現在の水平方向入力を変換したワールド移動方向です。</summary>
        public Vector3 WorldMoveDirection { get; private set; }

        /// <summary>正規化後の入力強度です。</summary>
        public float NormalizedMoveAmount { get; private set; }

        /// <summary>現在水平方向に移動しているかを示します。</summary>
        public bool IsMoving => WorldMoveDirection.sqrMagnitude > DirectionEpsilon;

        /// <summary>第一人称視口武器コントローラーです。</summary>
        public FirstPersonViewmodelController ViewmodelController => viewmodelController;

        /// <summary>第一人称視口武器コントローラーを設定します。</summary>
        public void SetViewmodelController(FirstPersonViewmodelController controller) => viewmodelController = controller;

        /// <summary>現在攻撃の踏み込み突進（Forward Lunge）を実行中かを示します。</summary>
        public bool IsLunging => isLunging;

        /// <summary>直近のフレームで計算された踏み込み突進の移動ベクトルです。</summary>
        public Vector3 LastLungeMotion { get; private set; }

        /// <summary>最後に地面と領域の両方で検証できた安全な位置です。</summary>
        public Vector3 LastValidPosition => lastValidPosition;

        [Inject]
        public void Construct(FirstPersonViewmodelController vmController = null)
        {
            if (vmController != null) viewmodelController = vmController;
        }

        public void SetDependencies(
            InputReader input = null,
            Camera movementCam = null,
            FirstPersonViewmodelController vmController = null)
        {
            if (input != null) inputReader = input;
            if (movementCam != null) movementCamera = movementCam;
            if (vmController != null) viewmodelController = vmController;
        }

        /// <summary>移動を制御するCharacterControllerです。</summary>
        public CharacterController CharacterController => characterController;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            CaptureCurrentPositionIfSafe();
        }

        private void OnValidate()
        {
            groundedVerticalSpeed = Mathf.Min(0f, groundedVerticalSpeed);
            groundProbeStartHeight = Mathf.Max(0.01f, groundProbeStartHeight);
            groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            if (characterController == null)
            {
                return;
            }

            GameplayInputSnapshot input = inputReader != null ? inputReader.ReadSnapshot() : default;
            ProcessMovement(input.Move, Time.deltaTime);
        }

        /// <summary>
        /// 攻撃時の踏み込み突進（Forward Lunge）を開始します。
        /// 移動境界と接地判定を安全に維持しながら、指定方向へ減速移動します。
        /// </summary>
        public void StartAttackLunge(Vector3 direction, float distance, float duration)
        {
            Vector3 horizontalDir = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontalDir.sqrMagnitude <= DirectionEpsilon)
            {
                horizontalDir = transform.forward;
                horizontalDir.y = 0f;
            }

            horizontalDir.Normalize();
            lungeDirection = horizontalDir;
            lungeTotalDistance = Mathf.Max(0f, distance);
            lungeTotalDuration = Mathf.Max(0.001f, duration);
            lungeRemainingTime = lungeTotalDuration;
            isLunging = lungeTotalDistance > 0f && lungeRemainingTime > 0f;
        }

        /// <summary>
        /// 被撃・死亡・攻撃中断時に踏み込み突進を直ちに停止します。
        /// </summary>
        public void CancelLunge()
        {
            isLunging = false;
            lungeRemainingTime = 0f;
        }

        /// <summary>
        /// 移動入力をカメラ基準の水平移動と重力へ変換します。移動入力はPlayerのyawを変更しません。
        /// </summary>
        public void ProcessMovement(Vector2 moveInput, float deltaTime)
        {
            var cc = CharacterController;
            if (cc == null)
            {
                return;
            }

            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            Vector2 normalizedInput = Vector2.ClampMagnitude(moveInput, 1f);
            NormalizedMoveAmount = normalizedInput.magnitude;
            WorldMoveDirection = GetCameraRelativeDirection(normalizedInput, GetMovementCameraTransform());

            viewmodelController?.SetMovementState(IsMoving, NormalizedMoveAmount);

            if (cc.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedVerticalSpeed;
            }

            verticalVelocity += gravity * safeDeltaTime;

            Vector3 horizontalMotion;
            if (isLunging && lungeRemainingTime > 0f && lungeTotalDuration > 0f)
            {
                float stepTime = Mathf.Min(safeDeltaTime, lungeRemainingTime);
                float midRemainingTime = Mathf.Max(0f, lungeRemainingTime - stepTime * 0.5f);
                float normalizedProgress = midRemainingTime / lungeTotalDuration;
                float currentSpeed = (2f * lungeTotalDistance / lungeTotalDuration) * normalizedProgress;
                Vector3 lungeDisplacement = lungeDirection * (currentSpeed * stepTime);

                lungeRemainingTime -= safeDeltaTime;
                if (lungeRemainingTime <= 0f)
                {
                    isLunging = false;
                    lungeRemainingTime = 0f;
                }

                LastLungeMotion = lungeDisplacement;
                horizontalMotion = lungeDisplacement + (WorldMoveDirection * (MoveSpeed * 0.15f * safeDeltaTime));
            }
            else
            {
                isLunging = false;
                LastLungeMotion = Vector3.zero;
                horizontalMotion = WorldMoveDirection * (MoveSpeed * safeDeltaTime);
            }

            Vector3 requestedMotion = horizontalMotion + Vector3.up * (verticalVelocity * safeDeltaTime);
            MoveSafely(requestedMotion);
        }

        /// <summary>
        /// 入力をカメラの水平前方・右方向に変換します。カメラが未指定または真上を向く場合はキャラクターの向きを使用します。
        /// </summary>
        public static Vector3 GetCameraRelativeDirection(Vector2 moveInput, Transform cameraTransform)
        {
            Vector2 clampedInput = Vector2.ClampMagnitude(moveInput, 1f);
            if (clampedInput.sqrMagnitude <= DirectionEpsilon)
            {
                return Vector3.zero;
            }

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            right = Vector3.ProjectOnPlane(right, Vector3.up);

            if (forward.sqrMagnitude <= DirectionEpsilon)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude <= DirectionEpsilon)
            {
                right = Vector3.right;
            }

            forward.Normalize();
            right.Normalize();
            return Vector3.ClampMagnitude(right * clampedInput.x + forward * clampedInput.y, 1f);
        }

        private void MoveSafely(Vector3 requestedMotion)
        {
            Vector3 currentPosition = transform.position;
            Vector3 candidatePosition = currentPosition + requestedMotion;
            candidatePosition = ClampCandidateAgainstEnemies(currentPosition, candidatePosition);
            Vector3 resolvedPosition;

            if (IsSafePosition(candidatePosition))
            {
                resolvedPosition = candidatePosition;
            }
            else if (!TryProjectToNearestSafeGround(candidatePosition, out resolvedPosition))
            {
                if (!hasLastValidPosition)
                {
                    CaptureCurrentPositionIfSafe();
                }

                resolvedPosition = hasLastValidPosition ? lastValidPosition : currentPosition;
            }

            CollisionFlags flags = characterController.Move(resolvedPosition - currentPosition);
            if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            {
                verticalVelocity = groundedVerticalSpeed;
            }

            if (IsSafePosition(transform.position))
            {
                lastValidPosition = transform.position;
                hasLastValidPosition = true;
                return;
            }

            // CharacterControllerの衝突結果でも領域外に残った場合だけ、検証済み位置へ戻します。
            if (hasLastValidPosition)
            {
                characterController.Move(lastValidPosition - transform.position);
            }
        }

        private Vector3 ClampCandidateAgainstEnemies(Vector3 currentPos, Vector3 candidatePos)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                candidatePos + Vector3.up * 1.0f,
                MinimumEnemyClearance + 0.5f,
                ProximityBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
            {
                return candidatePos;
            }

            Vector3 resultPos = candidatePos;
            Vector2 currentHorizontal = new(currentPos.x, currentPos.z);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = ProximityBuffer[i];
                if (col == null || col.transform == transform || col.transform.IsChildOf(transform))
                {
                    continue;
                }

                CombatantMarker marker = col.TryGetComponent<CombatantMarker>(out var m) ? m : col.GetComponentInParent<CombatantMarker>();
                if (marker == null || marker.Faction != CombatantMarker.CombatantFaction.Enemy || !marker.gameObject.activeInHierarchy)
                {
                    continue;
                }

                HealthComponent health = marker.Health;
                if (health != null && !health.IsAlive)
                {
                    continue;
                }

                Vector3 enemyCenter = marker.transform.position;
                Vector2 enemyHorizontal = new(enemyCenter.x, enemyCenter.z);
                Vector2 candidateHorizontal = new(resultPos.x, resultPos.z);

                Vector2 enemyToCandidate = candidateHorizontal - enemyHorizontal;
                float distCandidate = enemyToCandidate.magnitude;

                if (distCandidate < MinimumEnemyClearance)
                {
                    Vector2 enemyToCurrent = currentHorizontal - enemyHorizontal;
                    float distCurrent = enemyToCurrent.magnitude;
                    float allowedDist = distCurrent >= MinimumEnemyClearance ? MinimumEnemyClearance : distCurrent;

                    if (distCandidate < allowedDist)
                    {
                        Vector2 pushDir;
                        if (distCandidate > DirectionEpsilon)
                        {
                            pushDir = enemyToCandidate / distCandidate;
                        }
                        else if (distCurrent > DirectionEpsilon)
                        {
                            pushDir = enemyToCurrent / distCurrent;
                        }
                        else
                        {
                            Vector3 fwd = -transform.forward;
                            pushDir = new Vector2(fwd.x, fwd.z);
                            if (pushDir.sqrMagnitude <= DirectionEpsilon)
                            {
                                pushDir = -Vector2.up;
                            }
                            pushDir.Normalize();
                        }

                        Vector2 clampedHorizontal = enemyHorizontal + pushDir * allowedDist;
                        resultPos = new Vector3(clampedHorizontal.x, resultPos.y, clampedHorizontal.y);
                    }
                }
            }

            return resultPos;
        }

        private bool IsSafePosition(Vector3 position)
        {
            return IsInsidePlayableArea(position) && TryGetGround(position, out _);
        }

        private bool TryProjectToNearestSafeGround(Vector3 candidatePosition, out Vector3 projectedPosition)
        {
            Vector3 horizontalCandidate = candidatePosition;
            if (playableArea != null)
            {
                horizontalCandidate = playableArea.ClosestPoint(candidatePosition);
            }

            if (TryGetGround(horizontalCandidate, out RaycastHit hit) && IsInsidePlayableArea(hit.point))
            {
                projectedPosition = hit.point - Vector3.up * GetControllerBottomOffset();
                return true;
            }

            projectedPosition = default;
            return false;
        }

        private bool TryGetGround(Vector3 position, out RaycastHit hit)
        {
            Vector3 origin = position + Vector3.up * groundProbeStartHeight;
            float castDistance = groundProbeStartHeight + groundProbeDistance;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                castDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore);

            bool foundGround = false;
            hit = default;
            foreach (RaycastHit candidate in hits)
            {
                Collider collider = candidate.collider;
                if (collider == null || collider.transform == transform || collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!foundGround || candidate.distance < hit.distance)
                {
                    hit = candidate;
                    foundGround = true;
                }
            }

            return foundGround;
        }

        private bool IsInsidePlayableArea(Vector3 position)
        {
            if (playableArea == null)
            {
                return true;
            }

            Vector3 closestPoint = playableArea.ClosestPoint(position);
            return (closestPoint - position).sqrMagnitude <= DirectionEpsilon;
        }

        private float GetControllerBottomOffset()
        {
            return characterController.center.y - characterController.height * 0.5f;
        }

        private void CaptureCurrentPositionIfSafe()
        {
            if (!IsSafePosition(transform.position))
            {
                return;
            }

            lastValidPosition = transform.position;
            hasLastValidPosition = true;
        }

        private Transform GetMovementCameraTransform()
        {
            if (movementCamera != null)
            {
                return movementCamera.transform;
            }

            Camera mainCamera = Camera.main;
            return mainCamera?.transform;
        }

    }
}
