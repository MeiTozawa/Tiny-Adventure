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

        public CombatantMarker Owner => owner;
        public HealthComponent TargetHealth => targetHealth;
        public float DamageMultiplier => damageMultiplier;
        public HurtboxType Type => hurtboxType;
        public Collider HurtboxCollider => hurtboxCollider;

        public bool IsActive =>
            isActiveAndEnabled &&
            hurtboxCollider != null &&
            hurtboxCollider.enabled &&
            (owner == null || owner.IsAvailableForCombat) &&
            (targetHealth == null || targetHealth.IsAlive);

        private void Awake()
        {
            ResolveReferences();
            EnforceTriggerState();
            SubscribeHealth();
        }

        private void OnEnable()
        {
            ResolveReferences();
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
            ResolveReferences();
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

        private void ResolveReferences()
        {
            if (hurtboxCollider == null)
            {
                hurtboxCollider = GetComponent<Collider>();
            }

            if (owner == null)
            {
                owner = GetComponentInParent<CombatantMarker>();
            }

            if (targetHealth == null)
            {
                targetHealth = GetComponentInParent<HealthComponent>();
            }
        }

        private void EnforceTriggerState()
        {
            if (hurtboxCollider != null && !hurtboxCollider.isTrigger)
            {
                hurtboxCollider.isTrigger = true;
            }
        }

        private void SubscribeHealth()
        {
            if (targetHealth != null)
            {
                targetHealth.Died -= HandleDied;
                targetHealth.Died += HandleDied;
            }
        }

        private void UnsubscribeHealth()
        {
            if (targetHealth != null)
            {
                targetHealth.Died -= HandleDied;
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
