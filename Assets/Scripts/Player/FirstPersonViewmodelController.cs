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
    [DisallowMultipleComponent]
    public sealed class FirstPersonViewmodelController : MonoBehaviour, IHitStopParticipant
    {
        [SerializeField]
        private Camera targetCamera;
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

        [Tooltip("受撃反動の位置インパルス量（X: 横, Y: 上下, Z: 前後）です。")]
        [SerializeField]
        private Vector3 joltPositionImpulse = new Vector3(0.035f, -0.045f, -0.035f);

        [Tooltip("受撃反動の回転インパルス量（ピッチ, ヨー, ロール）です。")]
        [SerializeField]
        private Vector3 joltRotationImpulse = new Vector3(-6f, 6f, 9f);

        [Header("出刀運動学データ設定 (Attack Kinetics Config)")]
        [Tooltip("出刀軌跡およびタイミングを定義するScriptableObjectデータ資産です。")]
        [SerializeField]
        private ViewmodelAttackKineticsConfig attackKineticsConfig;

        [SerializeField]
        private ViewmodelSwayAndBob swayAndBob = new();

        private ViewmodelAttackKinetics attackKinetics;

        [Header("武器視覚参照 (Visual References)")]
        [Tooltip("刀光トレイル。")]
        [SerializeField]
        private TrailRenderer swordTrail;

        [Tooltip("武器メッシュレンダラー。")]
        [SerializeField]
        private Renderer swordRenderer;

        [Header("刀身視覚効果設定 (Blade Visuals Config)")]
        [SerializeField]
        private BladeVisualsConfig bladeVisuals = BladeVisualsConfig.Default;

        private readonly ViewmodelBladeVisuals bladeVisualsModule = new();

        private Vector3 currentJoltPos;
        private Quaternion currentJoltRot = Quaternion.identity;

        public ViewmodelAttackKineticsConfig AttackKineticsConfig
        {
            get => attackKineticsConfig;
            set
            {
                attackKineticsConfig = value;
                AttackKinetics.Configure(value);
            }
        }

        public float BaseAttackDuration => AttackKinetics.BaseAttackDuration;
        public bool IsAttacking => AttackKinetics.IsAttacking;
        public int CurrentAttackComboIndex => AttackKinetics.CurrentComboIndex;
        public float AttackProgress => AttackKinetics.AttackProgress;
        public bool IsInDamageWindow => AttackKinetics.IsInDamageWindow;
        public bool IsHitStopParticipant => isActiveAndEnabled;
        public bool IsHitStopPaused => AttackKinetics.IsHitStopPaused;
        public TrailRenderer SwordTrail => swordTrail;
        public Vector3 CurrentJoltPositionOffset => currentJoltPos;
        public Quaternion CurrentJoltRotationOffset => currentJoltRot;
        public bool IsJolting => currentJoltPos.sqrMagnitude > 0.00005f || Quaternion.Angle(currentJoltRot, Quaternion.identity) > 0.05f;

        /// <summary>
        /// 視口武器が出刀中である場合、その正規化進行度（0.0〜1.0）を取得します。
        /// </summary>
        public Result<float> GetAttackNormalizedTime()
        {
            if (AttackKinetics.IsAttacking)
            {
                return AttackKinetics.AttackProgress;
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

        public float MaxSwayDistance => SwayAndBob.MaxSwayDistance;
        public ViewmodelSwayAndBob SwayAndBob => swayAndBob;
        public ViewmodelAttackKinetics AttackKinetics => attackKinetics;
        public ViewmodelBladeVisuals BladeVisuals => bladeVisualsModule;
        public BladeVisualsConfig BladeVisualsConfig => bladeVisuals;

        [Inject]
        public void Construct(HitStopController hitStop = null)
        {
            hitStopController = hitStop;
        }

        private Transform targetCameraTransform;

        public void SetTargetCamera(Camera cam)
        {
            targetCamera = cam;
            targetCameraTransform = cam.transform;
        }

        private void Awake()
        {
            targetCameraTransform = targetCamera.transform;
            attackKinetics = new ViewmodelAttackKinetics(attackKineticsConfig);
            attackKinetics.Configure(attackKineticsConfig);
            bladeVisualsModule.Initialize(swordRenderer, swordTrail, bladeVisuals);
        }

        private void OnValidate()
        {
            attackKinetics?.Configure(attackKineticsConfig);
        }

        private void OnEnable()
        {
            hitStopController?.RegisterParticipant(this);
        }

        private void OnDisable()
        {
            hitStopController?.UnregisterParticipant(this);

            bladeVisualsModule.OnDisabled();
            attackKinetics.CancelAttack();
            swayAndBob.Reset();
            ResetImpactJolt();
        }

        public void BeginHitStop(HitStopToken token)
        {
            AttackKinetics.BeginHitStop();
        }

        public void EndHitStop(HitStopToken token)
        {
            AttackKinetics.EndHitStop();
        }

        public void TriggerAttack(
            int comboIndex,
            float speedMultiplier,
            float strikeOpen,
            float strikeClose)
        {
            AttackKinetics.TriggerAttack(comboIndex, speedMultiplier, strikeOpen, strikeClose);
            BladeVisuals.OnAttackStarted();
        }

        public void TriggerAttack(int comboIndex, float speedMultiplier)
        {
            TriggerAttack(comboIndex, speedMultiplier, 0f, 1f);
        }

        public void CancelAttack()
        {
            AttackKinetics.CancelAttack();
            BladeVisuals.OnAttackEnded();
        }

        public void SetMovementState(bool moving, float speedFactor = 1f)
        {
            SwayAndBob.SetMovementState(moving, speedFactor);
        }

        public void ApplyLookInput(Vector2 lookDelta)
        {
            SwayAndBob.ApplyLookInput(lookDelta);
        }

        public void TriggerImpactJolt(Vector3 localDirection, float intensity = 1f)
        {
            float safeIntensity = intensity;
            Vector3 dir = localDirection.sqrMagnitude > 0.001f ? localDirection.normalized : Vector3.back;

            currentJoltPos = new Vector3(
                dir.x * joltPositionImpulse.x * safeIntensity,
                joltPositionImpulse.y * safeIntensity,
                joltPositionImpulse.z * safeIntensity
            );

            currentJoltRot = Quaternion.Euler(
                joltRotationImpulse.x * safeIntensity,
                dir.x * joltRotationImpulse.y * safeIntensity,
                -dir.x * joltRotationImpulse.z * safeIntensity
            );
        }

        public void ResetImpactJolt()
        {
            currentJoltPos = Vector3.zero;
            currentJoltRot = Quaternion.identity;
        }

        public void Evaluate(float deltaTime)
        {
            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);

            SwayAndBob.Evaluate(safeDeltaTime, out Vector3 currentSwayPos, out Quaternion currentSwayRot, out Vector3 bobOffset);

            currentJoltPos = Vector3.Lerp(currentJoltPos, Vector3.zero, safeDeltaTime * joltRecoverSpeed);
            currentJoltRot = Quaternion.Slerp(currentJoltRot, Quaternion.identity, safeDeltaTime * joltRecoverSpeed);
            if (currentJoltPos.sqrMagnitude < 0.00005f)
            {
                currentJoltPos = Vector3.zero;
            }
            if (Quaternion.Angle(currentJoltRot, Quaternion.identity) < 0.01f)
            {
                currentJoltRot = Quaternion.identity;
            }

            AttackKinetics.Evaluate(safeDeltaTime, out Vector3 attackOffsetPos, out Quaternion attackOffsetRot, out float progress, out bool justCompleted);
            if (AttackKinetics.IsAttacking)
            {
                BladeVisuals.UpdateBladeGlow(progress);
            }
            if (justCompleted)
            {
                BladeVisuals.OnAttackEnded();
            }

            Transform camTransform = targetCameraTransform;
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
