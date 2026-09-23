using System.Collections.Generic;
using UnityEngine.Assertions;

namespace TinyAdventure
{
    /// <summary>
    /// シーン全体のレジストリが存在しない場合のフォールバック用インメモリ戦闘対象登録簿です。
    /// </summary>
    public sealed class LocalCombatantRegistry : ICombatantRegistry
    {
        private readonly HashSet<CombatantMarker> combatants = new HashSet<CombatantMarker>();

        public void Register(CombatantMarker combatant)
        {
            Assert.IsNotNull(combatant, "LocalCombatantRegistry: 登録する戦闘対象が未設定です。");
            Assert.IsTrue(combatant.IsIdentityValid, "LocalCombatantRegistry: 戦闘対象の識別情報が無効です。");
            combatants.Add(combatant);
        }

        public void Unregister(CombatantMarker combatant)
        {
            if (combatant != null)
            {
                combatants.Remove(combatant);
            }
        }

        public bool IsRegistered(CombatantMarker combatant)
        {
            return combatant != null && combatants.Contains(combatant);
        }
    }
}
