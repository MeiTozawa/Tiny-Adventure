using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾判定体（Combat Hurtbox）コンポーネント。
    /// 武器のCombatHitboxからの接触を受け止め、所属するCombatantMarkerおよびHealthComponentへ橋渡しします。
    /// 移動用コライダー（CharacterController等）と戦闘判定を物理層・責務の両面で分離します。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class CombatHurtbox : MonoBehaviour, ICombatHurtbox
    {
        [Header("参照")]
        [Tooltip("被弾判定に使用するトリガーコライダーです。未設定時は自身から自動取得します。")]
        [SerializeField]
        private Collider hurtboxCollider;

        [Tooltip("このHurtboxを所有する参戦者マーカーです。未設定時は親階層から自動取得します。")]
        [SerializeField]
        private CombatantMarker owner;

        [Tooltip("ダメージを適用する生命値コンポーネントです。未設定時は親階層から自動取得します。")]
        [SerializeField]
        private HealthComponent targetHealth;

        [Header("部位設定")]
        [Tooltip("このHurtboxの部位種別です。")]
        [SerializeField]
        private HurtboxType hurtboxType = HurtboxType.Torso;

        [Tooltip("この部位に適用されるダメージ倍率です（通常胴体: 1.0）。")]
        [SerializeField, Min(0.1f)]
        private float damageMultiplier = 1.0f;

        public CombatantMarker Owner
        {
            get
            {
                if (owner == null) owner = GetComponentInParent<CombatantMarker>();
                return owner;
            }
        }

        public HealthComponent TargetHealth
        {
            get
            {
                if (targetHealth == null && Owner != null) targetHealth = Owner.Health ?? Owner.GetComponent<HealthComponent>();
                return targetHealth;
            }
        }

        public float DamageMultiplier => damageMultiplier;
        public HurtboxType Type => hurtboxType;

        public Collider HurtboxCollider
        {
            get
            {
                if (hurtboxCollider == null) hurtboxCollider = GetComponent<Collider>();
                return hurtboxCollider;
            }
        }

        public bool IsActive =>
            isActiveAndEnabled &&
            HurtboxCollider != null &&
            HurtboxCollider.enabled &&
            (Owner == null || Owner.IsAvailableForCombat) &&
            (TargetHealth == null || TargetHealth.IsAlive);

        private void Awake()
        {
            EnforceTriggerState();
            SubscribeHealth();
        }

        private void OnEnable()
        {
            EnforceTriggerState();
            SubscribeHealth();
        }

        private void OnDisable()
        {
            UnsubscribeHealth();
        }

        private void OnDestroy()
        {
            UnsubscribeHealth();
        }

        private void OnValidate()
        {
            EnforceTriggerState();
        }

        /// <summary>
        /// 実行時またはテスト用の明示的初期化メソッドです。
        /// </summary>
        public void Initialize(CombatantMarker newOwner, HealthComponent newHealth, Collider newCollider, HurtboxType newType = HurtboxType.Torso, float newMultiplier = 1.0f)
        {
            UnsubscribeHealth();

            owner = newOwner;
            targetHealth = newHealth;
            hurtboxCollider = newCollider;
            hurtboxType = newType;
            damageMultiplier = Mathf.Max(0.1f, newMultiplier);

            EnforceTriggerState();
            SubscribeHealth();
        }

        private void EnforceTriggerState()
        {
            if (hurtboxCollider == null)
            {
                hurtboxCollider = GetComponent<Collider>();
            }

            if (hurtboxCollider != null && !hurtboxCollider.isTrigger)
            {
                hurtboxCollider.isTrigger = true;
            }
        }

        private void SubscribeHealth()
        {
            var health = TargetHealth;
            if (health != null)
            {
                health.Died -= HandleDied;
                health.Died += HandleDied;
            }
        }

        private void UnsubscribeHealth()
        {
            var health = TargetHealth;
            if (health != null)
            {
                health.Died -= HandleDied;
            }
        }

        private void HandleDied()
        {
            if (hurtboxCollider == null)
            {
                hurtboxCollider = GetComponent<Collider>();
            }

            if (hurtboxCollider != null)
            {
                hurtboxCollider.enabled = false;
            }
        }
    }
}
