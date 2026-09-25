using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// ダメージ処理および戦闘対象登録の中央サービスインターフェース。
    /// </summary>
    public interface IDamageService : IDamageFeedbackSource
    {
        ICombatantRegistry CombatantRegistry { get; }
        IGameplayStateProvider GameplayStateProvider { get; }
        IGameplayClock Clock { get; }

        void RegisterCombatant(CombatantMarker combatant);
        void UnregisterCombatant(CombatantMarker combatant);

        Result Validate(DamageRequest request, AttackWindowTracker attackWindow);
        Result Submit(DamageRequest request, AttackWindowTracker attackWindow);
        Result Submit(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            AttackWindowTracker attackWindow,
            Vector3 hitPoint);
        Result Submit(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            AttackWindowTracker attackWindow);
    }
}
