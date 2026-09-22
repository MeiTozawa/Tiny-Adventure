using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ユニットの陣営、安定した識別子、命中レイヤーを保持します。
    /// </summary>
    [DisallowMultipleComponent]
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

        public CombatantMarker Marker => this;
        public CombatantFaction Faction => faction;
        public string CombatantId => combatantId;
        public int HitLayer => hitLayer;
        public HealthComponent Health => healthComponent != null ? healthComponent : (healthComponent = GetComponent<HealthComponent>());

        private void Awake()
        {
            if (healthComponent == null)
            {
                healthComponent = GetComponent<HealthComponent>();
            }
        }

        /// <summary>有効かつアクティブなゲームオブジェクトだけを戦闘候補にします。</summary>
        public bool IsAvailableForCombat => isActiveAndEnabled && gameObject.activeInHierarchy;

        /// <summary>登録前に必要な安定識別子の契約を満たすかを返します。</summary>
        public bool IsIdentityValid => !string.IsNullOrWhiteSpace(combatantId);

        /// <summary>登録前に識別子の契約を日本語診断で検証します。</summary>
        public bool TryValidateIdentity(out string diagnostic)
        {
            if (IsIdentityValid)
            {
                diagnostic = string.Empty;
                return true;
            }

            diagnostic = "戦闘対象IDが設定されていません。";
            return false;
        }

        private void Reset()
        {
            hitLayer = gameObject.layer;
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
