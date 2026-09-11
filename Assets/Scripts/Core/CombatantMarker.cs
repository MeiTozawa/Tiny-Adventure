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

        public CombatantMarker Marker => this;
        public CombatantFaction Faction => faction;
        public string CombatantId => combatantId;
        public int HitLayer => hitLayer;

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

        /// <summary>テスト用の設定差し替えメソッドです。</summary>
        public void ConfigureForTests(CombatantFaction testFaction, string testCombatantId)
        {
            faction = testFaction;
            combatantId = testCombatantId;
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
