using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// カメラ基準の入力でKnightを移動し、接地と重力、および攻撃突進（Lunge）を制御します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float DirectionEpsilon = 0.0001f;

        [Header("参照")]
        [SerializeField] private InputReader inputReader;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private FirstPersonViewmodelController viewmodelController;

        [Header("ステータス設定")]
        [Tooltip("キャラクターの基礎ステータスアセットです。")]
        [SerializeField] private CharacterStatsConfig statsConfig;

        [Header("物理・重力設定")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float groundedVerticalSpeed = -2f;

        [Header("攻撃突進設定")]
        [Tooltip("攻撃突進中のプレイヤー入力による方向転換（ステアリング）影響倍率です。")]
        [SerializeField, Range(0f, 1f)] private float lungeSteeringMultiplier = 0.15f;

        private Camera movementCamera;
        private Transform movementCameraTransform;
        private float verticalVelocity;
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

        public float MoveSpeed
        {
            get => moveSpeedOverride ?? (statsConfig != null ? statsConfig.MoveSpeed : 4f);
            set => moveSpeedOverride = Mathf.Max(0f, value);
        }

        public Vector3 WorldMoveDirection { get; private set; }
        public float NormalizedMoveAmount { get; private set; }
        public bool IsMoving => WorldMoveDirection.sqrMagnitude > DirectionEpsilon;
        public CharacterController CharacterController => characterController;
        public FirstPersonViewmodelController ViewmodelController => viewmodelController;
        public bool IsLunging => isLunging;
        public Vector3 LastLungeMotion { get; private set; }

        private void Awake()
        {
            characterController ??= GetComponent<CharacterController>();
            movementCamera = Camera.main;
            if (movementCamera != null)
            {
                movementCameraTransform = movementCamera.transform;
            }
        }

        private void Update()
        {
            if (inputReader == null) return;

            GameplayInputSnapshot input = inputReader.ReadSnapshot();
            ProcessMovement(input.Move, Time.deltaTime);
        }

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

        public void CancelLunge()
        {
            isLunging = false;
            lungeRemainingTime = 0f;
        }

        public void ProcessMovement(Vector2 moveInput, float deltaTime)
        {
            if (characterController == null) return;

            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            Vector2 normalizedInput = Vector2.ClampMagnitude(moveInput, 1f);
            NormalizedMoveAmount = normalizedInput.magnitude;

            if (movementCameraTransform == null && movementCamera != null)
            {
                movementCameraTransform = movementCamera.transform;
            }

            WorldMoveDirection = GetCameraRelativeDirection(normalizedInput, movementCameraTransform);
            viewmodelController?.SetMovementState(IsMoving, NormalizedMoveAmount);

            // 接地と垂直速度
            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedVerticalSpeed;
            }
            else
            {
                verticalVelocity += gravity * safeDeltaTime;
            }

            // 水平移動（突進 または 通常歩行）
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
                horizontalMotion = lungeDisplacement + (WorldMoveDirection * (MoveSpeed * lungeSteeringMultiplier * safeDeltaTime));
            }
            else
            {
                isLunging = false;
                LastLungeMotion = Vector3.zero;
                horizontalMotion = WorldMoveDirection * (MoveSpeed * safeDeltaTime);
            }

            Vector3 totalMotion = horizontalMotion + Vector3.up * (verticalVelocity * safeDeltaTime);
            characterController.Move(totalMotion);
        }

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

            if (forward.sqrMagnitude <= DirectionEpsilon) forward = Vector3.forward;
            if (right.sqrMagnitude <= DirectionEpsilon) right = Vector3.right;

            forward.Normalize();
            right.Normalize();
            return Vector3.ClampMagnitude(right * clampedInput.x + forward * clampedInput.y, 1f);
        }
    }
}
