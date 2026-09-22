using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾判定部位（Hurtbox Type）の種別です。
    /// 部位ごとのダメージ倍率や将来のヘッドショット/シールド防御判定をサポートします。
    /// </summary>
    public enum HurtboxType
    {
        Torso = 0,
        Head = 1,
        Limb = 2,
        Shield = 3
    }

    /// <summary>
    /// 武器のHitboxと接触して被弾を受け止める受撃判定体（Hurtbox）の公開契約です。
    /// キャラクターの移動用コライダー（CharacterController等）と戦闘被弾判定を完全分離します。
    /// </summary>
    public interface ICombatHurtbox
    {
        /// <summary>このHurtboxが属する参戦者エンティティです。</summary>
        CombatantMarker Owner { get; }

        /// <summary>ダメージ適用先の生命値コンポーネントです。</summary>
        HealthComponent TargetHealth { get; }

        /// <summary>部位固有のダメージ倍率です（通常胴体: 1.0f）。</summary>
        float DamageMultiplier { get; }

        /// <summary>部位種別です。</summary>
        HurtboxType Type { get; }

        /// <summary>Hurtboxが現在被弾を受け付け可能かどうかです。</summary>
        bool IsActive { get; }

        /// <summary>Hurtboxに紐づく物理コライダーです。</summary>
        Collider HurtboxCollider { get; }
    }
}
