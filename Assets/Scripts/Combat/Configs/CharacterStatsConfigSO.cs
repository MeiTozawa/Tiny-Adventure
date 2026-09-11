using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// キャラクターの基礎ステータス（最大体力、移動速度、旋回速度など）を保持する ScriptableObject です。
    /// プレイヤーや敵の個体差・難易度ごとの数値をアセットとして管理します。
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterStatsConfig", menuName = "Tiny Adventure/Combat/Character Stats Config")]
    public class CharacterStatsConfigSO : ScriptableObject
    {
        public const float MinimumMoveSpeed = 0.01f;
        public const float MinimumHealth = 1f;
        public const float MinimumTurnSpeed = 1f;

        [Header("体力設定")]
        [SerializeField, Min(MinimumHealth)]
        private float maximumHealth = 100f;

        [Header("移動設定")]
        [SerializeField, Min(MinimumMoveSpeed)]
        private float moveSpeed = 5f;

        [SerializeField, Min(MinimumTurnSpeed)]
        private float turnSpeed = 540f;

        public float MaximumHealth
        {
            get => maximumHealth;
            set => maximumHealth = Mathf.Max(MinimumHealth, value);
        }

        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(MinimumMoveSpeed, value);
        }

        public float TurnSpeed
        {
            get => turnSpeed;
            set => turnSpeed = Mathf.Max(MinimumTurnSpeed, value);
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(MinimumHealth, maximumHealth);
            moveSpeed = Mathf.Max(MinimumMoveSpeed, moveSpeed);
            turnSpeed = Mathf.Max(MinimumTurnSpeed, turnSpeed);
        }
    }
}
