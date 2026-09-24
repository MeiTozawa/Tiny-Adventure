using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// キャラクターの基礎ステータス（最大体力、移動速度、旋回速度など）を保持する ScriptableObject です。
    /// プレイヤーや敵の個体差・難易度ごとの数値をアセットとして管理します。
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterStatsConfig", menuName = "Tiny Adventure/Combat/Character Stats Config")]
    public class CharacterStatsConfig : ScriptableObject
    {
        [Header("体力設定")]
        [Tooltip("キャラクターの最大体力です。")]
        [SerializeField, Min(1f)]
        private float maximumHealth;

        [Header("移動設定")]
        [Tooltip("キャラクターの基礎移動速度です。")]
        [SerializeField, Min(0f)]
        private float moveSpeed;

        [Tooltip("キャラクターの旋回角速度（度/秒）です。")]
        [SerializeField, Min(0f)]
        private float turnSpeed;

        public float MaximumHealth
        {
            get => maximumHealth;
            set => maximumHealth = Mathf.Max(1f, value);
        }

        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(0f, value);
        }

        public float TurnSpeed
        {
            get => turnSpeed;
            set => turnSpeed = Mathf.Max(0f, value);
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1f, maximumHealth);
            moveSpeed = Mathf.Max(0f, moveSpeed);
            turnSpeed = Mathf.Max(0f, turnSpeed);
        }
    }
}
