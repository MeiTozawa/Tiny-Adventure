using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 単一の攻撃動作に関する数値設定（ダメージ、射程、冷却、判定時間、速度感・突進）を保持する ScriptableObject です。
    /// 武器や攻撃種別ごとにアセットとして保存し、再利用・データ駆動化を可能にします。
    /// </summary>
    [CreateAssetMenu(fileName = "NewAttackConfig", menuName = "Tiny Adventure/Combat/Attack Config")]
    public class AttackConfig : ScriptableObject
    {
        [Header("ダメージと射程")]
        [Tooltip("この攻撃の基礎ダメージです。")]
        [SerializeField, Min(0f)]
        private float attackDamage;

        [Tooltip("この攻撃の有効射程（メートル）です。")]
        [SerializeField, Min(0f)]
        private float attackRange;

        [Header("タイミングと冷却")]
        [Tooltip("攻撃後のクールダウン時間（秒）です。")]
        [SerializeField, Min(0f)]
        private float attackCooldown;

        [Tooltip("攻撃有効ウィンドウを開くアニメーション正規化時間（前摇終了・出刀判定開始点）です。")]
        [SerializeField, Range(0f, 1f)]
        private float attackWindowOpenNormalizedTime;

        [Tooltip("攻撃有効ウィンドウを閉じるアニメーション正規化時間です。")]
        [SerializeField, Range(0f, 1f)]
        private float attackWindowCloseNormalizedTime;

        [Tooltip("攻撃動作完了とみなすアニメーション正規化時間です。")]
        [SerializeField, Range(0f, 1f)]
        private float attackCompletionNormalizedTime;

        [Header("速度感（Kinetics）")]
        [Tooltip("攻撃アニメーションの再生速度倍率です。")]
        [SerializeField, Min(0.01f)]
        private float attackSpeedMultiplier;

        public virtual float AttackDamage
        {
            get => attackDamage;
            set => attackDamage = Mathf.Max(0f, value);
        }

        public virtual float AttackRange
        {
            get => attackRange;
            set => attackRange = Mathf.Max(0f, value);
        }

        public virtual float AttackCooldown
        {
            get => attackCooldown;
            set => attackCooldown = Mathf.Max(0f, value);
        }

        public virtual float AttackWindowOpenNormalizedTime
        {
            get => attackWindowOpenNormalizedTime;
            set => attackWindowOpenNormalizedTime = Mathf.Clamp01(value);
        }

        public virtual float AttackWindowCloseNormalizedTime
        {
            get => attackWindowCloseNormalizedTime;
            set => attackWindowCloseNormalizedTime = Mathf.Clamp01(value);
        }

        public virtual float AttackCompletionNormalizedTime
        {
            get => attackCompletionNormalizedTime;
            set => attackCompletionNormalizedTime = Mathf.Clamp01(value);
        }

        public virtual float AttackSpeedMultiplier
        {
            get => attackSpeedMultiplier;
            set => attackSpeedMultiplier = Mathf.Max(0.01f, value);
        }

        private void OnValidate()
        {
            attackDamage = Mathf.Max(0f, attackDamage);
            attackRange = Mathf.Max(0f, attackRange);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            attackWindowOpenNormalizedTime = Mathf.Clamp01(attackWindowOpenNormalizedTime);
            attackWindowCloseNormalizedTime = Mathf.Clamp01(attackWindowCloseNormalizedTime);
            attackCompletionNormalizedTime = Mathf.Clamp01(attackCompletionNormalizedTime);
            attackSpeedMultiplier = Mathf.Max(0.01f, attackSpeedMultiplier);
        }
    }
}
