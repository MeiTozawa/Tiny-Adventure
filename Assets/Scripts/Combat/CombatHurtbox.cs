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
    [RequireComponent(typeof(Collider))]
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
        private float damageMultiplier;

        public CombatantMarker Owner => owner;
        public HealthComponent TargetHealth => targetHealth;
        public float DamageMultiplier => damageMultiplier;
        public HurtboxType Type => hurtboxType;
        public Collider HurtboxCollider => hurtboxCollider;

        public void Configure(
            Collider col = null,
            CombatantMarker own = null,
            HealthComponent health = null,
            HurtboxType newType = HurtboxType.Torso,
            float newMultiplier = 1.0f)
        {
            if (col != null) hurtboxCollider = col;
            if (own != null) owner = own;
            if (health != null) targetHealth = health;
            hurtboxType = newType;
            if (newMultiplier > 0f) damageMultiplier = Mathf.Max(0.1f, newMultiplier);
            EnforceTriggerState();
            SubscribeHealth();
        }

        public bool IsActive =>
            isActiveAndEnabled &&
            hurtboxCollider.enabled &&
            owner.IsAvailableForCombat &&
            targetHealth.IsAlive;

        private void Awake()
        {
            hurtboxCollider = GetComponent<Collider>();
            owner = GetComponentInParent<CombatantMarker>();
            targetHealth = owner.Health;
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

        private void EnforceTriggerState()
        {
            hurtboxCollider.isTrigger = true;
        }

        private void SubscribeHealth()
        {
            targetHealth.Died -= HandleDied;
            targetHealth.Died += HandleDied;
        }

        private void UnsubscribeHealth()
        {
            targetHealth.Died -= HandleDied;
        }

        private void HandleDied()
        {
            hurtboxCollider.enabled = false;
        }
    }
}
