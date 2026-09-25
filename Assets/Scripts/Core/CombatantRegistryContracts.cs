namespace TinyAdventure
{
    /// <summary>
    /// 正式な戦闘対象として扱えるマーカーの共通情報を公開します。
    /// </summary>
    public interface ICombatant
    {
        CombatantMarker Marker { get; }
        string CombatantId { get; }
        CombatantMarker.CombatantFaction Faction { get; }
        bool IsAvailableForCombat { get; }
    }

    /// <summary>
    /// 戦闘対象の登録状態を一元管理する契約です。
    /// DamageRequest は登録済みの source と target だけを受理します。
    /// </summary>
    public interface ICombatantRegistry
    {
        CombatantMarker Player { get; }
        void Register(CombatantMarker combatant);
        void Unregister(CombatantMarker combatant);
        bool IsRegistered(CombatantMarker combatant);
    }
}
