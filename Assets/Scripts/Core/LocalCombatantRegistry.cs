using System.Collections.Generic;

namespace TinyAdventure
{
    /// <summary>
    /// シーン全体のレジストリが存在しない場合のフォールバック用インメモリ戦闘対象登録簿です。
    /// </summary>
    public sealed class LocalCombatantRegistry : ICombatantRegistry
    {
        private readonly HashSet<CombatantMarker> combatants = new HashSet<CombatantMarker>();

        public bool Register(CombatantMarker combatant)
        {
            return combatant != null && combatants.Add(combatant);
        }

        public bool Unregister(CombatantMarker combatant)
        {
            return combatant != null && combatants.Remove(combatant);
        }

        public bool IsRegistered(CombatantMarker combatant)
        {
            return combatant != null && combatants.Contains(combatant);
        }
    }
}
