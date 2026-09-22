using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視口武器の視線慣性（Look Sway）および歩行・待機微動（Bobbing）を計算する独立モジュールです。
    /// 単一責任：マウス入力と移動状態に基づく慣性・振動オフセットの計算。
    /// </summary>
    [Serializable]
    public sealed class ViewmodelSwayAndBob
    {
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

        public float MaxSwayDistance => maxSwayDistance;

        /// <summary>
        /// マウスの視線移動量を受け取り、武器の慣性変位（Sway）目標値を計算します。
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
        /// プレイヤーの移動状態と速度割合を設定します。
        /// </summary>
        public void SetMovementState(bool moving, float speedFactor = 1f)
        {
            isMoving = moving;
            movementSpeedFactor = Mathf.Clamp01(speedFactor);
        }

        /// <summary>
        /// 毎フレームのSwayとBobbingを平滑評価します。
        /// </summary>
        public void Evaluate(float safeDeltaTime, out Vector3 swayPos, out Quaternion swayRot, out Vector3 bobOffset)
        {
            currentSwayPos = Vector3.Lerp(currentSwayPos, targetSwayPos, safeDeltaTime * swaySmoothness);
            currentSwayRot = Quaternion.Slerp(currentSwayRot, targetSwayRot, safeDeltaTime * swaySmoothness);
            targetSwayPos = Vector3.Lerp(targetSwayPos, Vector3.zero, safeDeltaTime * 4f);
            targetSwayRot = Quaternion.Slerp(targetSwayRot, Quaternion.identity, safeDeltaTime * 4f);

            swayPos = currentSwayPos;
            swayRot = currentSwayRot;
            bobOffset = CalculateBobbing(safeDeltaTime);
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

        public void Reset()
        {
            currentSwayPos = Vector3.zero;
            currentSwayRot = Quaternion.identity;
            targetSwayPos = Vector3.zero;
            targetSwayRot = Quaternion.identity;
            bobTimer = 0f;
        }
    }
}
