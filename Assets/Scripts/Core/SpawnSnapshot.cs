using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 再開時に復元する戦闘ユニットの初期配置と初期体力です。
    /// </summary>
    public readonly struct SpawnSnapshot
    {
        private SpawnSnapshot(Vector3 position, Quaternion rotation, float initialHealth)
        {
            Position = position;
            Rotation = rotation;
            InitialHealth = initialHealth;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float InitialHealth { get; }

        /// <summary>保存済みの値だけで判断できる基本契約を満たすかを返します。</summary>
        public bool IsValid => IsFinite(Position) && IsFinite(Rotation) && Rotation.x * Rotation.x + Rotation.y * Rotation.y + Rotation.z * Rotation.z + Rotation.w * Rotation.w > 0.000001f && DamageRequest.IsFinitePositiveAmount(InitialHealth);

        /// <summary>
        /// 有効な初期配置を生成します。無効な値はスナップショットとして保存しません。
        /// </summary>
        public static bool TryCreate(
            Vector3 position,
            Quaternion rotation,
            float initialHealth,
            out SpawnSnapshot snapshot,
            out string diagnostic)
        {
            if (!IsFinite(position))
            {
                snapshot = default;
                diagnostic = "出現位置に有限でない値が含まれています。";
                return false;
            }

            if (!IsFinite(rotation) || rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w <= 0.000001f)
            {
                snapshot = default;
                diagnostic = "出現回転が無効です。";
                return false;
            }

            if (!DamageRequest.IsFinitePositiveAmount(initialHealth))
            {
                snapshot = default;
                diagnostic = "初期体力は有限かつ0より大きい値である必要があります。";
                return false;
            }

            snapshot = new SpawnSnapshot(position, Normalize(rotation), initialHealth);
            diagnostic = string.Empty;
            return true;
        }

        private static Quaternion Normalize(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            return new Quaternion(
                rotation.x / magnitude,
                rotation.y / magnitude,
                rotation.z / magnitude,
                rotation.w / magnitude);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
