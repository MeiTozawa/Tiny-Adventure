using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視点における武器（Viewmodel）のカメラ追従、画面配置、
    /// マウス視線慣性（Look Sway）、歩行微振動（Bobbing）を制御します。
    /// 画面右下の視口領域に武器を安定して維持し、カメラの完全な仰俯角（Pitch）に同期させます。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FirstPersonViewmodelController : MonoBehaviour
    {
        [Header("カメラ参照")]
        [Tooltip("追従対象の主カメラです。未設定時はCamera.mainを自動取得します。")]
        [SerializeField]
        private Camera targetCamera;

        [Header("基準視口オフセット (Resting Offset)")]
        [Tooltip("カメラローカル空間における武器の基準待機位置です。")]
        [SerializeField]
        private Vector3 defaultPositionOffset = new Vector3(0.24f, -0.20f, 0.48f);

        [Tooltip("カメラローカル空間における武器の基準回転角度（オイラー角）です。")]
        [SerializeField]
        private Vector3 defaultRotationOffset = new Vector3(5f, -12f, 4f);

        [Header("視線慣性 (Look Sway)")]
        [Tooltip("マウス移動による武器の遅延追従量です。")]
        [SerializeField, Min(0f)]
        private float swayAmount = 0.0015f;

        [Tooltip("Swayによる最大位置変位量（メートル）です。")]
        [SerializeField, Min(0f)]
        private float maxSwayDistance = 0.035f;

        [Tooltip("マウス移動による武器の回転傾き量です。")]
        [SerializeField, Min(0f)]
        private float swayRotationAmount = 0.12f;

        [Tooltip("Swayによる最大回転角度（度）です。")]
        [SerializeField, Min(0f)]
        private float maxSwayAngle = 7f;

        [Tooltip("Swayの回復追従速度です。")]
        [SerializeField, Min(0.1f)]
        private float swaySmoothness = 10f;

        [Header("歩行・待機振動 (Bobbing)")]
        [Tooltip("歩行時の上下振動周波数です。")]
        [SerializeField, Min(0f)]
        private float walkBobFrequency = 9f;

        [Tooltip("歩行時の左右振動振幅です。")]
        [SerializeField, Min(0f)]
        private float walkBobHorizontalAmplitude = 0.007f;

        [Tooltip("歩行時の上下振動振幅です。")]
        [SerializeField, Min(0f)]
        private float walkBobVerticalAmplitude = 0.010f;

        [Tooltip("待機時の呼吸振動周波数です。")]
        [SerializeField, Min(0f)]
        private float idleBobFrequency = 2f;

        [Tooltip("待機時の呼吸振動振幅です。")]
        [SerializeField, Min(0f)]
        private float idleBobAmplitude = 0.0015f;

        private Vector3 currentSwayPos;
        private Quaternion currentSwayRot = Quaternion.identity;
        private Vector3 targetSwayPos;
        private Quaternion targetSwayRot = Quaternion.identity;

        private bool isMoving;
        private float movementSpeedFactor;
        private float bobTimer;

        /// <summary>武器の基準位置オフセットです。</summary>
        public Vector3 DefaultPositionOffset
        {
            get => defaultPositionOffset;
            set => defaultPositionOffset = value;
        }

        /// <summary>武器の基準回転オフセットです。</summary>
        public Vector3 DefaultRotationOffset
        {
            get => defaultRotationOffset;
            set => defaultRotationOffset = value;
        }

        /// <summary>Swayの最大許容変位距離です。</summary>
        public float MaxSwayDistance => maxSwayDistance;

        /// <summary>
        /// 追従対象のカメラを手動設定します（テストやカメラ初期化時に使用）。
        /// </summary>
        public void SetTargetCamera(Camera cam)
        {
            targetCamera = cam;
        }

        /// <summary>
        /// プレイヤーの移動状態と速度割合を設定します。
        /// </summary>
        public void SetMovementState(bool moving, float speedFactor = 1f)
        {
            isMoving = moving;
            movementSpeedFactor = Mathf.Clamp01(speedFactor);
        }

        /// <summary>
        /// マウスの視線移動量を受け取り、武器の慣性変位（Sway）を計算します。
        /// </summary>
        public void ApplyLookInput(Vector2 lookDelta)
        {
            float targetX = Mathf.Clamp(-lookDelta.x * swayAmount, -maxSwayDistance, maxSwayDistance);
            float targetY = Mathf.Clamp(-lookDelta.y * swayAmount, -maxSwayDistance, maxSwayDistance);
            targetSwayPos = new Vector3(targetX, targetY, 0f);

            float rotX = Mathf.Clamp(-lookDelta.y * swayRotationAmount, -maxSwayAngle, maxSwayAngle);
            float rotY = Mathf.Clamp(-lookDelta.x * swayRotationAmount, -maxSwayAngle, maxSwayAngle);
            float rotZ = Mathf.Clamp(lookDelta.x * swayRotationAmount * 0.4f, -maxSwayAngle, maxSwayAngle);
            targetSwayRot = Quaternion.Euler(rotX, rotY, rotZ);
        }

        /// <summary>
        /// 視口武器のワールド位置と姿勢を更新・評価します。
        /// </summary>
        public void Evaluate(float deltaTime)
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                return;
            }

            // Sway の平滑補間
            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
            currentSwayPos = Vector3.Lerp(currentSwayPos, targetSwayPos, safeDeltaTime * swaySmoothness);
            currentSwayRot = Quaternion.Slerp(currentSwayRot, targetSwayRot, safeDeltaTime * swaySmoothness);
            targetSwayPos = Vector3.Lerp(targetSwayPos, Vector3.zero, safeDeltaTime * 4f);
            targetSwayRot = Quaternion.Slerp(targetSwayRot, Quaternion.identity, safeDeltaTime * 4f);

            // Bobbing の計算
            Vector3 bobOffset = CalculateBobbing(safeDeltaTime);

            // カメラ空間からワールド空間への変換
            Transform camTransform = targetCamera.transform;
            Vector3 localOffset = defaultPositionOffset + currentSwayPos + bobOffset;
            Quaternion localRotation = Quaternion.Euler(defaultRotationOffset) * currentSwayRot;

            transform.position = camTransform.TransformPoint(localOffset);
            transform.rotation = camTransform.rotation * localRotation;
        }

        private Vector3 CalculateBobbing(float deltaTime)
        {
            if (isMoving && movementSpeedFactor > 0.05f)
            {
                bobTimer += deltaTime * walkBobFrequency * movementSpeedFactor;
                float x = Mathf.Cos(bobTimer * 0.5f) * walkBobHorizontalAmplitude;
                float y = Mathf.Sin(bobTimer) * walkBobVerticalAmplitude;
                return new Vector3(x, y, 0f);
            }
            else
            {
                bobTimer += deltaTime * idleBobFrequency;
                float y = Mathf.Sin(bobTimer) * idleBobAmplitude;
                return new Vector3(0f, y, 0f);
            }
        }

        private void LateUpdate()
        {
            Evaluate(Time.deltaTime);
        }
    }
}
