using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 攻撃種別の安定した識別子です。ログ、友軍判定、テストで共通利用します。
    /// </summary>
    public static class AttackKinds
    {
        public const string KnightSword = "KnightSword";
        public const string EnemyMelee = "EnemyMelee";
    }

    /// <summary>
    /// DamageService に渡す、検証済みで不変の傷害要求です。
    /// </summary>
    public readonly struct DamageRequest
    {
        private DamageRequest(
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            Vector3 hitPoint,
            double timestamp)
        {
            Source = source;
            Target = target;
            Amount = amount;
            AttackSequenceId = attackSequenceId;
            AttackKind = attackKind;
            HitPoint = hitPoint;
            Timestamp = timestamp;
        }

        public CombatantMarker Source { get; }
        public CombatantMarker Target { get; }
        public float Amount { get; }
        public int AttackSequenceId { get; }
        public string AttackKind { get; }
        public Vector3 HitPoint { get; }
        public double Timestamp { get; }

        /// <summary>
        /// 登録状態を除く、要求自身の値契約を満たすかを返します。
        /// </summary>
        public bool IsStructurallyValid =>
            Source != null &&
            Target != null &&
            Source != Target &&
            Source.IsIdentityValid &&
            Target.IsIdentityValid &&
            IsFinitePositiveAmount(Amount) &&
            IsValidAttackSequenceId(AttackSequenceId) &&
            !string.IsNullOrWhiteSpace(AttackKind) &&
            IsFinite(HitPoint) &&
            GameplayClock.IsValidTimestamp(Timestamp);

        /// <summary>
        /// 有限かつ正のダメージ量を検査します。
        /// </summary>
        public static bool IsFinitePositiveAmount(float amount)
        {
            return !float.IsNaN(amount) && !float.IsInfinity(amount) && amount > 0f;
        }

        /// <summary>
        /// 攻撃系列を一意に追跡できる正の識別子かを検査します。
        /// </summary>
        public static bool IsValidAttackSequenceId(int attackSequenceId)
        {
            return attackSequenceId > 0;
        }

        /// <summary>
        /// 登録済みの source と target だけから正式なダメージ要求を生成します。
        /// </summary>
        public static Result<DamageRequest> Create(
            ICombatantRegistry registry,
            CombatantMarker source,
            CombatantMarker target,
            float amount,
            int attackSequenceId,
            string attackKind,
            Vector3 hitPoint,
            double timestamp)
        {
            UnityEngine.Assertions.Assert.IsNotNull(registry, "DamageRequest: 戦闘対象レジストリ参照が未設定です。");

            if (source == null || !source.IsIdentityValid)
            {
                return GameError.InvalidParameter;
            }

            if (target == null || !target.IsIdentityValid || source == target)
            {
                return GameError.InvalidParameter;
            }

            if (!registry.IsRegistered(source) || !registry.IsRegistered(target))
            {
                return GameError.CombatantNotRegistered;
            }

            if (!IsFinitePositiveAmount(amount))
            {
                return GameError.InvalidParameter;
            }

            if (!IsValidAttackSequenceId(attackSequenceId))
            {
                return GameError.InvalidParameter;
            }

            if (string.IsNullOrWhiteSpace(attackKind))
            {
                return GameError.InvalidParameter;
            }

            if (!IsFinite(hitPoint))
            {
                return GameError.InvalidParameter;
            }

            if (GameplayClock.ValidateTimestamp(timestamp).IsErr)
            {
                return GameError.InvalidParameter;
            }

            return new DamageRequest(source, target, amount, attackSequenceId, attackKind, hitPoint, timestamp);
        }

        /// <summary>
        /// 生成後にレジストリから解除されていないかを再検査します。
        /// </summary>
        public bool HasRegisteredParticipants(ICombatantRegistry registry)
        {
            return registry != null &&
                IsStructurallyValid &&
                registry.IsRegistered(Source) &&
                registry.IsRegistered(Target);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
