using System;
using UnityEngine;
using UnityEngine.Assertions;

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
        public bool IsValid => IsFinite(Position) && IsFinite(Rotation) && Rotation.x * Rotation.x + Rotation.y * Rotation.y + Rotation.z * Rotation.z + Rotation.w * Rotation.w > 0.000001f && InitialHealth > 0f && !float.IsNaN(InitialHealth) && !float.IsInfinity(InitialHealth);

        /// <summary>
        /// 有効な初期配置を生成します。無効な値の場合はアサーションで中断します。
        /// </summary>
        public static SpawnSnapshot Create(
            Vector3 position,
            Quaternion rotation,
            float initialHealth)
        {
            Assert.IsTrue(IsFinite(position), "SpawnSnapshot: positionが有限値ではありません。");
            Assert.IsTrue(IsFinite(rotation) && rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w > 0.000001f, "SpawnSnapshot: rotationが不正です。");
            Assert.IsTrue(initialHealth > 0f && !float.IsNaN(initialHealth) && !float.IsInfinity(initialHealth), "SpawnSnapshot: initialHealthが正の有限値ではありません。");

            return new SpawnSnapshot(position, Normalize(rotation), initialHealth);
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
