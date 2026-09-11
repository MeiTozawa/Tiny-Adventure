using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// カメラ基準の入力でKnightを移動し、接地とプレイ可能領域を安全に維持します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float MinimumMoveSpeed = 0.01f;
        private const float DirectionEpsilon = 0.0001f;

        [Header("参照")]
        [SerializeField]
        private InputReader inputReader;

        [SerializeField]
        private Camera movementCamera;

        [Tooltip("Knightの移動を許可する領域を表すColliderです。未設定時は地面接触のみを検査します。")]
        [SerializeField]
        private Collider playableArea;

        [Header("ステータス設定")]
        [Tooltip("キャラクターの基礎ステータスアセットです。未設定時は下記のmoveSpeedを使用します。")]
        [SerializeField]
        private CharacterStatsConfigSO statsConfig;

        [Header("移動")]
        [SerializeField, Min(MinimumMoveSpeed)]
        private float moveSpeed = 5f;

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

        private CharacterController characterController;
        private float verticalVelocity;
        private Vector3 lastValidPosition;
        private bool hasLastValidPosition;
        private bool diagnosticReported;
        private float? moveSpeedOverride;

        public CharacterStatsConfigSO StatsConfig
        {
            get => statsConfig;
            set => statsConfig = value;
        }

        /// <summary>実際に用いる正の移動速度です。StatsConfig設定時はそちらを優先します。</summary>
        public float MoveSpeed
        {
            get => moveSpeedOverride ?? (statsConfig != null ? statsConfig.MoveSpeed : moveSpeed);
            set => moveSpeedOverride = Mathf.Max(MinimumMoveSpeed, value);
        }

        /// <summary>現在の水平方向入力を変換したワールド移動方向です。</summary>
        public Vector3 WorldMoveDirection { get; private set; }

        /// <summary>正規化後の入力強度です。</summary>
        public float NormalizedMoveAmount { get; private set; }

        /// <summary>現在水平方向に移動しているかを示します。</summary>
        public bool IsMoving => WorldMoveDirection.sqrMagnitude > DirectionEpsilon;

        /// <summary>最後に地面と領域の両方で検証できた安全な位置です。</summary>
        public Vector3 LastValidPosition => lastValidPosition;

        public event Action<string> DiagnosticReported;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            ResolveReferences();
            CaptureCurrentPositionIfSafe();
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(MinimumMoveSpeed, moveSpeed);
            groundedVerticalSpeed = Mathf.Min(0f, groundedVerticalSpeed);
            groundProbeStartHeight = Mathf.Max(0.01f, groundProbeStartHeight);
            groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
        }

        private void Update()
        {
            if (characterController == null)
            {
                ReportFailure("PlayerControllerにCharacterControllerがありません。Knightの移動を停止しました。");
                enabled = false;
                return;
            }

            ResolveReferences();
            GameplayInputSnapshot input = inputReader != null ? inputReader.ReadSnapshot() : default;
            ProcessMovement(input.Move, Time.deltaTime);
        }

        /// <summary>
        /// 移動入力をカメラ基準の水平移動と重力へ変換します。移動入力はPlayerのyawを変更しません。
        /// </summary>
        public void ProcessMovement(Vector2 moveInput, float deltaTime)
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
                if (characterController == null)
                {
                    ReportFailure("PlayerControllerにCharacterControllerがありません。Knightの移動を停止しました。");
                    return;
                }
            }

            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            Vector2 normalizedInput = Vector2.ClampMagnitude(moveInput, 1f);
            NormalizedMoveAmount = normalizedInput.magnitude;
            WorldMoveDirection = GetCameraRelativeDirection(normalizedInput, GetMovementCameraTransform());

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedVerticalSpeed;
            }

            verticalVelocity += gravity * safeDeltaTime;
            Vector3 requestedMotion = (WorldMoveDirection * moveSpeed + Vector3.up * verticalVelocity) * safeDeltaTime;
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
            return mainCamera != null ? mainCamera.transform : null;
        }

        private void ResolveReferences()
        {
            if (inputReader == null)
            {
                inputReader = GetComponent<InputReader>();
                if (inputReader == null)
                {
                    inputReader = FindAnyObjectByType<InputReader>();
                }
            }

            if (movementCamera == null)
            {
                movementCamera = Camera.main;
            }
        }

        private void ReportFailure(string message)
        {
            if (diagnosticReported)
            {
                return;
            }

            diagnosticReported = true;
            Debug.LogError($"[プレイヤー移動診断] {message}", this);
            DiagnosticReported?.Invoke(message);
        }
    }
}
