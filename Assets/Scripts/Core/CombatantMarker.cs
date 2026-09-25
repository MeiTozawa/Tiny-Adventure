using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ユニットの陣営、安定した識別子、命中レイヤー、基本体力コンポーネントを保持する実体のルートマーカーです。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class CombatantMarker : MonoBehaviour, ICombatant
    {
        public enum CombatantFaction
        {
            Player,
            Enemy
        }

        [Header("戦闘対象")]
        [SerializeField]
        private CombatantFaction faction = CombatantFaction.Player;

        [SerializeField]
        private string combatantId = "Knight";

        [SerializeField, Min(0)]
        private int hitLayer;

        [SerializeField]
        private HealthComponent healthComponent;

        private IKnockbackReceiver knockbackReceiver;
        private IHitFlashReceiver flashReceiver;
        private IHitAnimationReceiver animationReceiver;
        private Animator targetAnimator;

        /// <summary>エンティティがヒットフィードバックを受信したときに通知されます。</summary>
        public event Action<CombatFeedbackRequest> HitFeedbackReceived;

        public CombatantMarker Marker => this;
        public CombatantFaction Faction => faction;
        public string CombatantId => combatantId;
        public int HitLayer => hitLayer;
        public HealthComponent Health => healthComponent;
        public IKnockbackReceiver KnockbackReceiver => knockbackReceiver;
        public IHitFlashReceiver FlashReceiver => flashReceiver;
        public IHitAnimationReceiver AnimationReceiver => animationReceiver;
        public Animator TargetAnimator => targetAnimator;

        /// <summary>ヒットフィードバック要求をエンティティのリスナーへ配信します。</summary>
        public void DispatchHitFeedback(CombatFeedbackRequest request)
        {
            HitFeedbackReceived?.Invoke(request);
        }

        private void Awake()
        {
            healthComponent = GetComponent<HealthComponent>();
            knockbackReceiver = GetComponent<IKnockbackReceiver>();
            flashReceiver = GetComponentInChildren<IHitFlashReceiver>();
            animationReceiver = GetComponentInChildren<IHitAnimationReceiver>();
            targetAnimator = GetComponentInChildren<Animator>();
        }

        /// <summary>有効かつアクティブなゲームオブジェクトだけを戦闘候補にします。</summary>
        public bool IsAvailableForCombat => isActiveAndEnabled && gameObject.activeInHierarchy;

        /// <summary>登録前に必要な安定識別子の契約を満たすかを返します。</summary>
        public bool IsIdentityValid => !string.IsNullOrWhiteSpace(combatantId);

        private void Reset()
        {
            hitLayer = gameObject.layer;
            healthComponent = GetComponent<HealthComponent>();
        }

        /// <summary>識別情報を設定します。</summary>
        internal void SetIdentity(CombatantFaction newFaction, string newCombatantId)
        {
            faction = newFaction;
            combatantId = newCombatantId;
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(combatantId))
            {
                combatantId = "Knight";
            }

            hitLayer = Mathf.Max(0, hitLayer);
        }
    }
}
