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
        private Vector3 defaultPositionOffset = new Vector3(0.24f, -0.22f, 0.48f);

        [Tooltip("カメラローカル空間における武器の基準回転角度（オイラー角）です。")]
        [SerializeField]
        private Vector3 defaultRotationOffset = new Vector3(55f, 65f, 50f);

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

        [Header("出刀アニメーション (Attack Kinetics)")]
        [Tooltip("基準攻撃所要時間（秒）です。")]
        [SerializeField, Min(0.05f)]
        private float baseAttackDuration = 0.35f;

        private bool isAttacking;
        private int currentComboIndex;
        private float attackTimer;
        private float attackDuration;

        private Vector3 currentSwayPos;
        private Quaternion currentSwayRot = Quaternion.identity;
        private Vector3 targetSwayPos;
        private Quaternion targetSwayRot = Quaternion.identity;

        private bool isMoving;
        private float movementSpeedFactor;
        private float bobTimer;

        /// <summary>基準攻撃所要時間です。</summary>
        public float BaseAttackDuration => baseAttackDuration;

        /// <summary>現在出刀攻撃中であるかを示します。</summary>
        public bool IsAttacking => isAttacking;

        /// <summary>現在実行中のコンボ段数（0:横薙ぎ、1:縦斬り、2:突刺）です。</summary>
        public int CurrentAttackComboIndex => currentComboIndex;

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
        /// 指定されたコンボ段数と速度倍率で視口武器の出刀を開始します。
        /// </summary>
        public void TriggerAttack(int comboIndex, float speedMultiplier = 1f)
        {
            currentComboIndex = Mathf.Clamp(comboIndex, 0, 2);
            float speed = Mathf.Max(0.1f, speedMultiplier);
            attackDuration = baseAttackDuration / speed;
            attackTimer = 0f;
            isAttacking = true;
        }

        /// <summary>
        /// 出刀動作を中断し、待機姿勢へ即座に復帰します。
        /// </summary>
        public void CancelAttack()
        {
            isAttacking = false;
            attackTimer = 0f;
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

            // 出刀攻撃アニメーションの計算
            Vector3 attackOffsetPos = Vector3.zero;
            Quaternion attackOffsetRot = Quaternion.identity;

            if (isAttacking)
            {
                attackTimer += safeDeltaTime;
                float progress = Mathf.Clamp01(attackTimer / attackDuration);
                CalculateAttackMotion(currentComboIndex, progress, out attackOffsetPos, out attackOffsetRot);
                if (progress >= 1f)
                {
                    isAttacking = false;
                }
            }

            // カメラ空間からワールド空間への変換
            Transform camTransform = targetCamera.transform;
            Vector3 localOffset = defaultPositionOffset + currentSwayPos + bobOffset + attackOffsetPos;
            Quaternion localRotation = Quaternion.Euler(defaultRotationOffset) * attackOffsetRot * currentSwayRot;

            transform.position = camTransform.TransformPoint(localOffset);
            transform.rotation = camTransform.rotation * localRotation;
        }

        private static void CalculateAttackMotion(int comboIndex, float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            switch (comboIndex)
            {
                case 0:
                    CalculateHorizontalSlash(progress, out offsetPos, out offsetRot);
                    break;
                case 1:
                    CalculateVerticalSlash(progress, out offsetPos, out offsetRot);
                    break;
                case 2:
                    CalculateThrust(progress, out offsetPos, out offsetRot);
                    break;
                default:
                    offsetPos = Vector3.zero;
                    offsetRot = Quaternion.identity;
                    break;
            }
        }

        private static void CalculateHorizontalSlash(float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < 0.12f)
            {
                // 蓄勢（右へ微小に引く）
                float t = progress / 0.12f;
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(0.06f, 0.02f, -0.02f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, -15f, -5f), t);
            }
            else if (progress < 0.42f)
            {
                // 横薙ぎ一閃（右から左へ一気に薙ぎ払う）
                float t = (progress - 0.12f) / 0.30f;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 windup = new Vector3(0.06f, 0.02f, -0.02f);
                Vector3 peak = new Vector3(-0.36f, -0.04f, 0.10f);
                offsetPos = Vector3.Lerp(windup, peak, easedT);
                Quaternion windupRot = Quaternion.Euler(0f, -15f, -5f);
                Quaternion peakRot = Quaternion.Euler(-10f, 45f, 15f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, easedT);
            }
            else
            {
                // 待機姿勢へ復帰
                float t = (progress - 0.42f) / 0.58f;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 peak = new Vector3(-0.36f, -0.04f, 0.10f);
                offsetPos = Vector3.Lerp(peak, Vector3.zero, easedT);
                Quaternion peakRot = Quaternion.Euler(-10f, 45f, 15f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, easedT);
            }
        }

        private static void CalculateVerticalSlash(float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < 0.15f)
            {
                // 振り上げ
                float t = progress / 0.15f;
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(0.04f, 0.14f, -0.04f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(25f, 5f, 0f), t);
            }
            else if (progress < 0.45f)
            {
                // 振り下ろし（上方から下方へ袈裟斬り）
                float t = (progress - 0.15f) / 0.30f;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 windup = new Vector3(0.04f, 0.14f, -0.04f);
                Vector3 peak = new Vector3(-0.08f, -0.18f, 0.08f);
                offsetPos = Vector3.Lerp(windup, peak, easedT);
                Quaternion windupRot = Quaternion.Euler(25f, 5f, 0f);
                Quaternion peakRot = Quaternion.Euler(-35f, -10f, 0f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, easedT);
            }
            else
            {
                // 復帰
                float t = (progress - 0.45f) / 0.55f;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 peak = new Vector3(-0.08f, -0.18f, 0.08f);
                offsetPos = Vector3.Lerp(peak, Vector3.zero, easedT);
                Quaternion peakRot = Quaternion.Euler(-35f, -10f, 0f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, easedT);
            }
        }

        private static void CalculateThrust(float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < 0.15f)
            {
                // 照準と引き絞り
                float t = progress / 0.15f;
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(-0.12f, 0.05f, -0.06f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, -8f, 0f), t);
            }
            else if (progress < 0.45f)
            {
                // 直線高速刺突
                float t = (progress - 0.15f) / 0.30f;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 windup = new Vector3(-0.12f, 0.05f, -0.06f);
                Vector3 peak = new Vector3(-0.08f, 0.02f, 0.40f);
                offsetPos = Vector3.Lerp(windup, peak, easedT);
                Quaternion windupRot = Quaternion.Euler(0f, -8f, 0f);
                Quaternion peakRot = Quaternion.Euler(-5f, 0f, 5f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, easedT);
            }
            else
            {
                // 抜刀復帰
                float t = (progress - 0.45f) / 0.55f;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 peak = new Vector3(-0.08f, 0.02f, 0.40f);
                offsetPos = Vector3.Lerp(peak, Vector3.zero, easedT);
                Quaternion peakRot = Quaternion.Euler(-5f, 0f, 5f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, easedT);
            }
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
