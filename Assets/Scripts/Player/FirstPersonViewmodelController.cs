using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視点における武器（Viewmodel）のカメラ追従、画面配置、受撃衝撃反動（Impact Jolt）を制御し、
    /// 視線慣性・微動（ViewmodelSwayAndBob）、出刀運動学（ViewmodelAttackKinetics）、
    /// 刀身視覚効果（ViewmodelBladeVisuals）を統括・合成する軽量な Facade / Coordinator です。
    /// 単一責任：カメラ相対トランスフォームの合成とサブモジュールのライフサイクル統括。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FirstPersonViewmodelController : MonoBehaviour, IHitStopParticipant
    {
        [Header("カメラ参照")]
        [Tooltip("追従対象の主カメラです。未設定時はCamera.mainを自動取得します。")]
        [SerializeField]
        private Camera targetCamera;

        [SerializeField]
        private HitStopController hitStopController;

        [Header("基準視口オフセット (Resting Offset)")]
        [Tooltip("カメラローカル空間における武器の基準待機位置です。")]
        [SerializeField]
        private Vector3 defaultPositionOffset = new Vector3(0.24f, -0.22f, 0.48f);

        [Tooltip("カメラローカル空間における武器の基準回転角度（オイラー角）です。")]
        [SerializeField]
        private Vector3 defaultRotationOffset = new Vector3(55f, 65f, 50f);

        [Header("受撃慣性反動 (Impact Jolt)")]
        [Tooltip("受撃時の武器沈下・後退・側傾インパルスの復帰速度です。")]
        [SerializeField, Min(0.1f)]
        private float joltRecoverSpeed = 12f;

        [Header("サブモジュール (Delegates)")]
        [SerializeField]
        private ViewmodelSwayAndBob swayAndBob = new ViewmodelSwayAndBob();

        [SerializeField]
        private ViewmodelAttackKinetics attackKinetics = new ViewmodelAttackKinetics();

        [SerializeField]
        private ViewmodelBladeVisuals bladeVisuals = new ViewmodelBladeVisuals();

        private Vector3 currentJoltPos;
        private Quaternion currentJoltRot = Quaternion.identity;

        public float BaseAttackDuration => attackKinetics.BaseAttackDuration;
        public bool IsAttacking => attackKinetics.IsAttacking;
        public int CurrentAttackComboIndex => attackKinetics.CurrentComboIndex;
        public float AttackProgress => attackKinetics.AttackProgress;
        public bool IsInDamageWindow => attackKinetics.IsInDamageWindow;
        public bool IsHitStopParticipant => isActiveAndEnabled;
        public bool IsHitStopPaused => attackKinetics.IsHitStopPaused;
        public TrailRenderer SwordTrail => bladeVisuals.SwordTrail;
        public Vector3 CurrentJoltPositionOffset => currentJoltPos;
        public Quaternion CurrentJoltRotationOffset => currentJoltRot;
        public bool IsJolting => currentJoltPos.sqrMagnitude > 0.00005f || Quaternion.Angle(currentJoltRot, Quaternion.identity) > 0.05f;

        /// <summary>
        /// 視口武器が出刀中である場合、その正規化進行度（0.0〜1.0）を取得します。
        /// </summary>
        public Result<float> GetAttackNormalizedTime()
        {
            if (attackKinetics.IsAttacking)
            {
                return attackKinetics.AttackProgress;
            }

            return GameError.InvalidState;
        }

        public Vector3 DefaultPositionOffset
        {
            get => defaultPositionOffset;
            set => defaultPositionOffset = value;
        }

        public Vector3 DefaultRotationOffset
        {
            get => defaultRotationOffset;
            set => defaultRotationOffset = value;
        }

        public float MaxSwayDistance => swayAndBob.MaxSwayDistance;
        public ViewmodelSwayAndBob SwayAndBob => swayAndBob;
        public ViewmodelAttackKinetics AttackKinetics => attackKinetics;
        public ViewmodelBladeVisuals BladeVisuals => bladeVisuals;

        [Inject]
        public void Construct(HitStopController hitStop = null)
        {
            if (hitStop != null) hitStopController = hitStop;
        }

        private void Awake()
        {
            bladeVisuals.ResolveVisualReferences(gameObject);
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

            bladeVisuals.OnDisabled();
            attackKinetics.CancelAttack();
            swayAndBob.Reset();
            ResetImpactJolt();
        }

        public void BeginHitStop(HitStopToken token)
        {
            attackKinetics.BeginHitStop();
        }

        public void EndHitStop(HitStopToken token)
        {
            attackKinetics.EndHitStop();
        }

        public void SetTargetCamera(Camera cam)
        {
            targetCamera = cam;
        }

        public void TriggerAttack(
            int comboIndex,
            float speedMultiplier = 1f,
            float strikeOpen = ViewmodelAttackKinetics.DefaultStrikeOpenProgress,
            float strikeClose = ViewmodelAttackKinetics.DefaultStrikeCloseProgress)
        {
            bladeVisuals.ResolveVisualReferences(gameObject);
            attackKinetics.TriggerAttack(comboIndex, speedMultiplier, strikeOpen, strikeClose);
            bladeVisuals.OnAttackStarted();
        }

        public void CancelAttack()
        {
            attackKinetics.CancelAttack();
            bladeVisuals.OnAttackEnded();
        }

        public void SetMovementState(bool moving, float speedFactor = 1f)
        {
            swayAndBob.SetMovementState(moving, speedFactor);
        }

        public void ApplyLookInput(Vector2 lookDelta)
        {
            swayAndBob.ApplyLookInput(lookDelta);
        }

        public void TriggerImpactJolt(Vector3 localDirection, float intensity = 1f)
        {
            float safeIntensity = Mathf.Clamp(intensity, 0.2f, 2.5f);
            Vector3 dir = localDirection.sqrMagnitude > 0.001f ? localDirection.normalized : Vector3.back;

            currentJoltPos = new Vector3(
                dir.x * 0.035f * safeIntensity,
                -0.045f * safeIntensity,
                -0.035f * safeIntensity
            );

            currentJoltRot = Quaternion.Euler(
                -6f * safeIntensity,
                dir.x * 6f * safeIntensity,
                -dir.x * 9f * safeIntensity
            );
        }

        public void ResetImpactJolt()
        {
            currentJoltPos = Vector3.zero;
            currentJoltRot = Quaternion.identity;
        }

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

            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);

            // 1. Sway と Bobbing の評価
            swayAndBob.Evaluate(safeDeltaTime, out Vector3 currentSwayPos, out Quaternion currentSwayRot, out Vector3 bobOffset);

            // 2. Jolt の減衰復帰
            currentJoltPos = Vector3.Lerp(currentJoltPos, Vector3.zero, safeDeltaTime * joltRecoverSpeed);
            currentJoltRot = Quaternion.Slerp(currentJoltRot, Quaternion.identity, safeDeltaTime * joltRecoverSpeed);
            if (currentJoltPos.sqrMagnitude < 0.000005f)
            {
                currentJoltPos = Vector3.zero;
            }
            if (Quaternion.Angle(currentJoltRot, Quaternion.identity) < 0.01f)
            {
                currentJoltRot = Quaternion.identity;
            }

            // 3. 出刀攻撃運動学の評価とエフェクト更新
            attackKinetics.Evaluate(safeDeltaTime, out Vector3 attackOffsetPos, out Quaternion attackOffsetRot, out float progress, out bool justCompleted);
            if (attackKinetics.IsAttacking)
            {
                bladeVisuals.UpdateBladeGlow(progress);
            }
            if (justCompleted)
            {
                bladeVisuals.OnAttackEnded();
            }

            // 4. カメラ空間からワールド空間への変換と合成
            Transform camTransform = targetCamera.transform;
            Vector3 localOffset = defaultPositionOffset + currentSwayPos + bobOffset + attackOffsetPos + currentJoltPos;
            Quaternion localRotation = Quaternion.Euler(defaultRotationOffset) * attackOffsetRot * currentSwayRot * currentJoltRot;

            transform.position = camTransform.TransformPoint(localOffset);
            transform.rotation = camTransform.rotation * localRotation;
        }

        private void LateUpdate()
        {
            Evaluate(Time.deltaTime);
        }
    }
}
